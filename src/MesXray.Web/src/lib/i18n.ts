import type { Layer, NodeStatus, NodeType } from "../api/types";

/**
 * UI languages. English is the product vocabulary of the design document; Simplified Chinese is the demo audience's
 * language. Identifiers, node ids, SQL, values and evidence ids are never translated, only the words around them.
 * The API produces its explanation text in the same language (`language` on the AI requests).
 */
export type Lang = "en" | "zh";

export const LANGS: { id: Lang; label: string }[] = [
  { id: "en", label: "EN" },
  { id: "zh", label: "中文" },
];

const en = {
  "app.booting": "Scanning the evidence graph",
  "app.apiUnreachable": "Cannot reach the X-Ray API: {error}",

  "header.tagline": "System explainability and data lineage",
  "header.viewMode": "View mode",
  "header.architecture": "Architecture",
  "header.liveTrace": "Live Trace",
  "header.pickOrder": "Pick order",
  "header.trace": "Trace",
  "header.tracing": "Tracing",
  "header.material": "Material",
  "header.orderLevel": "(order level)",
  "header.liveTitle": "Trace {traceId} observed {observedAt}",
  "header.clear": "Clear",
  "header.language": "Language",

  "tree.title": "Response Tree",
  "tree.static": "GET /cwp/v1/picking/pickOrder (static)",
  "tree.loading": "Loading response fields",
  "tree.footer": "Click a field to run Trace Source. Key demo fields are in accent.",
  "tree.expand": "expand",
  "tree.collapse": "collapse",
  "tree.traceFor": "Trace Source for {id}",

  "graph.title": "Evidence Graph",
  "graph.architecture": "Architecture: page, API, service, SQL",
  "graph.architectureHint": "Architecture (run a trace to see field lineage)",
  "graph.impactOf": "Impact of {name}",
  "graph.traceOf": "Trace Source {field}",
  "graph.truncated": "truncated",
  "graph.legendPath": "traced path, in data-flow direction",
  "graph.legendValue": "live value",
  "graph.legendGap": "unknown or pending",
  "graph.legendStructural": "structural",
  "graph.folded": "+{n} inside",
  "graph.unfolded": "fold internals",
  "graph.foldHint": "SQL function internals (RETURN expression, CTEs, base columns): click to fold or unfold",
  "graph.unfoldAll": "Unfold function internals (+{n})",
  "graph.foldAll": "Fold function internals",
  "graph.replay": "Replay flow",
  "graph.replayStop": "Stop replay",
  "graph.replayHint": "Replay the trace as motion: the call goes down the execution path, then the value comes back up the data flow into the field",
  "node.gap": "{status}: need more evidence",

  "inspector.title": "Inspector",
  "inspector.selectHint": "select a node or a response field",
  "inspector.traceSource": "Trace Source",
  "inspector.tracing": "Tracing",
  "inspector.impact": "Impact",
  "inspector.analyzing": "Analyzing",
  "inspector.explain": "Explain",
  "inspector.explaining": "Explaining",
  "inspector.investigate": "Investigate",
  "inspector.investigateHint": "Explain with the live trace and generate hypotheses",
  "inspector.runLiveFirst": "Run a Live Trace first",
  "inspector.questionPlaceholder": "Optional question, e.g. Why is Available Quantity 0?",
  "inspector.questionAria": "Question for the investigator",
  "inspector.views": "Inspector views",
  "tab.details": "Details",
  "tab.trace": "Trace Source",
  "tab.impact": "Impact",
  "tab.explain": "Explain",
  "tab.unknownHops": "{n} unknown hop(s)",
  "tab.allKnown": "Every hop is Known",
  "tab.affected": "{n} affected node(s)",
  "tab.facts": "{n} evidence-bound fact(s)",
  "empty.details": "Select a node in the graph or a field in the response tree.",
  "empty.trace": "Run Trace Source on a field to see its upstream lineage.",
  "empty.impact": "Run Impact on a parameter, column, function or procedure.",
  "empty.explain": "Explain produces evidence-bound facts. Anything without evidence is reported as Unknown / Need More Evidence.",

  "section.source": "Source",
  "section.liveValues": "Live values",
  "section.metadata": "Metadata",
  "section.lineageOut": "Lineage (as output)",
  "section.containedIn": "Contained in",
  "section.edges": "Edges ({n})",
  "details.noSourceLiteral": "(no source: literal value)",
  "details.noSourceUnresolved": "(no source: unresolved)",
  "details.outgoing": "outgoing",
  "details.incoming": "incoming",

  "trace.title": "Trace Source",
  "trace.live": "live {traceId}",
  "section.executionPath": "Execution path",
  "section.upstream": "Upstream lineage",
  "section.unknowns": "Unknowns",
  "trace.allKnown": "Every hop on this path is Known.",
  "trace.alreadyExpanded": "(already expanded)",
  "trace.evidenceIds": "{n} evidence ids",
  "chip.needMoreEvidence": "Need More Evidence",

  "impact.title": "Impact Analysis",
  "impact.affectedOne": "1 affected node",
  "impact.affectedMany": "{n} affected nodes",
  "section.keyPaths": "Key paths to the surface",
  "section.byDistance": "Affected by distance",
  "impact.depth": "depth {n}",

  "explain.title": "Explain",
  "explain.aiTitle": "AI Explain",
  "verdict.known": "Known",
  "verdict.needMoreEvidence": "Need More Evidence",
  "verdict.unknown": "Unknown",
  "metric.confidence": "confidence",
  "metric.evidenceItems": "evidence items",
  "metric.facts": "facts",
  "metric.unknowns": "unknowns",
  "explain.confidenceHint": "Confidence is capped by the evidence-binding validator",
  "section.steps": "Auditable steps",
  "section.facts": "Known facts, each bound to evidence",
  "section.hypotheses": "Hypotheses",
  "explain.noHypotheses": "None. Every claim is evidenced.",
  "explain.check": "Check (read-only): {check}",
  "explain.noGaps": "No gaps on this path.",
  "section.nextSteps": "Next steps",
  "section.audit": "Audit",
  "audit.provider": "provider",
  "audit.model": "model",
  "audit.prompt": "prompt",
  "audit.timestamp": "timestamp",
  "audit.evidenceIds": "evidence ids",
  "audit.note": "note",
  "explain.showBundle": "Show evidence bundle ({n})",
  "explain.hideBundle": "Hide evidence bundle ({n})",
  "hypothesis.unverified": "unverified",
  "hypothesis.supported": "supported",
  "hypothesis.refuted": "refuted",

  "status.readonly": "Read-only: whitelisted runtime tools only, no SQL execution, no parameter or data changes.",
  "status.dismiss": "dismiss",
  "status.buildTitle": "built {at}, mode {mode}",
  "metric.nodes": "nodes",
  "metric.edges": "edges",
  "metric.lineage": "lineage",
  "metric.unknown": "unknown",
  "metric.pending": "pending",
  "metric.knownGapOne": "known gap",
  "metric.knownGaps": "known gaps",

  "notice.liveLoaded": "Live trace {traceId} loaded from {env} ({n} evidence items).",
  "notice.liveCleared": "Live trace cleared; showing static lineage only.",
} as const;

