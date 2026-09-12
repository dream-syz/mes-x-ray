import dagre from "@dagrejs/dagre";
import type { Edge, Node } from "@xyflow/react";
import type { GraphEdge, GraphNode, RuntimeValue } from "../../api/types";
import { relationLabel, type Lang } from "../../lib/i18n";
import { GRAPH_COLORS } from "../../lib/presentation";

/** Rank direction: `BT` puts the surface (page, JSON field) on top and SQL at the bottom; `LR` reads left to right. */
export type RankDirection = "BT" | "LR";

export interface XRayNodeData extends Record<string, unknown> {
  node: GraphNode;
  runtimeValues: RuntimeValue[];
  /** Part of the traced lineage or an impact key path. */
  highlighted: boolean;
  /** The field / origin the view is about. */
  focus: boolean;
  /** On the execution path (page -> API -> method -> SP). */
  onPath: boolean;
  /** Something else is highlighted, and this node is not part of it: fade it back. */
  dimmed: boolean;
  /** Stagger index for the entry animation. */
  index: number;
  /** Direction the graph was ranked in; decides where the handles sit. */
  direction: RankDirection;
}

export type XRayFlowNode = Node<XRayNodeData, "xray">;

export const NODE_WIDTH = 200;
export const NODE_HEIGHT = 56;

/**
 * Data flows from the Data layer to the Web layer. A `dependsOn` edge (expr -> column) points against the flow,
 * so it is reversed for layout; `flowsTo` and `structural` edges are used as-is.
 */
const flowPair = (edge: GraphEdge): [string, string] => (edge.direction === "dependsOn" ? [edge.toNodeId, edge.fromNodeId] : [edge.fromNodeId, edge.toNodeId]);

export interface LayoutInput {
  nodes: GraphNode[];
  edges: GraphEdge[];
  runtimeValues: Map<string, RuntimeValue[]>;
  highlighted: Set<string>;
  pathNodeIds: Set<string>;
  focusId: string | null;
  /** Canvas width / height. Used to pick the rank direction that fills the canvas best. */
  aspect: number;
  /** UI language for the edge labels (relation names); node names and conditions are never translated. */
  lang: Lang;
}

function rank(input: LayoutInput, ids: Set<string>, rankdir: RankDirection) {
  const graph = new dagre.graphlib.Graph();
  graph.setGraph({ rankdir, nodesep: 26, ranksep: rankdir === "BT" ? 58 : 72, marginx: 16, marginy: 16 });
  graph.setDefaultEdgeLabel(() => ({}));
  for (const node of input.nodes) graph.setNode(node.id, { width: NODE_WIDTH, height: NODE_HEIGHT });
  for (const edge of input.edges) {
    const [from, to] = flowPair(edge);
    if (ids.has(from) && ids.has(to) && from !== to) graph.setEdge(from, to);
  }
  dagre.layout(graph);
  const { width = 1, height = 1 } = graph.graph();
  return { graph, width, height };
}

export function layoutGraph(input: LayoutInput): { nodes: XRayFlowNode[]; edges: Edge[]; direction: RankDirection } {
  const ids = new Set(input.nodes.map((n) => n.id));

  // Lineage chains are ten ranks long but narrow, so they want to run top-to-bottom; the architecture map fans out
  // into a broad rank of tables and wants to run left-to-right. Rank both ways and keep the one that can be shown
  // largest on this canvas (the fit zoom is proportional to min(aspect / width, 1 / height)).
  const candidates = (["BT", "LR"] as RankDirection[]).map((dir) => ({ dir, ...rank(input, ids, dir) }));
  const fit = (c: (typeof candidates)[number]) => Math.min(input.aspect / c.width, 1 / c.height);
  const best = candidates.reduce((a, b) => (fit(b) > fit(a) * 1.05 ? b : a));
  const { graph, dir: direction } = best;

  // Focus mode: as soon as something is highlighted, everything else steps back.
  const focusMode = input.highlighted.size > 0 || input.pathNodeIds.size > 0;
  const isLit = (id: string) => input.highlighted.has(id) || input.pathNodeIds.has(id) || input.focusId === id;

  // Stagger the entry animation in data-flow order (columns first, surface last): bottom-up or left-to-right.
  const flowOrder = (id: string) => (direction === "BT" ? -(graph.node(id)?.y ?? 0) : graph.node(id)?.x ?? 0);
  const order = [...input.nodes].sort((a, b) => flowOrder(a.id) - flowOrder(b.id)).map((n) => n.id);
  const indexOf = new Map(order.map((id, i) => [id, i]));

  const nodes: XRayFlowNode[] = input.nodes.map((node) => {
    const position = graph.node(node.id);
    return {
      id: node.id,
      type: "xray",
      position: { x: (position?.x ?? 0) - NODE_WIDTH / 2, y: (position?.y ?? 0) - NODE_HEIGHT / 2 },
      data: {
        node,
        runtimeValues: input.runtimeValues.get(node.id) ?? [],
        highlighted: input.highlighted.has(node.id),
        focus: input.focusId === node.id,
        onPath: input.pathNodeIds.has(node.id),
        dimmed: focusMode && !isLit(node.id),
        index: indexOf.get(node.id) ?? 0,
        direction,
      },
      draggable: true,
    };
  });

  const edges: Edge[] = input.edges
    .filter((edge) => ids.has(edge.fromNodeId) && ids.has(edge.toNodeId))
    .map((edge) => {
      const [source, target] = flowPair(edge);
      const condition = edge.metadata?.condition;
      const relation = relationLabel(input.lang, edge.relationType);
      const label = condition ? `${relation}  ${condition}` : relation;
      const structural = edge.direction === "structural";
      // An edge is lit when it is part of the lineage itself, or when it joins two lit nodes of the execution path.
      const lit = input.highlighted.has(edge.id) || (isLit(edge.fromNodeId) && isLit(edge.toNodeId) && !structural);
      const dimmed = focusMode && !lit;
      const classes = ["edge", `edge-${edge.direction}`, lit ? "edge-path" : "", dimmed ? "edge-dimmed" : ""].filter(Boolean).join(" ");
      return {
        id: edge.id,
        source,
        target,
        label,
        type: "smoothstep",
        animated: lit && !structural,
        className: classes,
        style: { strokeDasharray: structural ? "4 4" : undefined, strokeWidth: lit ? 2 : 1.2 },
        labelStyle: { fontSize: 10, fill: condition ? GRAPH_COLORS.warn : "var(--muted)" },
        labelBgStyle: { fill: "var(--surface)", fillOpacity: 0.92 },
        labelBgPadding: [5, 2] as [number, number],
        labelBgBorderRadius: 3,
        markerEnd: { type: "arrowclosed" as const, width: 14, height: 14, color: lit ? GRAPH_COLORS.accent : structural ? GRAPH_COLORS.edgeStructural : GRAPH_COLORS.edge },
        data: { edge },
      };
    });

  return { nodes, edges, direction };
}
