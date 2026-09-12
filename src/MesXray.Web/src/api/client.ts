import type {
  CaseOverview,
  ExplainResponse,
  FieldTrace,
  ImpactResult,
  ImpactSummaryResponse,
  NodeDetailsResponse,
  ProblemDetails,
  RuntimeTrace,
  Subgraph,
  ToolResult,
} from "./types";

const BASE = "/api/xray";

/** Error carrying the RFC 9457 problem details returned by the API. */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails | undefined;

  constructor(status: number, problem: ProblemDetails | undefined, fallback: string) {
    super(problem?.detail ?? problem?.title ?? fallback);
    this.status = status;
    this.problem = problem;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${BASE}${path}`, {
    ...init,
    headers: { accept: "application/json", ...(init?.body ? { "content-type": "application/json" } : {}), ...init?.headers },
  });
  if (!response.ok) {
    let problem: ProblemDetails | undefined;
    try {
      problem = (await response.json()) as ProblemDetails;
    } catch {
      problem = undefined;
    }
    throw new ApiError(response.status, problem, `${response.status} ${response.statusText}`);
  }
  return (await response.json()) as T;
}

const q = (params: Record<string, string | number | boolean | null | undefined>): string => {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== "") {
      search.set(key, String(value));
    }
  }
  const text = search.toString();
  return text ? `?${text}` : "";
};

export const api = {
  case: (caseId: string) => request<CaseOverview>(`/cases/${encodeURIComponent(caseId)}`),
  cases: () => request<{ id: string; title: string }[]>("/cases"),

  architecture: (root?: string) => request<Subgraph>(`/graph${q({ mode: "architecture", root })}`),
  subgraph: (root: string, depth = 3, columns = true) => request<Subgraph>(`/graph${q({ root, depth, columns })}`),
  node: (id: string, traceId?: string | null, scope?: string | null) =>
    request<NodeDetailsResponse>(`/node${q({ id, traceId, scope })}`),

  trace: (field: string, traceId?: string | null, scope?: string | null) =>
    request<FieldTrace>(`/trace/field${q({ field, traceId, scope })}`),
  impact: (id: string, depth?: number) => request<ImpactResult>(`/impact${q({ id, depth })}`),

  livePickOrder: (orderNo: string, materialNo?: string | null) =>
    request<RuntimeTrace>("/runtime/pick-order", { method: "POST", body: JSON.stringify({ orderNo, materialNo: materialNo || null }) }),
  runtimeTrace: (traceId: string) => request<RuntimeTrace>(`/runtime/traces/${encodeURIComponent(traceId)}`),
  invokeTool: (tool: string, args: Record<string, string>) =>
    request<ToolResult>("/runtime/tools", { method: "POST", body: JSON.stringify({ tool, arguments: args }) }),

  // `language` ("en" | "zh") only changes the sentences of the answer; ids, expressions, values and evidence ids stay verbatim.
  explain: (focusNodeId: string, traceId?: string | null, scope?: string | null, question?: string, language?: string) =>
    request<ExplainResponse>("/ai/explain", { method: "POST", body: JSON.stringify({ focusNodeId, traceId, scope, question, language }) }),
  investigate: (focusNodeId: string, traceId: string, scope?: string | null, question?: string, language?: string) =>
    request<ExplainResponse>("/ai/investigate", { method: "POST", body: JSON.stringify({ focusNodeId, traceId, scope, question, language }) }),
  impactSummary: (nodeId: string, language?: string) =>
    request<ImpactSummaryResponse>("/ai/impact-summary", { method: "POST", body: JSON.stringify({ nodeId, language }) }),
};