export type MessageKey = keyof typeof en;

const zh: Record<MessageKey, string> = {
  "app.booting": "正在扫描证据图",
  "app.apiUnreachable": "无法连接 X-Ray API：{error}",

  "header.tagline": "系统可解释性与数据血缘",
  "header.viewMode": "视图模式",
  "header.architecture": "架构",
  "header.liveTrace": "实时追踪",
  "header.pickOrder": "拣货单",
  "header.trace": "追踪",
  "header.tracing": "追踪中",
  "header.material": "物料",
  "header.orderLevel": "（订单级）",
  "header.liveTitle": "Trace {traceId}，观测时间 {observedAt}",
  "header.clear": "清除",
  "header.language": "语言",

  "tree.title": "响应树",
  "tree.static": "GET /cwp/v1/picking/pickOrder（静态）",
  "tree.loading": "正在加载响应字段",
  "tree.footer": "点击字段执行溯源。演示重点字段以高亮色标出。",
  "tree.expand": "展开",
  "tree.collapse": "收起",
  "tree.traceFor": "对 {id} 执行溯源",

  "graph.title": "证据图",
  "graph.architecture": "架构：页面、API、服务、SQL",
  "graph.architectureHint": "架构（执行溯源后显示字段血缘）",
  "graph.impactOf": "{name} 的影响范围",
  "graph.traceOf": "溯源 {field}",
  "graph.truncated": "已截断",
  "graph.legendPath": "追踪路径，沿数据流方向",
  "graph.legendValue": "实时值",
  "graph.legendGap": "未知或待补充",
  "graph.legendStructural": "结构关系",
  "graph.folded": "内部 +{n}",
  "graph.unfolded": "折叠内部",
  "graph.foldHint": "SQL 函数内部（RETURN 表达式、CTE、基表列）：点击折叠或展开",
  "graph.unfoldAll": "展开函数内部（+{n}）",
  "graph.foldAll": "折叠函数内部",
  "graph.replay": "回放流转",
  "graph.replayStop": "停止回放",
  "graph.replayHint": "把这次追踪回放成动画：调用沿执行路径向下，取值再沿数据流向上回到字段",
  "node.gap": "{status}：需要更多证据",

  "inspector.title": "检视器",
  "inspector.selectHint": "选择一个节点或响应字段",
  "inspector.traceSource": "溯源",
  "inspector.tracing": "溯源中",
  "inspector.impact": "影响",
  "inspector.analyzing": "分析中",
  "inspector.explain": "解释",
  "inspector.explaining": "解释中",
  "inspector.investigate": "调查",
  "inspector.investigateHint": "结合实时 trace 解释并生成假设",
  "inspector.runLiveFirst": "请先执行实时追踪",
  "inspector.questionPlaceholder": "可选问题，例如：为什么 Available Quantity 是 0？",
  "inspector.questionAria": "向调查器提问",
  "inspector.views": "检视器视图",
  "tab.details": "详情",
  "tab.trace": "溯源",
  "tab.impact": "影响",
  "tab.explain": "解释",
  "tab.unknownHops": "{n} 个未知跳",
  "tab.allKnown": "每一跳均为已知",
  "tab.affected": "{n} 个受影响节点",
  "tab.facts": "{n} 条有证据支撑的事实",
  "empty.details": "在图中选择一个节点，或在响应树中选择一个字段。",
  "empty.trace": "对字段执行溯源以查看其上游血缘。",
  "empty.impact": "对参数、列、函数或存储过程执行影响分析。",
  "empty.explain": "解释只输出有证据支撑的事实；没有证据的内容一律报告为 Unknown / Need More Evidence。",

  "section.source": "来源",
  "section.liveValues": "实时值",
  "section.metadata": "元数据",
  "section.lineageOut": "血缘（作为输出）",
  "section.containedIn": "所属容器",
  "section.edges": "边（{n}）",
  "details.noSourceLiteral": "（无来源：字面值）",
  "details.noSourceUnresolved": "（无来源：未解析）",
  "details.outgoing": "出边",
  "details.incoming": "入边",

  "trace.title": "溯源",
  "trace.live": "实时 {traceId}",
  "section.executionPath": "执行路径",
  "section.upstream": "上游血缘",
  "section.unknowns": "未知项",
  "trace.allKnown": "该路径上的每一跳均为已知。",
  "trace.alreadyExpanded": "（已展开过）",
  "trace.evidenceIds": "{n} 个证据 id",
  "chip.needMoreEvidence": "需要更多证据",

  "impact.title": "影响分析",
  "impact.affectedOne": "1 个受影响节点",
  "impact.affectedMany": "{n} 个受影响节点",
  "section.keyPaths": "到表层的关键路径",
  "section.byDistance": "按距离分组",
  "impact.depth": "深度 {n}",

  "explain.title": "解释",
  "explain.aiTitle": "AI 解释",
  "verdict.known": "已知",
  "verdict.needMoreEvidence": "需要更多证据",
  "verdict.unknown": "未知",
  "metric.confidence": "置信度",
  "metric.evidenceItems": "证据项",
  "metric.facts": "事实",
  "metric.unknowns": "未知项",
  "explain.confidenceHint": "置信度受证据绑定校验器限制",
  "section.steps": "可审计步骤",
  "section.facts": "已知事实，每条均绑定证据",
  "section.hypotheses": "假设",
  "explain.noHypotheses": "无。所有结论均有证据。",
  "explain.check": "核查（只读）：{check}",
  "explain.noGaps": "该路径没有缺口。",
  "section.nextSteps": "下一步",
  "section.audit": "审计",
  "audit.provider": "提供方",
  "audit.model": "模型",
  "audit.prompt": "提示词版本",
  "audit.timestamp": "时间戳",
  "audit.evidenceIds": "证据 id",
  "audit.note": "备注",
  "explain.showBundle": "显示证据包（{n}）",
  "explain.hideBundle": "隐藏证据包（{n}）",
  "hypothesis.unverified": "未验证",
  "hypothesis.supported": "成立",
  "hypothesis.refuted": "已否定",

  "status.readonly": "只读：仅白名单运行时工具，不执行 SQL，不修改参数或业务数据。",
  "status.dismiss": "关闭",
  "status.buildTitle": "构建于 {at}，模式 {mode}",
  "metric.nodes": "节点",
  "metric.edges": "边",
  "metric.lineage": "血缘",
  "metric.unknown": "未知",
  "metric.pending": "待补充",
  "metric.knownGapOne": "已知缺口",
  "metric.knownGaps": "已知缺口",

  "notice.liveLoaded": "已从 {env} 加载实时 trace {traceId}（{n} 项证据）。",
  "notice.liveCleared": "已清除实时 trace，仅显示静态血缘。",
};

