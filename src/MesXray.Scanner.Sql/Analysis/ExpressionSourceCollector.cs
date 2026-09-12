using MesXray.Domain.Graph;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace MesXray.Scanner.Sql.Analysis;

/// <summary>How a source participates in an expression.</summary>
public enum SourceRole
{
    /// <summary>Contributes to the value.</summary>
    Value,

    /// <summary>Selects the branch of a CASE/IIF.</summary>
    Control,

    /// <summary>Determines ordering (WITHIN GROUP / OVER ORDER BY).</summary>
    Order,

    /// <summary>Partitioning of a window function.</summary>
    Partition,
}

/// <summary>
/// One upstream input of an expression, with the branch condition under which it applies. <see cref="Variable"/> names
/// the local variable through which the input arrived (<c>@UseQuantityAllocated</c>), when it did not come directly.
/// </summary>
public sealed record SourceRef(
    string NodeId,
    NodeType NodeType,
    string Name,
    string? QualifiedName,
    SourceRole Role,
    string? Condition,
    TransformType Transform,
    string? BranchExpression,
    string? Variable = null);

/// <summary>Services the collector needs from the enclosing object analyzer.</summary>
public interface ISourceResolver
{
    /// <summary>Resolves a column reference to a node; null when it cannot be resolved statically.</summary>
    (string NodeId, NodeType Type, string Name, string? QualifiedName)? ResolveColumn(ColumnReferenceExpression column);

    /// <summary>Resolves a local variable to the system parameter it was loaded from, if any.</summary>
    string? ParameterBoundTo(string variableName);

    /// <summary>
    /// Inputs of a plain local variable (not bound to a system parameter): the sources of every expression assigned to
    /// it plus, as control inputs, the columns of the <c>IF</c> predicates that guarded those assignments. Empty when
    /// the variable is a parameter of the object or was never assigned from data.
    /// </summary>
    IEnumerable<SourceRef> LocalVariableSources(string variableName, CollectContext context);

    /// <summary>Returns the system parameter read by this call (e.g. AF_GetSystemParameterValue('WMS_Enabled')), if it is one.</summary>
    string? SystemParameterRead(FunctionCall call);

    /// <summary>Returns (schema, name) when the call targets a user-defined function.</summary>
    (string Schema, string Name)? UserDefinedFunction(FunctionCall call);

    /// <summary>Called when the collector meets a scalar subquery so its tables can be registered as reads.</summary>
    IEnumerable<SourceRef> CollectSubquery(ScalarSubquery subquery, CollectContext context);

    /// <summary>
    /// Called for <c>EXISTS (subquery)</c>: the columns compared in the subquery's WHERE decide the predicate, so they
    /// are reported (with the caller's role, normally control); the tables are registered as reads.
    /// </summary>
    IEnumerable<SourceRef> CollectPredicateSubquery(ScalarSubquery subquery, CollectContext context);
}

/// <summary>Immutable traversal state.</summary>
public sealed record CollectContext(
    string? Condition,
    bool InAggregate,
    bool InConditional,
    bool InFunction,
    SourceRole Role,
    string? BranchExpression)
{
    public static readonly CollectContext Root = new(null, false, false, false, SourceRole.Value, null);

    public TransformType Transform(bool isFunctionSource) => this switch
    {
        { InConditional: true } => TransformType.Conditional,
        { InAggregate: true } => TransformType.Aggregate,
        _ when isFunctionSource || InFunction => TransformType.FunctionCall,
        _ => TransformType.Expression,
    };
}

