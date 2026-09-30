import { useCallback, useEffect, useMemo, useReducer, useRef } from "react";
import { api, ApiError } from "../api/client";
import type {
  CaseOverview,
  ExplainResponse,
  FieldTrace,
  GraphNode,
  ImpactResult,
  NodeDetailsResponse,
  RuntimeTrace,
  Subgraph,
} from "../api/types";
import { detectLang, persistLang, translate, type Lang, type MessageKey, type Vars } from "../lib/i18n";

export type ViewMode = "architecture" | "trace";
export type InspectorTab = "details" | "trace" | "impact" | "explain";

/** What produced the current explanation, so it can be re-requested in another language. */
export interface ExplainRequest {
  nodeId: string;
  investigate: boolean;
  question?: string;
}

export interface XRayState {
  lang: Lang;
  overview: CaseOverview | null;
  architecture: Subgraph | null;
  responseFields: GraphNode[];
  mode: ViewMode;
  live: RuntimeTrace | null;
  scope: string | null;
  selectedNodeId: string | null;
  details: NodeDetailsResponse | null;
  trace: FieldTrace | null;
  impact: ImpactResult | null;
  explanation: ExplainResponse | null;
  explainRequest: ExplainRequest | null;
  tab: InspectorTab;
  busy: Partial<Record<"boot" | "live" | "details" | "trace" | "impact" | "explain", boolean>>;
  error: string | null;
  notice: string | null;
  /** Deep link `replay=1` / `replay=hold`: replay the flow of every trace as it arrives (or arm it and wait for the button). */
  replayOnLoad: ReplayOnLoad | null;
}

export type ReplayOnLoad = "auto" | "hold";

type Action =
  | { type: "booted"; overview: CaseOverview; architecture: Subgraph; responseFields: GraphNode[] }
  | { type: "busy"; key: keyof XRayState["busy"]; value: boolean }
  | { type: "error"; message: string | null }
  | { type: "notice"; message: string | null }
  | { type: "lang"; lang: Lang }
  | { type: "mode"; mode: ViewMode }
  | { type: "scope"; scope: string | null }
  | { type: "live"; trace: RuntimeTrace | null }
  | { type: "select"; nodeId: string; details: NodeDetailsResponse | null }
  | { type: "trace"; trace: FieldTrace | null }
  | { type: "impact"; impact: ImpactResult | null }
  | { type: "explanation"; explanation: ExplainResponse | null; request: ExplainRequest | null; keepTab?: boolean }
  | { type: "tab"; tab: InspectorTab }
  | { type: "replayOnLoad"; mode: ReplayOnLoad | null };

const initial: XRayState = {
  lang: "en",
  overview: null,
  architecture: null,
  responseFields: [],
  mode: "architecture",
  live: null,
  scope: null,
  selectedNodeId: null,
  details: null,
  trace: null,
  impact: null,
  explanation: null,
  explainRequest: null,
  tab: "details",
  busy: { boot: true },
  error: null,
  notice: null,
  replayOnLoad: null,
};

function reduce(state: XRayState, action: Action): XRayState {
  switch (action.type) {
    case "booted":
      return { ...state, overview: action.overview, architecture: action.architecture, responseFields: action.responseFields, scope: action.overview.case.defaultScope ?? null };
    case "busy":
      return { ...state, busy: { ...state.busy, [action.key]: action.value } };
    case "error":
      return { ...state, error: action.message };
    case "notice":
      return { ...state, notice: action.message };
    case "lang":
      return { ...state, lang: action.lang };
    case "mode":
      return { ...state, mode: action.mode };
    case "scope":
      return { ...state, scope: action.scope };
    case "live":
      return { ...state, live: action.trace, mode: action.trace ? "trace" : state.mode };
    case "select":
      return { ...state, selectedNodeId: action.nodeId, details: action.details };
    case "trace":
      return { ...state, trace: action.trace, tab: action.trace ? "trace" : state.tab, mode: action.trace ? "trace" : state.mode };
    case "impact":
      return { ...state, impact: action.impact, tab: action.impact ? "impact" : state.tab, mode: action.impact ? "trace" : state.mode };
    case "explanation":
      return { ...state, explanation: action.explanation, explainRequest: action.request, tab: action.explanation && !action.keepTab ? "explain" : state.tab };
    case "tab":
      return { ...state, tab: action.tab };
    case "replayOnLoad":
      return { ...state, replayOnLoad: action.mode };
    default:
      return state;
  }
}

const describe = (error: unknown): string => {
  if (error instanceof ApiError) return `${error.status}: ${error.message}`;
  if (error instanceof Error) return error.message;
  return String(error);
};

