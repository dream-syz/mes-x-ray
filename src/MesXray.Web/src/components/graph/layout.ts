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
  /** The node owns an internals subtree in the trace (a SQL function) that can be folded away. */
  foldable: boolean;
  /** Number of internal nodes currently folded into this node; 0 when unfolded. */
  folded: number;
}

export type XRayFlowNode = Node<XRayNodeData, "xray">;

export const NODE_WIDTH = 200;
export const NODE_HEIGHT = 56;
/** Extra rank height for a function that carries the fold chip, so the chip does not sit on the node below. */
const FOLD_CHIP_HEIGHT = 22;

const nodeSize = (id: string, foldable?: Set<string>) => ({
  width: NODE_WIDTH,
  height: NODE_HEIGHT + (foldable?.has(id) ? FOLD_CHIP_HEIGHT : 0),
});

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
  /** Nodes folded away (the internals of a folded SQL function). Optional: the architecture and impact views hide nothing. */
  hidden?: Set<string>;
  /** Hop node id -> number of internal nodes folded into it. */
  folded?: Map<string, number>;
  /** Hop node ids that own a foldable internals subtree. */
  foldable?: Set<string>;
}

export interface LayoutResult {
  nodes: XRayFlowNode[];
  edges: Edge[];
  direction: RankDirection;
  /** Ids of the lit nodes (traced lineage, execution path, focus) that are visible: what the camera frames first. */
  litIds: string[];
}

function rank(input: LayoutInput, nodes: GraphNode[], ids: Set<string>, rankdir: RankDirection) {
  const graph = new dagre.graphlib.Graph();
  graph.setGraph({ rankdir, nodesep: 26, ranksep: rankdir === "BT" ? 58 : 72, marginx: 16, marginy: 16 });
  graph.setDefaultEdgeLabel(() => ({}));
  for (const node of nodes) graph.setNode(node.id, nodeSize(node.id, input.foldable));
  for (const edge of input.edges) {
    const [from, to] = flowPair(edge);
    if (ids.has(from) && ids.has(to) && from !== to) graph.setEdge(from, to);
  }
  dagre.layout(graph);
  const { width = 1, height = 1 } = graph.graph();
  return { graph, width, height };
}

/**
 * Removes the hidden nodes, and with them the structural containers (tables, CTEs, temp tables) that only held hidden
 * members: a table whose every traced column is folded away has nothing left to say.
 */
function visibleNodes(input: LayoutInput, isLit: (id: string) => boolean): GraphNode[] {
  const hidden = input.hidden;
  if (!hidden || hidden.size === 0) return input.nodes;
  const remaining = new Set(input.nodes.map((n) => n.id).filter((id) => !hidden.has(id)));
  const degreeAll = new Map<string, number>();
  const degreeLeft = new Map<string, number>();
  const bump = (map: Map<string, number>, id: string) => map.set(id, (map.get(id) ?? 0) + 1);
  for (const edge of input.edges) {
    if (edge.fromNodeId === edge.toNodeId) continue;
    bump(degreeAll, edge.fromNodeId);
    bump(degreeAll, edge.toNodeId);
    if (remaining.has(edge.fromNodeId) && remaining.has(edge.toNodeId)) {
      bump(degreeLeft, edge.fromNodeId);
      bump(degreeLeft, edge.toNodeId);
    }
  }
  return input.nodes.filter((n) => remaining.has(n.id) && (isLit(n.id) || (degreeAll.get(n.id) ?? 0) === 0 || (degreeLeft.get(n.id) ?? 0) > 0));
}

export function layoutGraph(input: LayoutInput): LayoutResult {
  // Focus mode: as soon as something is highlighted, everything else steps back.
  const focusMode = input.highlighted.size > 0 || input.pathNodeIds.size > 0;
  const isLit = (id: string) => input.highlighted.has(id) || input.pathNodeIds.has(id) || input.focusId === id;

  const shown = visibleNodes(input, isLit);
  const ids = new Set(shown.map((n) => n.id));

  // Lineage chains are ten ranks long but narrow, so they want to run top-to-bottom; the architecture map fans out
  // into a broad rank of tables and wants to run left-to-right. Rank both ways and keep the one that can be shown
  // largest on this canvas (the fit zoom is proportional to min(aspect / width, 1 / height)).
  const candidates = (["BT", "LR"] as RankDirection[]).map((dir) => ({ dir, ...rank(input, shown, ids, dir) }));
  const fit = (c: (typeof candidates)[number]) => Math.min(input.aspect / c.width, 1 / c.height);
  const best = candidates.reduce((a, b) => (fit(b) > fit(a) * 1.05 ? b : a));
  const { graph, dir: direction } = best;

  // Stagger the entry animation in data-flow order (columns first, surface last): bottom-up or left-to-right.
  const flowOrder = (id: string) => (direction === "BT" ? -(graph.node(id)?.y ?? 0) : graph.node(id)?.x ?? 0);
  const order = [...shown].sort((a, b) => flowOrder(a.id) - flowOrder(b.id)).map((n) => n.id);
  const indexOf = new Map(order.map((id, i) => [id, i]));

  const nodes: XRayFlowNode[] = shown.map((node) => {
    const position = graph.node(node.id);
    const size = nodeSize(node.id, input.foldable);
    return {
      id: node.id,
      type: "xray",
      position: { x: (position?.x ?? 0) - size.width / 2, y: (position?.y ?? 0) - size.height / 2 },
      data: {
        node,
        runtimeValues: input.runtimeValues.get(node.id) ?? [],
        highlighted: input.highlighted.has(node.id),
        focus: input.focusId === node.id,
        onPath: input.pathNodeIds.has(node.id),
        dimmed: focusMode && !isLit(node.id),
        index: indexOf.get(node.id) ?? 0,
        direction,
        foldable: input.foldable?.has(node.id) ?? false,
        folded: input.folded?.get(node.id) ?? 0,
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

  return { nodes, edges, direction, litIds: shown.filter((n) => isLit(n.id)).map((n) => n.id) };
}