const MESSAGES: Record<Lang, Record<MessageKey, string>> = { en, zh };

export type Vars = Record<string, string | number | null | undefined>;

export function translate(lang: Lang, key: MessageKey, vars?: Vars): string {
  const template = MESSAGES[lang][key] ?? en[key];
  if (!vars) return template;
  return template.replace(/\{(\w+)\}/g, (match, name: string) => {
    const value = vars[name];
    return value === undefined || value === null ? match : String(value);
  });
}

// ---------------------------------------------------------------- domain labels

const TYPE_LABELS: Record<Lang, Record<NodeType, string>> = {
  en: {
    page: "Page",
    jsonField: "JSON field",
    api: "API",
    method: "Method",
    model: "Model",
    field: "Property",
    storedProcedure: "Stored procedure",
    resultColumn: "Result column",
    function: "Function",
    table: "Table",
    column: "Column",
    intermediate: "CTE / temp table",
    intermediateColumn: "Intermediate column",
    expression: "Expression",
    systemParameter: "System parameter",
    branch: "Branch",
  },
  zh: {
    page: "页面",
    jsonField: "JSON 字段",
    api: "API",
    method: "方法",
    model: "模型",
    field: "属性",
    storedProcedure: "存储过程",
    resultColumn: "结果列",
    function: "函数",
    table: "表",
    column: "列",
    intermediate: "CTE / 临时表",
    intermediateColumn: "中间列",
    expression: "表达式",
    systemParameter: "系统参数",
    branch: "分支",
  },
};

