using MesXray.Domain.Graph;
using MesXray.Scanner.DotNet.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MesXray.Scanner.DotNet.Rules;

/// <summary>
/// [HttpGet]/[HttpPost]... + [Route] -> API node HANDLED_BY the action; the action's (unwrapped) return model is
/// RETURNED by the API and every serialisable leaf of that model becomes a JSON field (API CONTAINS json,
/// field SERIALIZES_AS json).
/// </summary>
public sealed class EndpointRule
{
    private static readonly Dictionary<string, string> HttpAttributes = new(StringComparer.Ordinal)
    {
        ["HttpGet"] = "GET",
        ["HttpPost"] = "POST",
        ["HttpPut"] = "PUT",
        ["HttpDelete"] = "DELETE",
        ["HttpPatch"] = "PATCH",
    };

    private readonly ScanContext _ctx;

    public EndpointRule(ScanContext ctx)
    {
        _ctx = ctx;
    }

    public void Apply()
    {
        foreach (var controller in _ctx.Types.Types.Where(_ctx.Types.IsController).OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var declaration = _ctx.Types.DeclarationOf(controller);
            if (declaration is null)
            {
                continue;
            }

            var classRoute = RouteTemplate(declaration.AttributeLists, "Route") ?? string.Empty;
            classRoute = classRoute.Replace("[controller]", controller.Name.Replace("Controller", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);

            foreach (var action in declaration.Members.OfType<MethodDeclarationSyntax>())
            {
                var (httpMethod, template) = HttpVerb(action.AttributeLists);
                if (httpMethod is null)
                {
                    continue;
                }

                var route = Combine(classRoute, template ?? string.Empty)
                    .Replace("[action]", action.Identifier.Text, StringComparison.OrdinalIgnoreCase);
                var apiId = NodeIds.Api(httpMethod, route);
                var methodSymbol = _ctx.SemanticModelFor(action).GetDeclaredSymbol(action) as IMethodSymbol;
                var methodId = methodSymbol is null ? NodeIds.Method(controller.Name, action.Identifier.Text) : ScanContext.MethodId(methodSymbol);

                _ctx.Builder.Define(new Node
                {
                    Id = apiId,
                    Type = NodeType.Api,
                    Name = $"{httpMethod} {NodeIds.NormalizeRoute(route)}",
                    QualifiedName = $"{controller.Name}.{action.Identifier.Text}",
                    Layer = Layer.Api,
                    Source = _ctx.Location(action),
                    Metadata = new Dictionary<string, string>
                    {
                        ["httpMethod"] = httpMethod,
                        ["route"] = NodeIds.NormalizeRoute(route),
                        ["controller"] = controller.Name,
                        ["action"] = action.Identifier.Text,
                    },
                    Description = MethodRule.DocSummary(action),
                });

                var evidence = _ctx.EvidenceRef(action);
                _ctx.Builder.Link(apiId, RelationType.HandledBy, methodId, EvidenceType.Roslyn, evidence);

                if (methodSymbol is null)
                {
                    continue;
                }

                var responseType = ScanContext.Unwrap(methodSymbol.ReturnType);
                if (!_ctx.Types.IsModel(responseType))
                {
                    _ctx.Builder.Report(Domain.Scanning.ScanDiagnosticSeverity.Info,
                        $"Endpoint {apiId} returns '{methodSymbol.ReturnType.ToDisplayString()}' which is not a scanned model; JSON fields not generated.",
                        _ctx.RelativePath(action), _ctx.LineOf(action));
                    continue;
                }

                _ctx.Builder.Link(apiId, RelationType.Returns, ScanContext.ModelId(responseType), EvidenceType.Roslyn, evidence,
                    metadata: new Dictionary<string, string>
                    {
                        ["declaredType"] = methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        ["isArray"] = ScanContext.IsCollection(UnwrapAsync(methodSymbol.ReturnType)) ? "true" : "false",
                    });

                var rootIsArray = ScanContext.IsCollection(UnwrapAsync(methodSymbol.ReturnType));
                EmitJsonFields(apiId, (INamedTypeSymbol)responseType, path: string.Empty, qualifiedPath: rootIsArray ? "$[]" : "$", evidence, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));
            }
        }
    }

