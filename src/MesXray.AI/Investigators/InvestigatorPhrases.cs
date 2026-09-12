using MesXray.AI.Contracts;
using MesXray.AI.Evidence;
using MesXray.Domain.Graph;
using MesXray.Graph.Queries;

namespace MesXray.AI.Investigators;

/// <summary>
/// Language of the generated explanation text. Identifiers, node ids, SQL expressions, values and evidence ids are
/// never translated; only the sentences around them are.
/// </summary>
public static class AiLanguage
{
    public const string English = "en";
    public const string Chinese = "zh";

    /// <summary>Maps any BCP-47-ish tag (<c>zh</c>, <c>zh-CN</c>, <c>zh-Hans</c>) to a supported language; everything else is English.</summary>
    public static string Normalize(string? language)
        => language is not null && language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? Chinese : English;
}

/// <summary>Sentence templates of the rule-based investigator, one implementation per language.</summary>
internal abstract class InvestigatorPhrases
{
    public static readonly InvestigatorPhrases English = new EnglishPhrases();
    public static readonly InvestigatorPhrases Chinese = new ChinesePhrases();

    public static InvestigatorPhrases For(string? language) => AiLanguage.Normalize(language) == AiLanguage.Chinese ? Chinese : English;

    // ---- impact summary
    public abstract string ImpactReaches(string origin, string target, IEnumerable<string> path);
    public abstract string ImpactUnknownNode(string name, string id, NodeStatus status);
    public abstract string ImpactNoDownstream(string origin);
    public abstract string ImpactSummary(string origin, int count, IEnumerable<KeyValuePair<string, int>> summary);
    public abstract string ImpactStepStart(string originId);
    public abstract string ImpactStepFollowed(int count, bool truncated);
    public abstract string ImpactStepKeyPaths(int count);

    // ---- explain / investigate
    public abstract string StepResolvedFocus(string id, NodeType type, NodeStatus status);
    public abstract string SummaryUnknownFocus(string name);
    public abstract string NextScanFocus(string name);
    public abstract string FactObservedValue(string values, string? traceId, string? environment);
    public abstract string StepOverlaidTrace(string traceId, string? scope);
    public abstract string StepTraceNoValue(string traceId, string name);
    public abstract string StepNoLiveTrace();
    public abstract string FactExecutionPath(IEnumerable<string> names);
    public abstract string StepExecutionPath(int count);
    public abstract string StepWalkedHops(int count);
    public abstract string FactLink(string child, RelationType relation, string qualified, IReadOnlyList<string> observed);
    public abstract string DefaultProducedName { get; }
    public abstract string FactExpression(string produced, string expression);
    public abstract string FactBranchUses(string condition, string produced, IEnumerable<string> sources);
    public abstract string FactBranchUndecided(string produced, string control);
    public abstract string NextReadParameter(string control);
    public abstract string FactControlledBy(string produced, string control, string value);
    public abstract string HypothesisNoBranchMatches(string produced, string control, string value);
    public abstract string CheckCompareCase { get; }
    public abstract string FactActiveBranch(string condition, IEnumerable<string> sources);
    public abstract string StepEvaluatedCase(string produced, string control, string value);
    public abstract string HypothesisUnknownSource(string produced, string unknown);
    public abstract string CheckImportDefinition(string unknown);
    public abstract string NextScanDefinition(string unknown);
    public abstract string FactBaseColumns(IEnumerable<string> columns, IReadOnlyList<string> tables);
    public abstract string FactParameterFeeds(string parameter, string? value, string target);
    public abstract string NextFinishPending(string name);
    public abstract string StepCheckedDefinitions(int count);
    public abstract string UnknownNoRuntimeValue(string name, string traceId, string? scope);
    public abstract string NextRunTracePickOrder { get; }
    public abstract string Summary(string focus, int factCount, string? spName, IReadOnlyList<string> observed, ExplainVerdict verdict, IEnumerable<string> unknownNames, bool hypothesisOnly, int runtimeNotes);
    public abstract string HypothesisIsNullFallback(string produced, string observed, string expression);
    public abstract string CheckInnerFunction { get; }
    public abstract string FactLocalVariable(string produced, string note);
    public abstract string FactBranchConstant(string condition, string produced, string literal);
    public abstract string FactBranchDependsOnRows(string produced, IEnumerable<string> columns);
    public abstract string NextCheckRows(IReadOnlyList<string> tables);
    public abstract string HypothesisConstantBranch(string produced, string observed, string condition);
    public abstract string CheckConstantBranch(string condition);
    public abstract string Qualify(HopSummary hop);
}

