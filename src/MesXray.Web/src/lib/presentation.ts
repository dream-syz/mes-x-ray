import type { GraphNode, Layer, NodeStatus, NodeType } from "../api/types";

/**
 * Layers (design §8): Web / API / Service / Data / Config. The graph is monochrome on purpose: the layout already
 * orders layers left to right, and the single accent colour is reserved for what the X-ray reveals (traced path,
 * selection, live evidence). Layers are therefore labelled, not coloured.
 */
export const LAYERS: Record<Layer, { label: string }> = {
  web: { label: "Web" },
  api: { label: "API" },
  service: { label: "Service" },
  data: { label: "Data" },
  config: { label: "Config" },
};

/** Colours that must be passed as values (SVG markers, minimap); everything else uses the CSS variables. */
export const GRAPH_COLORS = {
  accent: "#3fc1e8",
  warn: "#f2b545",
  edge: "rgba(255,255,255,0.24)",
  edgeStructural: "rgba(255,255,255,0.16)",
  nodeIdle: "#2a3140",
  nodeDim: "#1a1f29",
  mask: "rgba(10,13,18,0.72)",
};

export const TYPE_LABELS: Record<NodeType, string> = {
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
};

export const STATUS_LABELS: Record<NodeStatus, string> = {
  known: "Known",
  pending: "Pending",
  unknown: "Unknown - Need More Evidence",
};

export const RELATION_LABELS: Record<string, string> = {
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
};

export const relationLabel = (relation: string | null | undefined): string =>
  relation ? RELATION_LABELS[relation] ?? relation.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase() : "";

export const shortName = (node: GraphNode): string => {
  switch (node.type) {
    case "expression":
      return node.name.length > 40 ? `${node.name.slice(0, 40)}...` : node.name;
    case "api":
      return node.name;
    default:
      return node.name;
  }
};

export const formatValue = (value: unknown): string => {
  if (value === null || value === undefined) return "null";
  if (typeof value === "string") return `"${value}"`;
  if (typeof value === "object") return JSON.stringify(value);
  return String(value);
};

export const percent = (value: number): string => `${Math.round(value * 100)}%`;
