using System.Text.RegularExpressions;
using MesXray.Domain.Graph;
using MesXray.Domain.Scanning;
using MesXray.Scanner.DotNet.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MesXray.Scanner.DotNet.Rules;

/// <summary>
/// Walks every method body once and applies the body-level rules from the design document:
/// <list type="bullet">
/// <item>Method Call: METHOD -> CALLS -> METHOD (interface calls resolved to in-source implementations; guard condition recorded).</item>
/// <item>Dapper SP: METHOD -> EXECUTES_SP -> SP, plus SP -> MAPS_TO -> MODEL for the generic result type.</item>
/// <item>Property Assignment: FIELD -> ENRICHED_BY -> METHOD, or FIELD -> MAPS_TO -> FIELD for plain property copies.</item>
/// <item>Branch: METHOD -> BRANCHES_ON -> FIELD for if-conditions reading a model property.</item>
/// </list>
/// </summary>
public sealed partial class MethodBodyRule
{
    private static readonly HashSet<string> DapperMethods = new(StringComparer.Ordinal)
    {
        "Query", "QueryAsync", "QueryFirst", "QueryFirstAsync", "QueryFirstOrDefault", "QueryFirstOrDefaultAsync",
        "QuerySingle", "QuerySingleAsync", "QuerySingleOrDefault", "QuerySingleOrDefaultAsync",
        "QueryMultiple", "QueryMultipleAsync", "Execute", "ExecuteAsync", "ExecuteScalar", "ExecuteScalarAsync",
    };

    private readonly ScanContext _ctx;

    public MethodBodyRule(ScanContext ctx)
    {
        _ctx = ctx;
    }