internal sealed class EnglishPhrases : InvestigatorPhrases
{
    public override string ImpactReaches(string origin, string target, IEnumerable<string> path) => $"A change of {origin} reaches {target} via {string.Join(" -> ", path)}.";
    public override string ImpactUnknownNode(string name, string id, NodeStatus status) => $"{name} ({id}) is {status}: downstream effects beyond it cannot be confirmed.";
    public override string ImpactNoDownstream(string origin) => $"No downstream node depends on {origin}.";
    public override string ImpactSummary(string origin, int count, IEnumerable<KeyValuePair<string, int>> summary) => $"{origin} affects {count} downstream node(s): {string.Join(", ", summary.Select(kv => $"{kv.Value} {kv.Key}"))}.";
    public override string ImpactStepStart(string originId) => $"Started from {originId}.";
    public override string ImpactStepFollowed(int count, bool truncated) => $"Followed downstream (FlowsTo) and dependent (DependsOn) edges plus structural containment; {count} node(s) reached{(truncated ? " (truncated)" : string.Empty)}.";
    public override string ImpactStepKeyPaths(int count) => $"Selected {count} key path(s) ending at pages, APIs or JSON fields.";

    public override string StepResolvedFocus(string id, NodeType type, NodeStatus status) => $"Resolved focus node {id} ({type}, {status}).";
    public override string SummaryUnknownFocus(string name) => $"Unknown: {name} has no scanned definition, so nothing can be explained from evidence.";
    public override string NextScanFocus(string name) => $"Scan or import the definition of {name} and rebuild the graph.";
    public override string FactObservedValue(string values, string? traceId, string? environment) => $"Observed value: {values} (trace {traceId}, {environment}).";
    public override string StepOverlaidTrace(string traceId, string? scope) => $"Overlaid live trace {traceId}{(scope is null ? string.Empty : $" scoped to {scope}")}.";
    public override string StepTraceNoValue(string traceId, string name) => $"Live trace {traceId} carries no value for {name}.";
    public override string StepNoLiveTrace() => "No live trace supplied: static explanation only.";
    public override string FactExecutionPath(IEnumerable<string> names) => $"Execution path: {string.Join(" -> ", names)}.";
    public override string StepExecutionPath(int count) => $"Reconstructed the static execution path with {count} node(s).";
    public override string StepWalkedHops(int count) => $"Walked {count} upstream hop(s) through serialization, Dapper mapping, SQL aliases and expressions.";

    public override string FactLink(string child, RelationType relation, string qualified, IReadOnlyList<string> observed)
    {
        var value = observed.Count > 0 ? $" (observed {string.Join("; ", observed)})" : string.Empty;
        return $"{child} {Verb(relation)} {qualified}{value}.";
    }