/// <summary>
/// Walks a scalar expression and reports every column, parameter and user-defined function it depends on, together
/// with the CASE branch condition and the role (value / control / order) of each dependency.
/// </summary>
public sealed class ExpressionSourceCollector
{
    private static readonly HashSet<string> AggregateFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "MIN", "MAX", "COUNT", "COUNT_BIG", "AVG", "STRING_AGG", "STDEV", "VAR", "CHECKSUM_AGG",
    };

    private readonly ISourceResolver _resolver;

    public ExpressionSourceCollector(ISourceResolver resolver)
    {
        _resolver = resolver;
    }

    public List<SourceRef> Collect(ScalarExpression expression)
    {
        var sources = new List<SourceRef>();
        Visit(expression, CollectContext.Root, sources);
        return sources;
    }

    /// <summary>True when the expression is a constant with no upstream input.</summary>
    public static bool IsLiteral(ScalarExpression expression) => expression is Literal
        || (expression is ParenthesisExpression p && IsLiteral(p.Expression))
        || (expression is UnaryExpression u && IsLiteral(u.Expression));

    public static bool IsAggregate(FunctionCall call) => call.CallTarget is null && AggregateFunctions.Contains(call.FunctionName.Value);

    public static string Kind(ScalarExpression expression) => expression switch
    {
        Literal => "literal",
        ColumnReferenceExpression => "column",
        SimpleCaseExpression or SearchedCaseExpression or IIfCall => "case",
        FunctionCall f when IsAggregate(f) => "aggregate",
        FunctionCall { OverClause: not null } => "window",
        FunctionCall f when f.CallTarget is not null => "functionCall",
        FunctionCall => "builtin",
        CoalesceExpression or NullIfExpression => "builtin",
        CastCall or ConvertCall or TryCastCall or TryConvertCall => "cast",
        BinaryExpression => "arithmetic",
        ScalarSubquery => "subquery",
        _ => "expression",
    };

    private void Visit(ScalarExpression? expression, CollectContext ctx, List<SourceRef> sources)
    {
        switch (expression)
        {
            case null:
            case Literal:
                return;

            case ColumnReferenceExpression column:
                AddColumn(column, ctx, sources);
                return;

            case VariableReference variable:
                AddVariable(variable.Name, ctx, sources);
                return;

            case ParenthesisExpression p:
                Visit(p.Expression, ctx, sources);
                return;

            case UnaryExpression u:
                Visit(u.Expression, ctx, sources);
                return;

            case BinaryExpression b:
                Visit(b.FirstExpression, ctx, sources);
                Visit(b.SecondExpression, ctx, sources);
                return;

            case CoalesceExpression c:
                foreach (var e in c.Expressions)
                {
                    Visit(e, ctx, sources);
                }

                return;

            case NullIfExpression n:
                Visit(n.FirstExpression, ctx, sources);
                Visit(n.SecondExpression, ctx, sources);
                return;

            case CastCall cast:
                Visit(cast.Parameter, ctx, sources);
                return;

            case TryCastCall tryCast:
                Visit(tryCast.Parameter, ctx, sources);
                return;

            case ConvertCall convert:
                Visit(convert.Parameter, ctx, sources);
                return;

            case TryConvertCall tryConvert:
                Visit(tryConvert.Parameter, ctx, sources);
                return;

            case SimpleCaseExpression simpleCase:
                VisitSimpleCase(simpleCase, ctx, sources);
                return;

            case SearchedCaseExpression searchedCase:
                VisitSearchedCase(searchedCase, ctx, sources);
                return;

            case IIfCall iif:
                VisitBoolean(iif.Predicate, ctx with { Role = SourceRole.Control }, sources);
                var predicate = SqlText.Of(iif.Predicate);
                Visit(iif.ThenExpression, ctx with { Condition = predicate, InConditional = true, BranchExpression = SqlText.Of(iif.ThenExpression) }, sources);
                Visit(iif.ElseExpression, ctx with { Condition = "ELSE", InConditional = true, BranchExpression = SqlText.Of(iif.ElseExpression) }, sources);
                return;

            case FunctionCall call:
                VisitFunction(call, ctx, sources);
                return;

            case ScalarSubquery subquery:
                sources.AddRange(_resolver.CollectSubquery(subquery, ctx));
                return;

            default:
                // Unsupported scalar expression kinds are ignored on purpose: no evidence, no edge.
                return;
        }
    }

    private void VisitSimpleCase(SimpleCaseExpression simpleCase, CollectContext ctx, List<SourceRef> sources)
    {
        var inputText = SqlText.Of(simpleCase.InputExpression);
        Visit(simpleCase.InputExpression, ctx with { Role = SourceRole.Control }, sources);

        foreach (var when in simpleCase.WhenClauses)
        {
            var condition = $"{inputText} = {SqlText.Of(when.WhenExpression)}";
            Visit(when.WhenExpression, ctx with { Role = SourceRole.Control }, sources);
            Visit(when.ThenExpression, ctx with { Condition = condition, InConditional = true, BranchExpression = SqlText.Of(when.ThenExpression) }, sources);
        }

        if (simpleCase.ElseExpression is not null)
        {
            Visit(simpleCase.ElseExpression, ctx with { Condition = "ELSE", InConditional = true, BranchExpression = SqlText.Of(simpleCase.ElseExpression) }, sources);
        }
    }

    private void VisitSearchedCase(SearchedCaseExpression searchedCase, CollectContext ctx, List<SourceRef> sources)
    {
        foreach (var when in searchedCase.WhenClauses)
        {
            var condition = SqlText.Of(when.WhenExpression);
            VisitBoolean(when.WhenExpression, ctx with { Role = SourceRole.Control }, sources);
            Visit(when.ThenExpression, ctx with { Condition = condition, InConditional = true, BranchExpression = SqlText.Of(when.ThenExpression) }, sources);
        }

        if (searchedCase.ElseExpression is not null)
        {
            Visit(searchedCase.ElseExpression, ctx with { Condition = "ELSE", InConditional = true, BranchExpression = SqlText.Of(searchedCase.ElseExpression) }, sources);
        }
    }

    private void VisitFunction(FunctionCall call, CollectContext ctx, List<SourceRef> sources)
    {
        var parameterName = _resolver.SystemParameterRead(call);
        if (parameterName is not null)
        {
            sources.Add(new SourceRef(NodeIds.SystemParameter(parameterName), NodeType.SystemParameter, parameterName, null,
                ctx.Role, ctx.Condition, ctx.Transform(false), ctx.BranchExpression));
            return;
        }

        var udf = _resolver.UserDefinedFunction(call);
        var inner = ctx;
        if (udf is not null)
        {
            var (schema, name) = udf.Value;
            sources.Add(new SourceRef(NodeIds.Function(name, schema), NodeType.Function, name, $"{schema}.{name}",
                ctx.Role, ctx.Condition, ctx.Transform(true), ctx.BranchExpression));
            inner = ctx with { InFunction = true };
        }
        else if (IsAggregate(call))
        {
            inner = ctx with { InAggregate = true };
        }

        foreach (var argument in call.Parameters)
        {
            Visit(argument, inner, sources);
        }

        if (call.WithinGroupClause?.OrderByClause is not null)
        {
            foreach (var element in call.WithinGroupClause.OrderByClause.OrderByElements)
            {
                Visit(element.Expression, inner with { Role = SourceRole.Order }, sources);
            }
        }

        if (call.OverClause is not null)
        {
            foreach (var partition in call.OverClause.Partitions)
            {
                Visit(partition, ctx with { Role = SourceRole.Partition }, sources);
            }

            if (call.OverClause.OrderByClause is not null)
            {
                foreach (var element in call.OverClause.OrderByClause.OrderByElements)
                {
                    Visit(element.Expression, ctx with { Role = SourceRole.Order }, sources);
                }
            }
        }
    }

    /// <summary>Boolean expressions (CASE WHEN predicates, IIF) contribute control dependencies.</summary>
    public void VisitBoolean(BooleanExpression? predicate, CollectContext ctx, List<SourceRef> sources)
    {
        switch (predicate)
        {
            case null:
                return;
            case BooleanComparisonExpression cmp:
                Visit(cmp.FirstExpression, ctx, sources);
                Visit(cmp.SecondExpression, ctx, sources);
                return;
            case BooleanBinaryExpression bin:
                VisitBoolean(bin.FirstExpression, ctx, sources);
                VisitBoolean(bin.SecondExpression, ctx, sources);
                return;
            case BooleanParenthesisExpression paren:
                VisitBoolean(paren.Expression, ctx, sources);
                return;
            case BooleanNotExpression not:
                VisitBoolean(not.Expression, ctx, sources);
                return;
            case BooleanIsNullExpression isNull:
                Visit(isNull.Expression, ctx, sources);
                return;
            case BooleanTernaryExpression ternary:
                Visit(ternary.FirstExpression, ctx, sources);
                Visit(ternary.SecondExpression, ctx, sources);
                Visit(ternary.ThirdExpression, ctx, sources);
                return;
            case InPredicate inPredicate:
                Visit(inPredicate.Expression, ctx, sources);
                foreach (var v in inPredicate.Values)
                {
                    Visit(v, ctx, sources);
                }

                if (inPredicate.Subquery is not null)
                {
                    sources.AddRange(_resolver.CollectSubquery(inPredicate.Subquery, ctx));
                }

                return;
            case ExistsPredicate exists:
                sources.AddRange(_resolver.CollectPredicateSubquery(exists.Subquery, ctx));
                return;
            case LikePredicate like:
                Visit(like.FirstExpression, ctx, sources);
                Visit(like.SecondExpression, ctx, sources);
                return;
            default:
                return;
        }
    }

    private void AddColumn(ColumnReferenceExpression column, CollectContext ctx, List<SourceRef> sources)
    {
        var resolved = _resolver.ResolveColumn(column);
        if (resolved is null)
        {
            return;
        }

        var (nodeId, type, name, qualified) = resolved.Value;
        sources.Add(new SourceRef(nodeId, type, name, qualified, ctx.Role, ctx.Condition, ctx.Transform(false), ctx.BranchExpression));
    }

    private void AddVariable(string variableName, CollectContext ctx, List<SourceRef> sources)
    {
        var parameter = _resolver.ParameterBoundTo(variableName);
        if (parameter is null)
        {
            // Object parameter (nothing to say) or plain local variable: expand what was assigned to it.
            sources.AddRange(_resolver.LocalVariableSources(variableName, ctx));
            return;
        }

        sources.Add(new SourceRef(NodeIds.SystemParameter(parameter), NodeType.SystemParameter, parameter, null,
            ctx.Role, ctx.Condition, ctx.Transform(false), ctx.BranchExpression));
    }
}
