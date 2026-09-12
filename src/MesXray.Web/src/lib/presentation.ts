import type { GraphNode } from "../api/types";

/**
 * The graph is monochrome on purpose: the layout already orders the layers (design §8: Web / API / Service / Data /
 * Config) in data-flow direction, and the single accent colour is reserved for what the X-ray reveals (traced path,
 * selection, live evidence). Layers, types, statuses and relations are therefore labelled, not coloured; the labels
 * live in `i18n.ts` because they follow the UI language.
 */

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