    public override string DefaultProducedName => "value";
    public override string FactExpression(string produced, string expression) => $"{produced} = {expression}.";
    public override string FactBranchUses(string condition, string produced, IEnumerable<string> sources) => $"Branch `{condition}` of {produced} uses {string.Join(", ", sources)}.";
    public override string FactBranchUndecided(string produced, string control) => $"Which branch of {produced} applies is decided by system parameter {control}; its runtime value was not observed.";
    public override string NextReadParameter(string control) => $"Run read_system_parameter('{control}') to determine the active branch.";
    public override string FactControlledBy(string produced, string control, string value) => $"{produced} is controlled by {control} = {value} at runtime.";
    public override string HypothesisNoBranchMatches(string produced, string control, string value) => $"No branch condition of {produced} matches {control} = {value}; the CASE may yield NULL.";
    public override string CheckCompareCase => "Compare the CASE conditions with the observed parameter value.";
    public override string FactActiveBranch(string condition, IEnumerable<string> sources) => $"Active branch for this trace: `{condition}` -> {string.Join(", ", sources)}.";
    public override string StepEvaluatedCase(string produced, string control, string value) => $"Evaluated CASE conditions of {produced} against {control} = {value}.";
    public override string HypothesisUnknownSource(string produced, string unknown) => $"{produced} is determined inside {unknown}, whose definition is not available; the observed value cannot be verified further.";
    public override string CheckImportDefinition(string unknown) => $"Import the definition of {unknown} (or capture its return value in TEST) and rescan.";
    public override string NextScanDefinition(string unknown) => $"Scan the definition of {unknown}.";
    public override string FactBaseColumns(IEnumerable<string> columns, IReadOnlyList<string> tables) => $"Base table columns involved: {string.Join(", ", columns)}{(tables.Count > 0 ? $" (tables {string.Join(", ", tables)})" : string.Empty)}.";
    public override string FactParameterFeeds(string parameter, string? value, string target) => $"System parameter {parameter}{(value is null ? string.Empty : $" = {value}")} feeds {target}.";
    public override string NextFinishPending(string name) => $"Finish scanning {name} (marked Pending).";
    public override string StepCheckedDefinitions(int count) => $"Checked definitions: {count} Unknown/Pending node(s) on the path.";
    public override string UnknownNoRuntimeValue(string name, string traceId, string? scope) => $"No runtime value for {name} in trace {traceId}{(scope is null ? string.Empty : $" / {scope}")}.";
    public override string NextRunTracePickOrder => "Run trace_pick_order for the order to capture the value.";

    public override string Summary(string focus, int factCount, string? spName, IReadOnlyList<string> observed, ExplainVerdict verdict, IEnumerable<string> unknownNames, bool hypothesisOnly, int runtimeNotes)
    {
        var value = observed.Count > 0 ? $" Observed {string.Join("; ", observed)}." : string.Empty;
        var origin = spName is null ? string.Empty : $" It originates in {spName}.";
        var names = unknownNames.ToList();
        var caveat = verdict switch
        {
            ExplainVerdict.Known => " Every hop is backed by scanned code or runtime evidence." + (runtimeNotes > 0 ? $" {runtimeNotes} runtime detail(s) of this trace were not captured (see unknowns)." : string.Empty),
            _ when hypothesisOnly => " Need more evidence: the observed value is explained by an unverified hypothesis (see next steps).",
            _ when names.Count > 0 => $" Need more evidence: {string.Join(", ", names)}.",
            _ => " Need more evidence (see unknowns).",
        };
        return $"{focus} is explained through {factCount} evidence-backed fact(s).{origin}{value}{caveat}";
    }

    public override string HypothesisIsNullFallback(string produced, string observed, string expression) => $"{produced} equals the ISNULL fallback {observed} of `{expression}`; the inner value was probably NULL (no matching row, or the inner function returned NULL).";
    public override string CheckInnerFunction => "Evaluate the inner expression for this material in TEST (read-only) and check whether it is NULL.";
    public override string FactLocalVariable(string produced, string note) => $"Local variable in {produced}: {note}.";
    public override string FactBranchConstant(string condition, string produced, string literal) => $"Branch `{condition}` of {produced} returns the constant {literal}.";
    public override string FactBranchDependsOnRows(string produced, IEnumerable<string> columns) => $"Which branch of {produced} applies depends on row data: {string.Join(", ", columns)}; these rows were not captured in the trace.";
    public override string NextCheckRows(IReadOnlyList<string> tables) => $"Query {string.Join(", ", tables)} for this material in TEST (read-only) to determine the active branch.";
    public override string HypothesisConstantBranch(string produced, string observed, string condition) => $"{produced} equals the constant {observed} returned when {condition}; that condition probably held for this material.";
    public override string CheckConstantBranch(string condition) => $"Evaluate `{condition}` for this material in TEST (read-only) and confirm it holds.";