    public void Apply()
    {
        foreach (var type in _ctx.Types.Types.Where(t => t.TypeKind == TypeKind.Class))
        {
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary))
            {
                var declaration = method.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<MethodDeclarationSyntax>().FirstOrDefault();
                if (declaration is null || (declaration.Body is null && declaration.ExpressionBody is null))
                {
                    continue;
                }

                SyntaxNode body = (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody!;
                var callerId = ScanContext.MethodId(method);

                foreach (var node in body.DescendantNodes())
                {
                    switch (node)
                    {
                        case InvocationExpressionSyntax invocation:
                            HandleInvocation(callerId, invocation);
                            break;
                        case AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } assignment:
                            HandleAssignment(callerId, assignment);
                            break;
                        case IfStatementSyntax ifStatement:
                            HandleBranch(callerId, ifStatement);
                            break;
                    }
                }
            }
        }
    }

    // ----- Method calls & Dapper -----

    private void HandleInvocation(string callerId, InvocationExpressionSyntax invocation)
    {
        if (TryHandleDapper(callerId, invocation))
        {
            return;
        }

        var symbol = _ctx.ResolveMethod(invocation);
        if (symbol is null)
        {
            return; // unresolvable external call (LINQ, BCL, third-party); not a graph fact.
        }

        var target = symbol.ReducedFrom ?? symbol.OriginalDefinition;
        if (!_ctx.Types.IsDeclaredInSource(target))
        {
            return;
        }

        var guard = GuardOf(invocation);
        var evidence = _ctx.EvidenceRef(invocation);

        if (target.ContainingType.TypeKind == TypeKind.Interface)
        {
            var implementations = _ctx.Types.ImplementationsOf(target.ContainingType);
            if (implementations.Count == 0)
            {
                var ifaceId = ScanContext.MethodId(target);
                _ctx.Builder.Reference(ifaceId, NodeType.Method, target.Name, Layer.Service,
                    $"Interface member {ScanContext.QualifiedName(target)} has no implementation in the scanned source.", ScanContext.QualifiedName(target));
                _ctx.Builder.Link(callerId, RelationType.Calls, ifaceId, EvidenceType.Roslyn, evidence, 0.8, WithGuard(guard, ("via", target.ContainingType.Name)));
                return;
            }

            var confidence = implementations.Count == 1 ? 0.95 : 0.7;
            if (implementations.Count > 1)
            {
                _ctx.Builder.Report(ScanDiagnosticSeverity.Warning,
                    $"{target.ContainingType.Name}.{target.Name} has {implementations.Count} implementations; all are linked with reduced confidence.",
                    _ctx.RelativePath(invocation), _ctx.LineOf(invocation));
            }

            foreach (var impl in implementations)
            {
                var implMethod = impl.FindImplementationForInterfaceMember(target) as IMethodSymbol;
                var calleeId = implMethod is null ? NodeIds.Method(impl.Name, target.Name) : ScanContext.MethodId(implMethod);
                _ctx.Builder.Link(callerId, RelationType.Calls, calleeId, EvidenceType.Roslyn, evidence, confidence, WithGuard(guard, ("via", target.ContainingType.Name)));
            }

            return;
        }

        _ctx.Builder.Link(callerId, RelationType.Calls, ScanContext.MethodId(target), EvidenceType.Roslyn, evidence, 1.0, WithGuard(guard));
    }

    private bool TryHandleDapper(string callerId, InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return false;
        }

        var methodName = member.Name.Identifier.Text;
        if (!DapperMethods.Contains(methodName))
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 0)
        {
            return false;
        }

        var isStoredProcedure = arguments.Any(a => a.Expression.ToString().EndsWith("CommandType.StoredProcedure", StringComparison.Ordinal));
        var commandArgument = arguments.FirstOrDefault(a => a.NameColon is null || a.NameColon.Name.Identifier.Text is "sql" or "commandText") ?? arguments[0];
        var evidence = _ctx.EvidenceRef(invocation);

        string spId;
        double confidence;
        var evidenceType = EvidenceType.Roslyn;
        var edgeEvidence = evidence;
        var metadata = new Dictionary<string, string>
        {
            ["dapperMethod"] = methodName,
            ["commandType"] = isStoredProcedure ? "StoredProcedure" : "unspecified",
        };

        if (commandArgument.Expression is LiteralExpressionSyntax { Token.Value: string literal })
        {
            var parsed = ParseSqlObjectName(literal);
            if (parsed is null)
            {
                if (!isStoredProcedure)
                {
                    // Inline SQL text: out of scope for the SP rule; record it so nobody assumes it was analysed.
                    _ctx.Builder.Report(ScanDiagnosticSeverity.Info, $"Inline SQL in {callerId} was not analysed: {Truncate(literal)}", _ctx.RelativePath(invocation), _ctx.LineOf(invocation));
                    return true;
                }

                parsed = (NodeIds.DefaultSchema, literal.Trim());
            }

            spId = NodeIds.StoredProcedure(parsed.Value.Name, parsed.Value.Schema);
            confidence = isStoredProcedure ? 1.0 : 0.8;
            _ctx.Builder.Reference(spId, NodeType.StoredProcedure, parsed.Value.Name, Layer.Data,
                "Stored procedure referenced from .NET; definition comes from the SQL scanner.", $"{parsed.Value.Schema}.{parsed.Value.Name}");
        }
        else if (ConfiguredProcedure(commandArgument.Expression, invocation) is { } configured)
        {
            // e.g. _options.StorageBinProcedure with PickingOptions.StorageBinProcedure in the site settings: the name is
            // not in the code, so the evidence is the configuration entry, cited next to the call site.
            spId = NodeIds.StoredProcedure(configured.Name, configured.Schema);
            confidence = 0.9;
            evidenceType = EvidenceType.Configuration;
            edgeEvidence = _ctx.SiteSettings.EvidenceRef(configured.SettingKey);
            metadata["resolvedFrom"] = configured.SettingKey;
            metadata["configuredValue"] = configured.Setting.Value;
            metadata["codeRef"] = evidence;
            metadata["evidenceRefs"] = $"{evidence};{edgeEvidence}";
            if (configured.Setting.Provided is not null)
            {
                metadata["provided"] = configured.Setting.Provided;
            }

            _ctx.Builder.Reference(spId, NodeType.StoredProcedure, configured.Name, Layer.Data,
                $"Stored procedure named by site configuration ({configured.SettingKey}); definition comes from the SQL scanner.", $"{configured.Schema}.{configured.Name}");
            _ctx.Builder.Report(ScanDiagnosticSeverity.Info,
                $"{callerId} executes the procedure configured in {configured.SettingKey}: {configured.Setting.Value}.",
                _ctx.RelativePath(invocation), _ctx.LineOf(invocation));
        }
        else
        {
            // e.g. _options.StorageBinProcedure without a site setting: the name is not a compile-time constant.
            var expressionText = commandArgument.Expression.ToString();
            var placeholderName = $"<{expressionText}>";
            spId = NodeIds.StoredProcedure(placeholderName);
            confidence = 0.5;
            metadata["unresolvedExpression"] = expressionText;
            _ctx.Builder.Reference(spId, NodeType.StoredProcedure, placeholderName, Layer.Data,
                "Procedure name is not a compile-time constant.");
            _ctx.Builder.Report(ScanDiagnosticSeverity.Warning,
                $"{callerId} executes a procedure whose name is computed at runtime ({expressionText}); marked Unknown.",
                _ctx.RelativePath(invocation), _ctx.LineOf(invocation));
        }

        var guard = GuardOf(invocation);
        _ctx.Builder.Link(callerId, RelationType.ExecutesSp, spId, evidenceType, edgeEvidence, confidence, WithGuard(guard, metadata.Select(kv => (kv.Key, kv.Value)).ToArray()));

        // Generic result type: QueryAsync<CWPPickOrderRow>(...) -> Dapper maps result columns onto the model by name.
        if (member.Name is GenericNameSyntax generic && generic.TypeArgumentList.Arguments.Count == 1)
        {
            var typeInfo = _ctx.SemanticModelFor(generic).GetTypeInfo(generic.TypeArgumentList.Arguments[0]).Type
                           ?? _ctx.SemanticModelFor(generic).GetSymbolInfo(generic.TypeArgumentList.Arguments[0]).Symbol as ITypeSymbol;
            if (typeInfo is not null && _ctx.Types.IsModel(typeInfo))
            {
                _ctx.Builder.Link(spId, RelationType.MapsTo, ScanContext.ModelId(typeInfo), EvidenceType.Roslyn, evidence, confidence,
                    new Dictionary<string, string> { ["strategy"] = "DapperByName", ["resultType"] = typeInfo.Name });
            }
        }

        return true;
    }

    private sealed record ConfiguredProcedureName(string SettingKey, SiteSetting Setting, string Schema, string Name);

    /// <summary>
    /// A command argument that reads an options property (<c>_options.StorageBinProcedure</c>) resolves to the value the
    /// site binds to that property, when the site settings carry one. Anything else stays unresolved.
    /// </summary>
    private ConfiguredProcedureName? ConfiguredProcedure(ExpressionSyntax commandExpression, InvocationExpressionSyntax invocation)
    {
        if (_ctx.SiteSettings.Count == 0 || _ctx.ResolveProperty(commandExpression) is not { } property)
        {
            return null;
        }

        if (!_ctx.SiteSettings.TryGet(property.ContainingType.Name, property.Name, out var setting))
        {
            return null;
        }

        var key = SiteSettings.Key(property.ContainingType.Name, property.Name);
        var parsed = ParseSqlObjectName(setting.Value);
        if (parsed is null)
        {
            _ctx.Builder.Report(ScanDiagnosticSeverity.Warning,
                $"Site setting {key} = '{setting.Value}' is not a SQL object name; the procedure stays Unknown.",
                _ctx.RelativePath(invocation), _ctx.LineOf(invocation));
            return null;
        }

        return new ConfiguredProcedureName(key, setting, parsed.Value.Schema, parsed.Value.Name);
    }

    // ----- Property assignments -----

    private void HandleAssignment(string callerId, AssignmentExpressionSyntax assignment)
    {
        var targetProperty = ResolveAssignmentTarget(assignment);
        if (targetProperty is null || !_ctx.Types.IsModel(targetProperty.ContainingType))
        {
            return;
        }

        var targetId = ScanContext.FieldId(targetProperty);
        var value = UnwrapValue(assignment.Right);
        var evidence = _ctx.EvidenceRef(assignment);
        var guard = GuardOf(assignment);

        if (value is InvocationExpressionSyntax invocation)
        {
            var callee = _ctx.ResolveMethod(invocation);
            var target = callee?.ReducedFrom ?? callee?.OriginalDefinition;
            if (target is null || !_ctx.Types.IsDeclaredInSource(target))
            {
                return;
            }

            foreach (var calleeId in ResolveCallees(target))
            {
                _ctx.Builder.Link(targetId, RelationType.EnrichedBy, calleeId, EvidenceType.Roslyn, evidence, 1.0,
                    WithGuard(guard, ("assignment", Truncate(assignment.ToString()))));
            }

            return;
        }

        if (value is MemberAccessExpressionSyntax or IdentifierNameSyntax)
        {
            var sourceProperty = _ctx.ResolveProperty(value);
            if (sourceProperty is null || !_ctx.Types.IsModel(sourceProperty.ContainingType))
            {
                return;
            }

            var sourceId = ScanContext.FieldId(sourceProperty);
            var edge = _ctx.Builder.Link(sourceId, RelationType.MapsTo, targetId, EvidenceType.Roslyn, evidence, 1.0,
                WithGuard(guard, ("strategy", "Assignment"), ("assignment", Truncate(assignment.ToString()))));
            _ctx.Builder.AddLineage(new FieldLineage
            {
                OutputFieldId = targetId,
                SourceFieldId = sourceId,
                TransformType = TransformType.Direct,
                Expression = Truncate(assignment.ToString()),
                Condition = guard?.Condition,
                EvidenceEdgeIds = [edge.Id],
            });
        }
    }

    private IPropertySymbol? ResolveAssignmentTarget(AssignmentExpressionSyntax assignment)
    {
        if (assignment.Left is MemberAccessExpressionSyntax)
        {
            return _ctx.ResolveProperty(assignment.Left);
        }

        // Object initializer: new Model { Prop = value }
        if (assignment.Left is IdentifierNameSyntax && assignment.Parent is InitializerExpressionSyntax)
        {
            return _ctx.ResolveProperty(assignment.Left);
        }

        return null;
    }

    private IEnumerable<string> ResolveCallees(IMethodSymbol target)
    {
        if (target.ContainingType.TypeKind != TypeKind.Interface)
        {
            yield return ScanContext.MethodId(target);
            yield break;
        }

        var implementations = _ctx.Types.ImplementationsOf(target.ContainingType);
        if (implementations.Count == 0)
        {
            yield return ScanContext.MethodId(target);
            yield break;
        }

        foreach (var impl in implementations)
        {
            yield return impl.FindImplementationForInterfaceMember(target) is IMethodSymbol m ? ScanContext.MethodId(m) : NodeIds.Method(impl.Name, target.Name);
        }
    }

    private static ExpressionSyntax UnwrapValue(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case AwaitExpressionSyntax await:
                    expression = await.Expression;
                    continue;
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppress:
                    expression = suppress.Operand;
                    continue;
                case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } coalesce:
                    expression = coalesce.Left;
                    continue;
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "ToList" or "ToArray" or "AsList" } chain }:
                    expression = chain.Expression;
                    continue;
                default:
                    return expression;
            }
        }
    }

    // ----- Branches -----

    private void HandleBranch(string callerId, IfStatementSyntax ifStatement)
    {
        foreach (var access in ifStatement.Condition.DescendantNodesAndSelf().Where(n => n is MemberAccessExpressionSyntax or IdentifierNameSyntax))
        {
            if (access.Parent is MemberAccessExpressionSyntax parentAccess && parentAccess.Name == access)
            {
                continue; // handled through the parent member access
            }

            var property = _ctx.ResolveProperty((ExpressionSyntax)access);
            if (property is null || !_ctx.Types.IsModel(property.ContainingType))
            {
                continue;
            }

            _ctx.Builder.Link(callerId, RelationType.BranchesOn, ScanContext.FieldId(property), EvidenceType.Roslyn, _ctx.EvidenceRef(ifStatement.Condition), 1.0,
                new Dictionary<string, string>
                {
                    ["condition"] = Normalize(ifStatement.Condition.ToString()),
                    ["hasElse"] = ifStatement.Else is null ? "false" : "true",
                });
        }
    }

    // ----- Guard conditions -----

    private sealed record Guard(string Condition, string Branch);

    /// <summary>The innermost enclosing if/else (within the same method) that guards <paramref name="node"/>.</summary>
    private static Guard? GuardOf(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null && current is not MethodDeclarationSyntax and not LocalFunctionStatementSyntax and not LambdaExpressionSyntax; current = current.Parent)
        {
            if (current is IfStatementSyntax ifStatement)
            {
                if (ifStatement.Statement.Span.Contains(node.Span))
                {
                    return new Guard(Normalize(ifStatement.Condition.ToString()), "then");
                }

                if (ifStatement.Else is not null && ifStatement.Else.Span.Contains(node.Span))
                {
                    return new Guard(Normalize(ifStatement.Condition.ToString()), "else");
                }
            }
        }

        return null;
    }

    private static Dictionary<string, string> WithGuard(Guard? guard, params (string Key, string Value)[] extra)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (guard is not null)
        {
            metadata["condition"] = guard.Condition;
            metadata["branch"] = guard.Branch;
        }

        foreach (var (key, value) in extra)
        {
            metadata[key] = value;
        }

        return metadata;
    }

    // ----- helpers -----

    private static (string Schema, string Name)? ParseSqlObjectName(string text)
    {
        var match = SqlObjectName().Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }

        var schema = match.Groups["schema"].Success ? match.Groups["schema"].Value : NodeIds.DefaultSchema;
        return (schema, match.Groups["name"].Value);
    }

    [GeneratedRegex(@"^(?:\[?(?<schema>[A-Za-z_][\w]*)\]?\.)?\[?(?<name>[A-Za-z_][\w]*)\]?$")]
    private static partial Regex SqlObjectName();

    private static string Normalize(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static string Truncate(string text)
    {
        var normalized = Normalize(text);
        return normalized.Length <= 160 ? normalized : normalized[..157] + "...";
    }
}