const STATUS_LABELS: Record<Lang, Record<NodeStatus, string>> = {
  en: { known: "Known", pending: "Pending", unknown: "Unknown - Need More Evidence" },
  zh: { known: "已知", pending: "待补充", unknown: "未知（需要更多证据）" },
};

/** Short status word for tight spots (graph nodes). */
const STATUS_SHORT: Record<Lang, Record<NodeStatus, string>> = {
  en: { known: "Known", pending: "Pending", unknown: "Unknown" },
  zh: { known: "已知", pending: "待补充", unknown: "未知" },
};

const LAYER_LABELS: Record<Lang, Record<Layer, string>> = {
  en: { web: "Web", api: "API", service: "Service", data: "Data", config: "Config" },
  zh: { web: "Web", api: "API", service: "服务", data: "数据", config: "配置" },
};

const RELATION_LABELS: Record<Lang, Record<string, string>> = {
  en: {
    calls: "calls",
    handledBy: "handled by",
    executesSp: "executes SP",
    returns: "returns",
    mapsTo: "maps to",
    serializesAs: "serializes as",
    enrichedBy: "enriched by",
    aliasOf: "alias of",
    derivedFrom: "derived from",
    controlledBy: "controlled by",
    computedBy: "computed by",
    produces: "produces",
    reads: "reads",
    usesParameter: "uses parameter",
    contains: "contains",
    ofType: "of type",
    hasBranch: "has branch",
  },
  zh: {
    calls: "调用",
    handledBy: "由其处理",
    executesSp: "执行 SP",
    returns: "返回",
    mapsTo: "映射到",
    serializesAs: "序列化为",
    enrichedBy: "补充自",
    aliasOf: "别名",
    derivedFrom: "派生自",
    controlledBy: "受控于",
    computedBy: "计算自",
    produces: "产生",
    reads: "读取",
    usesParameter: "使用参数",
    contains: "包含",
    ofType: "类型为",
    hasBranch: "含分支",
  },
};