    public override string Qualify(HopSummary hop)
    {
        var owner = hop.ContainerNodeId is null ? null : NodeIds.OwnerKeyOf(hop.ContainerNodeId);
        var status = hop.Status == NodeStatus.Known ? string.Empty : $" [{hop.Status}]";
        return hop.Type switch
        {
            NodeType.Column when owner is not null => $"{owner}.{hop.Name}{status}",
            NodeType.ResultColumn or NodeType.IntermediateColumn when owner is not null => $"{owner}.{hop.Name}{status}",
            NodeType.Field when owner is not null => $"{NodeIds.LeafName(hop.ContainerNodeId!)}.{hop.Name}{status}",
            NodeType.Function => $"function {hop.Name}{status}",
            NodeType.SystemParameter => $"parameter {hop.Name}{status}",
            _ => $"{hop.Name}{status}",
        };
    }

    private static string Verb(RelationType relation) => relation switch
    {
        RelationType.SerializesAs => "is the JSON serialization of",
        RelationType.MapsTo => "is mapped by name (Dapper) from",
        RelationType.AliasOf => "is an alias of",
        RelationType.Returns => "is returned by",
        RelationType.EnrichedBy => "is enriched by",
        RelationType.Produces => "is produced by",
        RelationType.DerivedFrom => "derives from",
        RelationType.ComputedBy => "is computed by",
        RelationType.ControlledBy => "is controlled by",
        _ => relation.ToString().ToLowerInvariant(),
    };
}

internal sealed class ChinesePhrases : InvestigatorPhrases
{
    private const string Arrow = " -> ";

    public override string ImpactReaches(string origin, string target, IEnumerable<string> path) => $"{origin} 的变更会沿 {string.Join(Arrow, path)} 传导到 {target}。";
    public override string ImpactUnknownNode(string name, string id, NodeStatus status) => $"{name}（{id}）状态为{StatusName(status)}：其下游的影响无法确认。";
    public override string ImpactNoDownstream(string origin) => $"没有下游节点依赖 {origin}。";
    public override string ImpactSummary(string origin, int count, IEnumerable<KeyValuePair<string, int>> summary) => $"{origin} 影响 {count} 个下游节点：{string.Join("，", summary.Select(kv => $"{TypeName(kv.Key)} {kv.Value} 个"))}。";
    public override string ImpactStepStart(string originId) => $"从 {originId} 出发。";
    public override string ImpactStepFollowed(int count, bool truncated) => $"沿下游（FlowsTo）、依赖（DependsOn）和结构包含边遍历，到达 {count} 个节点{(truncated ? "（已截断）" : string.Empty)}。";
    public override string ImpactStepKeyPaths(int count) => $"选出 {count} 条终止于页面、API 或 JSON 字段的关键路径。";

    public override string StepResolvedFocus(string id, NodeType type, NodeStatus status) => $"定位焦点节点 {id}（{TypeName(type.ToString())}，{StatusName(status)}）。";
    public override string SummaryUnknownFocus(string name) => $"未知：{name} 没有已扫描的定义，无法基于证据给出解释。";
    public override string NextScanFocus(string name) => $"扫描或导入 {name} 的定义并重建图。";
    public override string FactObservedValue(string values, string? traceId, string? environment) => $"观测值：{values}（trace {traceId}，{environment}）。";
    public override string StepOverlaidTrace(string traceId, string? scope) => $"叠加实时 trace {traceId}{(scope is null ? string.Empty : $"（范围 {scope}）")}。";
    public override string StepTraceNoValue(string traceId, string name) => $"实时 trace {traceId} 中没有 {name} 的取值。";
    public override string StepNoLiveTrace() => "未提供实时 trace：仅做静态解释。";
    public override string FactExecutionPath(IEnumerable<string> names) => $"执行路径：{string.Join(Arrow, names)}。";
    public override string StepExecutionPath(int count) => $"重建静态执行路径，共 {count} 个节点。";
    public override string StepWalkedHops(int count) => $"沿序列化、Dapper 映射、SQL 别名和表达式向上游遍历 {count} 跳。";