export interface Scenario {
  order?: string;
  field?: string;
  scope?: string | null;
  explain?: boolean;
  investigate?: boolean;
  impact?: string;
  tab?: InspectorTab;
  /** `replay=1` plays the flow of the trace once it is on screen; `replay=hold` darkens the path and waits for the button. */
  replay?: ReplayOnLoad;
}

/** Deep-link parameters for the demo script (docs/demo-script.md). */
export function scenarioFromUrl(search: string): Scenario | null {
  const params = new URLSearchParams(search);
  const replay = params.get("replay");
  const scenario: Scenario = {
    order: params.get("order") ?? undefined,
    field: params.get("field") ?? undefined,
    scope: params.get("scope"),
    explain: params.get("explain") === "1" || params.get("investigate") === "1",
    investigate: params.get("investigate") === "1",
    impact: params.get("impact") ?? undefined,
    tab: (params.get("tab") as InspectorTab | null) ?? undefined,
    replay: replay === "1" ? "auto" : replay === "hold" ? "hold" : undefined,
  };
  return scenario.order || scenario.field || scenario.impact ? scenario : null;
}

export function useXRay(caseId = "pick-order-details") {
  const [state, dispatch] = useReducer(reduce, initial, (base) => ({ ...base, lang: detectLang(window.location.search) }));

  // Callbacks read the language through a ref so that changing it never re-creates them (the boot effect depends on
  // runScenario and must not run twice).
  const langRef = useRef(state.lang);
  langRef.current = state.lang;
  const userChangedLang = useRef(false);
  useEffect(() => persistLang(state.lang, { updateUrl: userChangedLang.current }), [state.lang]);
  const t = useCallback((key: MessageKey, vars?: Vars) => translate(langRef.current, key, vars), []);

  const run = useCallback(async <T,>(key: keyof XRayState["busy"], work: () => Promise<T>): Promise<T | undefined> => {
    dispatch({ type: "busy", key, value: true });
    dispatch({ type: "error", message: null });
    try {
      return await work();
    } catch (error) {
      dispatch({ type: "error", message: describe(error) });
      return undefined;
    } finally {
      dispatch({ type: "busy", key, value: false });
    }
  }, []);

  /**
   * Runs a whole demo step in one go (live trace -> field trace -> explain) without depending on intermediate
   * React state. Used by deep links such as `?order=PICK0843858&field=availableQuantity&scope=T12288&explain=1`.
   */
  const runScenario = useCallback(
    (scenario: Scenario) =>
      run("live", async () => {
        let live: RuntimeTrace | null = null;
        if (scenario.order) {
          live = await api.livePickOrder(scenario.order, null);
          dispatch({ type: "live", trace: live });
        }
        const scope = live ? scenario.scope ?? null : null;
        if (live) dispatch({ type: "scope", scope });
        if (scenario.field) {
          const trace = await api.trace(scenario.field, live?.traceId, scope);
          dispatch({ type: "trace", trace });
          const details = await api.node(trace.field.id, live?.traceId, scope);
          dispatch({ type: "select", nodeId: trace.field.id, details });
          if (scenario.explain) {
            const investigate = !!(scenario.investigate && live);
            const explanation =
              investigate && live
                ? await api.investigate(trace.field.id, live.traceId, scope, undefined, langRef.current)
                : await api.explain(trace.field.id, live?.traceId, scope, undefined, langRef.current);
            dispatch({ type: "explanation", explanation, request: { nodeId: trace.field.id, investigate } });
          }
          dispatch({ type: "tab", tab: scenario.tab ?? (scenario.explain ? "explain" : "trace") });
        }
        if (scenario.impact) {
          const impact = await api.impact(scenario.impact);
          dispatch({ type: "impact", impact });
          const details = await api.node(impact.origin.id, live?.traceId, scope);
          dispatch({ type: "select", nodeId: impact.origin.id, details });
          dispatch({ type: "tab", tab: "impact" });
        }
      }),
    [run],
  );

  // Boot: case overview, architecture view and the static list of response fields, then any deep-linked scenario.
  useEffect(() => {
    void run("boot", async () => {
      const overview = await api.case(caseId);
      const [architecture, apiSubgraph] = await Promise.all([api.architecture(overview.case.rootNodeId), api.subgraph(overview.case.apiNodeId, 1, true)]);
      const responseFields = apiSubgraph.nodes.filter((n) => n.type === "jsonField").sort((a, b) => a.id.localeCompare(b.id));
      dispatch({ type: "booted", overview, architecture, responseFields });
    }).then(() => {
      const scenario = scenarioFromUrl(window.location.search);
      if (!scenario) return;
      if (scenario.replay) dispatch({ type: "replayOnLoad", mode: scenario.replay });
      void runScenario(scenario);
    });
  }, [caseId, run, runScenario]);

  const selectNode = useCallback(
    (nodeId: string) =>
      run("details", async () => {
        const details = await api.node(nodeId, state.live?.traceId, state.scope);
        dispatch({ type: "select", nodeId, details });
        return details;
      }),
    [run, state.live?.traceId, state.scope],
  );

  const traceField = useCallback(
    (field: string, scopeOverride?: string | null) =>
      run("trace", async () => {
        const scope = scopeOverride === undefined ? state.scope : scopeOverride;
        if (scopeOverride !== undefined) dispatch({ type: "scope", scope: scopeOverride });
        const trace = await api.trace(field, state.live?.traceId, state.live ? scope : null);
        dispatch({ type: "trace", trace });
        const details = await api.node(trace.field.id, state.live?.traceId, state.live ? scope : null);
        dispatch({ type: "select", nodeId: trace.field.id, details });
        return trace;
      }),
    [run, state.live, state.scope],
  );

  const analyzeImpact = useCallback(
    (nodeId: string) =>
      run("impact", async () => {
        const impact = await api.impact(nodeId);
        dispatch({ type: "impact", impact });
        return impact;
      }),
    [run],
  );

  const explain = useCallback(
    (nodeId: string, investigate: boolean, question?: string) =>
      run("explain", async () => {
        const traceId = state.live?.traceId ?? null;
        const scope = state.live ? state.scope : null;
        const lang = langRef.current;
        const explanation = investigate && traceId ? await api.investigate(nodeId, traceId, scope, question, lang) : await api.explain(nodeId, traceId, scope, question, lang);
        dispatch({ type: "explanation", explanation, request: { nodeId, investigate: !!(investigate && traceId), question } });
        return explanation;
      }),
    [run, state.live, state.scope],
  );

  const startLiveTrace = useCallback(
    (orderNo: string, materialNo?: string | null) =>
      run("live", async () => {
        const trace = await api.livePickOrder(orderNo, materialNo);
        dispatch({ type: "live", trace });
        const scopes = Array.from(new Set(trace.evidence.map((e) => e.scope).filter((s): s is string => !!s)));
        const scope = materialNo && scopes.includes(materialNo) ? materialNo : state.scope && scopes.includes(state.scope) ? state.scope : scopes[0] ?? null;
        dispatch({ type: "scope", scope });
        dispatch({ type: "notice", message: t("notice.liveLoaded", { traceId: trace.traceId, env: trace.environment, n: trace.evidence.length }) });
        return trace;
      }),
    [run, state.scope, t],
  );

  const stopLiveTrace = useCallback(() => {
    dispatch({ type: "live", trace: null });
    dispatch({ type: "notice", message: t("notice.liveCleared") });
  }, [t]);

  /** Switches the UI language and re-requests the open explanation so its sentences follow (ids and evidence are unchanged). */
  const setLang = useCallback(
    (lang: Lang) => {
      if (lang === langRef.current) return;
      langRef.current = lang;
      userChangedLang.current = true;
      dispatch({ type: "lang", lang });
      if (state.notice) dispatch({ type: "notice", message: null });
      const request = state.explainRequest;
      if (!request) return;
      void run("explain", async () => {
        const traceId = state.live?.traceId ?? null;
        const scope = state.live ? state.scope : null;
        const explanation =
          request.investigate && traceId
            ? await api.investigate(request.nodeId, traceId, scope, request.question, lang)
            : await api.explain(request.nodeId, traceId, scope, request.question, lang);
        dispatch({ type: "explanation", explanation, request, keepTab: true });
        return explanation;
      });
    },
    [run, state.explainRequest, state.live, state.scope, state.notice],
  );

  const setMode = useCallback((mode: ViewMode) => dispatch({ type: "mode", mode }), []);
  const setTab = useCallback((tab: InspectorTab) => dispatch({ type: "tab", tab }), []);
  const setScope = useCallback((scope: string | null) => dispatch({ type: "scope", scope }), []);
  const dismissError = useCallback(() => dispatch({ type: "error", message: null }), []);

  const scopes = useMemo(() => {
    if (!state.live) return [];
    return Array.from(new Set(state.live.evidence.map((e) => e.scope).filter((s): s is string => !!s))).sort();
  }, [state.live]);

  return { state, scopes, selectNode, traceField, analyzeImpact, explain, startLiveTrace, stopLiveTrace, runScenario, setLang, setMode, setTab, setScope, dismissError };
}

export type XRayController = ReturnType<typeof useXRay>;