export const typeLabel = (lang: Lang, type: NodeType): string => TYPE_LABELS[lang][type] ?? type;
export const statusLabel = (lang: Lang, status: NodeStatus): string => STATUS_LABELS[lang][status] ?? status;
export const statusShort = (lang: Lang, status: NodeStatus): string => STATUS_SHORT[lang][status] ?? status;
export const layerLabel = (lang: Lang, layer: Layer): string => LAYER_LABELS[lang][layer] ?? layer;
export const relationLabel = (lang: Lang, relation: string | null | undefined): string =>
  relation ? RELATION_LABELS[lang][relation] ?? RELATION_LABELS.en[relation] ?? relation.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase() : "";

// ---------------------------------------------------------------- detection and persistence

export const LANG_STORAGE_KEY = "mes-xray.lang";

const asLang = (value: string | null | undefined): Lang | null => (value === "en" || value === "zh" ? value : value?.toLowerCase().startsWith("zh") ? "zh" : value?.toLowerCase().startsWith("en") ? "en" : null);

/** `?lang=` wins (deep links for the demo), then the saved choice, then the browser language. */
export function detectLang(search: string): Lang {
  const fromUrl = asLang(new URLSearchParams(search).get("lang"));
  if (fromUrl) return fromUrl;
  try {
    const saved = asLang(window.localStorage.getItem(LANG_STORAGE_KEY));
    if (saved) return saved;
  } catch {
    // storage may be unavailable (privacy mode); fall through
  }
  return asLang(typeof navigator !== "undefined" ? navigator.language : null) ?? "en";
}

/**
 * Remembers the choice and lets the browser pick CJK fonts and line-breaking rules. With `updateUrl` the `lang`
 * parameter is written into the address bar as well, so the current view can be shared in that language.
 */
export function persistLang(lang: Lang, options: { updateUrl?: boolean } = {}): void {
  try {
    window.localStorage.setItem(LANG_STORAGE_KEY, lang);
  } catch {
    // ignore
  }
  document.documentElement.lang = lang === "zh" ? "zh-CN" : "en";
  if (options.updateUrl) {
    const url = new URL(window.location.href);
    url.searchParams.set("lang", lang);
    window.history.replaceState(window.history.state, "", url);
  }
}