    public override string FactLink(string child, RelationType relation, string qualified, IReadOnlyList<string> observed)
    {
        var value = observed.Count > 0 ? $"（观测值 {string.Join("；", observed)}）" : string.Empty;
        var sentence = relation switch
        {
            RelationType.SerializesAs => $"{child} 是 {qualified} 的 JSON 序列化结果",
            RelationType.MapsTo => $"{child} 由 Dapper 按名称从 {qualified} 映射而来",
            RelationType.AliasOf => $"{child} 是 {qualified} 的别名",
            RelationType.Returns => $"{child} 由 {qualified} 返回",
            RelationType.EnrichedBy => $"{child} 由 {qualified} 补充",
            RelationType.Produces => $"{child} 由 {qualified} 产生",
            RelationType.DerivedFrom => $"{child} 派生自 {qualified}",
            RelationType.ComputedBy => $"{child} 由 {qualified} 计算得出",
            RelationType.ControlledBy => $"{child} 受 {qualified} 控制",
            _ => $"{child} {relation.ToString().ToLowerInvariant()} {qualified}",
        };
        return $"{sentence}{value}。";
    }

    public override string DefaultProducedName => "该值";
    public override string FactExpression(string produced, string expression) => $"{produced} = {expression}。";
    public override string FactBranchUses(string condition, string produced, IEnumerable<string> sources) => $"{produced} 的分支 `{condition}` 使用 {string.Join("、", sources)}。";
    public override string FactBranchUndecided(string produced, string control) => $"{produced} 走哪个分支由系统参数 {control} 决定；其运行时取值未被观测到。";
    public override string NextReadParameter(string control) => $"执行 read_system_parameter('{control}') 以确定生效分支。";
    public override string FactControlledBy(string produced, string control, string value) => $"{produced} 在运行时受 {control} = {value} 控制。";
    public override string HypothesisNoBranchMatches(string produced, string control, string value) => $"{produced} 没有任何分支条件匹配 {control} = {value}；CASE 可能返回 NULL。";
    public override string CheckCompareCase => "将 CASE 条件与观测到的参数值逐一比对。";
    public override string FactActiveBranch(string condition, IEnumerable<string> sources) => $"本次 trace 生效的分支：`{condition}` -> {string.Join("、", sources)}。";
    public override string StepEvaluatedCase(string produced, string control, string value) => $"按 {control} = {value} 评估 {produced} 的 CASE 条件。";
    public override string HypothesisUnknownSource(string produced, string unknown) => $"{produced} 在 {unknown} 内部决定，而该对象的定义不可用；观测值无法继续验证。";
    public override string CheckImportDefinition(string unknown) => $"导入 {unknown} 的定义（或在 TEST 环境捕获其返回值）后重新扫描。";
    public override string NextScanDefinition(string unknown) => $"扫描 {unknown} 的定义。";
    public override string FactBaseColumns(IEnumerable<string> columns, IReadOnlyList<string> tables) => $"涉及的基表列：{string.Join("、", columns)}{(tables.Count > 0 ? $"（表 {string.Join("、", tables)}）" : string.Empty)}。";
    public override string FactParameterFeeds(string parameter, string? value, string target) => $"系统参数 {parameter}{(value is null ? string.Empty : $" = {value}")} 输入到 {target}。";
    public override string NextFinishPending(string name) => $"完成对 {name} 的扫描（当前标记为待补充）。";
    public override string StepCheckedDefinitions(int count) => $"检查定义：路径上有 {count} 个未知/待补充节点。";
    public override string UnknownNoRuntimeValue(string name, string traceId, string? scope) => $"trace {traceId}{(scope is null ? string.Empty : $" / {scope}")} 中没有 {name} 的运行时取值。";
    public override string NextRunTracePickOrder => "对该订单执行 trace_pick_order 以捕获取值。";

