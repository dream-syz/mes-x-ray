// Mirrors the JSON contracts of MesXray.Api (camelCase, enums as camelCase strings).

export type NodeStatus = "known" | "pending" | "unknown";
export type Layer = "web" | "api" | "service" | "data" | "config";
export type NodeType =
  | "page"
  | "jsonField"
  | "api"
  | "method"
  | "model"
  | "field"
  | "storedProcedure"
  | "resultColumn"
  | "function"
  | "table"
  | "column"
  | "intermediate"
  | "intermediateColumn"
  | "expression"
  | "systemParameter"
  | "branch";

export interface SourceLocation {
  path: string;
  startLine?: number | null;
  endLine?: number | null;
}

export interface GraphNode {
  id: string;
  type: NodeType;
  name: string;
  qualifiedName?: string | null;
  layer: Layer;
  status: NodeStatus;
  source?: SourceLocation | null;
  metadata: Record<string, string>;
  description?: string | null;
  scanVersion?: string | null;
}

export interface GraphEdge {
  id: string;
  fromNodeId: string;
  toNodeId: string;
  relationType: string;
  confidence: number;
  evidenceType: string;
  evidenceRef?: string | null;
  metadata: Record<string, string>;
  direction: "dependsOn" | "flowsTo" | "structural";
}

export interface Subgraph {
  rootNodeId: string;
  nodes: GraphNode[];
  edges: GraphEdge[];
  truncated: boolean;
}

export interface FieldLineage {
  id: string;
  outputFieldId: string;
  sourceFieldId?: string | null;
  transformType: string;
  expression?: string | null;
  condition?: string | null;
  evidenceEdgeIds: string[];
  confidence: number;
}

export interface NodeDetails {
  node: GraphNode;
  incoming: GraphEdge[];
  outgoing: GraphEdge[];
  neighbors: GraphNode[];
  lineageAsOutput: FieldLineage[];
  lineageAsSource: FieldLineage[];
  container?: GraphNode | null;
  evidenceIds: string[];
}

export interface RuntimeValue {
  evidenceId: string;
  scope?: string | null;
  label: string;
  value: unknown;
  evidenceType: string;
}

export interface NodeDetailsResponse {
  details: NodeDetails;
  runtimeValues: RuntimeValue[];
}

export interface TraceHop {
  nodeId: string;
  node: GraphNode;
  viaRelation?: string | null;
  viaEdgeId?: string | null;
  transformType?: string | null;
  expression?: string | null;
  condition?: string | null;
  confidence: number;
  status: NodeStatus;
  containerNodeId?: string | null;
  runtimeValues: RuntimeValue[];
  isRepeat: boolean;
  sources: TraceHop[];
}

export interface Unknown {
  nodeId: string;
  name: string;
  type: NodeType;
  reason: string;
}

export interface FieldTrace {
  field: GraphNode;
  root: TraceHop;
  executionPath: GraphNode[];
  unknowns: Unknown[];
  evidenceIds: string[];
  graph: Subgraph;
  traceId?: string | null;
  scope?: string | null;
}

export interface ImpactedNode {
  node: GraphNode;
  depth: number;
  viaEdgeId?: string | null;
  viaRelation?: string | null;
}

export interface ImpactResult {
  origin: GraphNode;
  affected: ImpactedNode[];
  summary: Record<string, number>;
  keyPaths: string[][];
  evidenceIds: string[];
  graph: Subgraph;
  truncated: boolean;
}

export interface RuntimeEvidence {
  id: string;
  traceId: string;
  entityType: string;
  entityKey: string;
  nodeId: string;
  evidenceType: string;
  value: unknown;
  label: string;
  scope?: string | null;
  observedAt: string;
  environment: string;
}

export interface RuntimeTrace {
  traceId: string;
  entityType: string;
  entityKey: string;
  environment: string;
  observedAt: string;
  response?: unknown;
  evidence: RuntimeEvidence[];
  unknowns: string[];
  fixtureId?: string | null;
}

export interface TraceSummary {
  traceId: string;
  entityType: string;
  entityKey: string;
  environment: string;
  observedAt: string;
  evidenceCount: number;
  fixtureId?: string | null;
  scopes: string[];
}

export interface CaseDefinition {
  id: string;
  title: string;
  description: string;
  rootNodeId: string;
  apiNodeId: string;
  responseModelNodeId: string;
  keyFields: string[];
  systemParameters: string[];
  defaultFixtureId?: string | null;
  defaultTraceId?: string | null;
  defaultScope?: string | null;
  knownGaps: { nodeId: string; reason: string; priority: string }[];
}

export interface GapStatus {
  nodeId: string;
  reason: string;
  priority: string;
  status: NodeStatus;
  name?: string | null;
}

export interface BuildStep {
  name: string;
  nodes: number;
  edges: number;
  lineages: number;
  elapsedMs: number;
  diagnostics: string[];
}

export interface GraphBuildReport {
  mode: string;
  fixtureRoot: string;
  builtAt: string;
  steps: BuildStep[];
  nodes: number;
  edges: number;
  lineages: number;
  unknownNodes: number;
  pendingNodes: number;
  linker?: { mappedColumns: number; unmappedFields: string[]; unmappedColumns: string[] } | null;
}

export interface CaseOverview {
  case: CaseDefinition;
  keyFields: GraphNode[];
  systemParameters: GraphNode[];
  knownGaps: GapStatus[];
  traces: TraceSummary[];
  allowedTools: string[];
  forbiddenTools: string[];
  build: GraphBuildReport;
}

export type HypothesisStatus = "unverified" | "supported" | "refuted";
export type ExplainVerdict = "known" | "needMoreEvidence" | "unknown";

export interface KnownFact {
  text: string;
  evidenceIds: string[];
}

export interface Hypothesis {
  text: string;
  status: HypothesisStatus;
  suggestedCheck?: string | null;
  evidenceIds?: string[] | null;
}

export interface AiAudit {
  provider: string;
  model: string;
  promptVersion: string;
  timestamp: string;
  evidenceIds: string[];
  note?: string | null;
}

export interface Explanation {
  summary: string;
  steps: string[];
  knownFacts: KnownFact[];
  hypotheses: Hypothesis[];
  unknowns: string[];
  nextSteps: string[];
  confidence: number;
  verdict: ExplainVerdict;
  audit: AiAudit;
  evidenceCount: number;
}

export interface EvidenceItem {
  id: string;
  kind: "node" | "edge" | "lineage" | "runtime";
  text: string;
  nodeId?: string | null;
  scope?: string | null;
  value?: unknown;
}

export interface ExplainResponse {
  explanation: Explanation;
  evidence: EvidenceItem[];
  focusNodeId: string;
  traceId?: string | null;
  scope?: string | null;
}

export interface ImpactSummaryResponse {
  explanation: Explanation;
  summary: Record<string, number>;
  keyPaths: string[][];
}

export interface ToolResult {
  auditId: string;
  tool: string;
  allowed: boolean;
  denyReason?: string | null;
  result?: unknown;
  timestamp: string;
}

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  [key: string]: unknown;
}
