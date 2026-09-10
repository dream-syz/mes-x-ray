import { useCallback, useEffect, useMemo, useReducer } from "react";
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

export type ViewMode = "architecture" | "trace";
export type InspectorTab = "details" | "trace" | "impact" | "explain";

export interface XRayState {
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
  tab: InspectorTab;
  busy: Partial<Record<"boot" | "live" | "details" | "trace" | "impact" | "explain", boolean>>;
  error: string | null;
  notice: string | null;
}

type Action =
  | { type: "booted"; overview: CaseOverview; architecture: Subgraph; responseFields: GraphNode[] }
  | { type: "busy"; key: keyof XRayState["busy"]; value: boolean }
  | { type: "error"; message: string | null }
  | { type: "notice"; message: string | null }
  | { type: "mode"; mode: ViewMode }
  | { type: "scope"; scope: string | null }
  | { type: "live"; trace: RuntimeTrace | null }
  | { type: "select"; nodeId: string; details: NodeDetailsResponse | null }
  | { type: "trace"; trace: FieldTrace | null }
  | { type: "impact"; impact: ImpactResult | null }
  | { type: "explanation"; explanation: ExplainResponse | null }
  | { type: "tab"; tab: InspectorTab };

const initial: XRayState = {
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
  tab: "details",
  busy: { boot: true },
  error: null,
  notice: null,
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
      return { ...state, explanation: action.explanation, tab: action.explanation ? "explain" : state.tab };
    case "tab":
      return { ...state, tab: action.tab };
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
}

/** Deep-link parameters for the demo script (docs/demo-script.md). */
export function scenarioFromUrl(search: string): Scenario | null {
  const params = new URLSearchParams(search);
  const scenario: Scenario = {
    order: params.get("order") ?? undefined,
    field: params.get("field") ?? undefined,
    scope: params.get("scope"),
    explain: params.get("explain") === "1" || params.get("investigate") === "1",
    investigate: params.get("investigate") === "1",
    impact: params.get("impact") ?? undefined,
    tab: (params.get("tab") as InspectorTab | null) ?? undefined,
  };
  return scenario.order || scenario.field || scenario.impact ? scenario : null;
}

export function useXRay(caseId = "pick-order-details") {
  const [state, dispatch] = useReducer(reduce, initial);

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
            const explanation = scenario.investigate && live ? await api.investigate(trace.field.id, live.traceId, scope) : await api.explain(trace.field.id, live?.traceId, scope);
            dispatch({ type: "explanation", explanation });
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
      if (scenario) void runScenario(scenario);
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
        const explanation = investigate && traceId ? await api.investigate(nodeId, traceId, scope, question) : await api.explain(nodeId, traceId, scope, question);
        dispatch({ type: "explanation", explanation });
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
        dispatch({ type: "notice", message: `Live trace ${trace.traceId} loaded from ${trace.environment} (${trace.evidence.length} evidence items).` });
        return trace;
      }),
    [run, state.scope],
  );

  const stopLiveTrace = useCallback(() => {
    dispatch({ type: "live", trace: null });
    dispatch({ type: "notice", message: "Live trace cleared; showing static lineage only." });
  }, []);

  const setMode = useCallback((mode: ViewMode) => dispatch({ type: "mode", mode }), []);
  const setTab = useCallback((tab: InspectorTab) => dispatch({ type: "tab", tab }), []);
  const setScope = useCallback((scope: string | null) => dispatch({ type: "scope", scope }), []);
  const dismissError = useCallback(() => dispatch({ type: "error", message: null }), []);

  const scopes = useMemo(() => {
    if (!state.live) return [];
    return Array.from(new Set(state.live.evidence.map((e) => e.scope).filter((s): s is string => !!s))).sort();
  }, [state.live]);

  return { state, scopes, selectNode, traceField, analyzeImpact, explain, startLiveTrace, stopLiveTrace, runScenario, setMode, setTab, setScope, dismissError };
}

export type XRayController = ReturnType<typeof useXRay>;