    public override string Summary(string focus, int factCount, string? spName, IReadOnlyList<string> observed, ExplainVerdict verdict, IEnumerable<string> unknownNames, bool hypothesisOnly, int runtimeNotes)
    {
        var value = observed.Count > 0 ? $"观测值 {string.Join("；", observed)}。" : string.Empty;
        var origin = spName is null ? string.Empty : $"它来源于 {spName}。";
        var names = unknownNames.ToList();
        var caveat = verdict switch
        {
            ExplainVerdict.Known => "每一跳都有已扫描代码或运行时证据支撑。" + (runtimeNotes > 0 ? $"本次 trace 有 {runtimeNotes} 项运行时细节未被捕获（见未知项）。" : string.Empty),
            _ when hypothesisOnly => "需要更多证据：观测值的解释仍是未验证的假设（见下一步）。",
            _ when names.Count > 0 => $"需要更多证据：{string.Join("、", names)}。",
            _ => "需要更多证据（见未知项）。",
        };
        return $"{focus} 由 {factCount} 条有证据支撑的事实解释。{origin}{value}{caveat}";
    }

    public override string HypothesisIsNullFallback(string produced, string observed, string expression) => $"{produced} 等于 `{expression}` 中 ISNULL 的回退值 {observed}；内层取值很可能为 NULL（没有匹配的行，或内层函数返回 NULL）。";
    public override string CheckInnerFunction => "在 TEST 环境只读执行内层表达式，检查该物料下其是否为 NULL。";
    public override string FactLocalVariable(string produced, string note) => $"{produced} 中的局部变量：{note}。";
    public override string FactBranchConstant(string condition, string produced, string literal) => $"{produced} 的分支 `{condition}` 返回常量 {literal}。";
    public override string FactBranchDependsOnRows(string produced, IEnumerable<string> columns) => $"{produced} 走哪个分支取决于行数据：{string.Join("、", columns)}；trace 中未捕获这些行。";
    public override string NextCheckRows(IReadOnlyList<string> tables) => $"在 TEST 环境只读查询 {string.Join("、", tables)} 中该物料的行，以确定生效分支。";
    public override string HypothesisConstantBranch(string produced, string observed, string condition) => $"{produced} 等于条件 {condition} 成立时返回的常量 {observed}；该条件很可能对此物料成立。";
    public override string CheckConstantBranch(string condition) => $"在 TEST 环境只读验证 `{condition}` 对该物料是否成立。";

    public override string Qualify(HopSummary hop)
    {
        var owner = hop.ContainerNodeId is null ? null : NodeIds.OwnerKeyOf(hop.ContainerNodeId);
        var status = hop.Status == NodeStatus.Known ? string.Empty : $"［{StatusName(hop.Status)}］";
        return hop.Type switch
        {
            NodeType.Column when owner is not null => $"{owner}.{hop.Name}{status}",
            NodeType.ResultColumn or NodeType.IntermediateColumn when owner is not null => $"{owner}.{hop.Name}{status}",
            NodeType.Field when owner is not null => $"{NodeIds.LeafName(hop.ContainerNodeId!)}.{hop.Name}{status}",
            NodeType.Function => $"函数 {hop.Name}{status}",
            NodeType.SystemParameter => $"参数 {hop.Name}{status}",
            _ => $"{hop.Name}{status}",
        };
    }

    private static string StatusName(NodeStatus status) => status switch
    {
        NodeStatus.Known => "已知",
        NodeStatus.Pending => "待补充",
        NodeStatus.Unknown => "未知",
        _ => status.ToString(),
    };

    /// <summary>Node type names as they appear in impact summaries (<see cref="ImpactResult.Summary"/> keys are enum names).</summary>
    private static string TypeName(string type) => type switch
    {
        "Page" => "页面",
        "JsonField" => "JSON 字段",
        "Api" => "API",
        "Method" => "方法",
        "Model" => "模型",
        "Field" => "属性",
        "StoredProcedure" => "存储过程",
        "ResultColumn" => "结果列",
        "Function" => "函数",
        "Table" => "表",
        "Column" => "列",
        "Intermediate" => "CTE / 临时表",
        "IntermediateColumn" => "中间列",
        "Expression" => "表达式",
        "SystemParameter" => "系统参数",
        "Branch" => "分支",
        _ => type,
    };
}