    private void EmitJsonFields(string apiId, INamedTypeSymbol model, string path, string qualifiedPath, string evidence, HashSet<INamedTypeSymbol> visiting)
    {
        if (!visiting.Add(model))
        {
            return; // recursive type
        }

        foreach (var property in ModelRule.PublicProperties(model))
        {
            var propertyDeclaration = property.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<PropertyDeclarationSyntax>().FirstOrDefault();
            var jsonName = ModelRule.JsonName(property, propertyDeclaration);
            var childPath = path.Length == 0 ? jsonName : $"{path}.{jsonName}";
            var isCollection = ScanContext.IsCollection(property.Type);
            var childQualified = $"{qualifiedPath}.{jsonName}{(isCollection ? "[]" : string.Empty)}";
            var unwrapped = ScanContext.Unwrap(property.Type);

            if (_ctx.Types.IsModel(unwrapped))
            {
                EmitJsonFields(apiId, (INamedTypeSymbol)unwrapped, childPath, childQualified, evidence, visiting);
                continue;
            }

            var jsonId = NodeIds.Json(childPath);
            _ctx.Builder.Define(new Node
            {
                Id = jsonId,
                Type = NodeType.JsonField,
                Name = jsonName,
                QualifiedName = childQualified,
                Layer = Layer.Web,
                Source = propertyDeclaration is null ? null : _ctx.Location(propertyDeclaration),
                Metadata = new Dictionary<string, string>
                {
                    ["jsonPath"] = childQualified,
                    ["clrType"] = property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    ["api"] = apiId,
                },
            });

            _ctx.Builder.Link(apiId, RelationType.Contains, jsonId, EvidenceType.Roslyn, evidence);
            var serializeEvidence = propertyDeclaration is null ? evidence : _ctx.EvidenceRef(propertyDeclaration);
            var edge = _ctx.Builder.Link(ScanContext.FieldId(property), RelationType.SerializesAs, jsonId, EvidenceType.Roslyn, serializeEvidence,
                confidence: 0.95,
                metadata: new Dictionary<string, string> { ["policy"] = "System.Text.Json camelCase (ASP.NET Core default)" });
            _ctx.Builder.AddLineage(new FieldLineage
            {
                OutputFieldId = jsonId,
                SourceFieldId = ScanContext.FieldId(property),
                TransformType = TransformType.Serialization,
                Expression = $"{property.Name} -> {jsonName} (camelCase)",
                EvidenceEdgeIds = [edge.Id],
                Confidence = 0.95,
            });
        }

        visiting.Remove(model);
    }

    private static ITypeSymbol UnwrapAsync(ITypeSymbol type)
    {
        while (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named
               && named.OriginalDefinition.Name is "Task" or "ValueTask" or "ActionResult")
        {
            type = named.TypeArguments[0];
        }

        return type;
    }

    private static (string? HttpMethod, string? Template) HttpVerb(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attribute in attributeLists.SelectMany(a => a.Attributes))
        {
            var name = attribute.Name.ToString().Replace("Attribute", string.Empty, StringComparison.Ordinal);
            if (HttpAttributes.TryGetValue(name, out var verb))
            {
                return (verb, FirstStringArgument(attribute));
            }
        }

        return (null, null);
    }

    private static string? RouteTemplate(SyntaxList<AttributeListSyntax> attributeLists, string attributeName)
    {
        var attribute = attributeLists.SelectMany(a => a.Attributes)
            .FirstOrDefault(a => a.Name.ToString().Replace("Attribute", string.Empty, StringComparison.Ordinal) == attributeName);
        return attribute is null ? null : FirstStringArgument(attribute);
    }

    private static string? FirstStringArgument(AttributeSyntax attribute)
        => attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax { Token.Value: string s } ? s : null;

    private static string Combine(string prefix, string template)
    {
        if (template.StartsWith('/') || template.StartsWith('~'))
        {
            return template.TrimStart('~');
        }

        return string.IsNullOrEmpty(prefix) ? template : $"{prefix.TrimEnd('/')}/{template}".TrimEnd('/');
    }
}
