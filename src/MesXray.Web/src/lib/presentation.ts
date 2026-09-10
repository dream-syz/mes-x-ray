import type { GraphNode, Layer, NodeStatus, NodeType } from "../api/types";

/** Layer colour coding (design §8): Web / API / Service / Data / Config. */
export const LAYER_COLORS: Record<Layer, { fill: string; border: string; text: string; label: string }> = {
  web: { fill: "#eef4ff", border: "#3b6fd6", text: "#1d3f8a", label: "Web" },
  api: { fill: "#ecfbf3", border: "#1f9d61", text: "#0f5c37", label: "API" },
  service: { fill: "#fff6e6", border: "#d98a12", text: "#7a4a05", label: "Service" },
  data: { fill: "#f4eeff", border: "#7a4fd0", text: "#3f2378", label: "Data" },
  config: { fill: "#fdeeee", border: "#d0473f", text: "#7a1f1a", label: "Config" },
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
