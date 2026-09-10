import dagre from "@dagrejs/dagre";
import type { Edge, Node } from "@xyflow/react";
import type { GraphEdge, GraphNode, RuntimeValue } from "../../api/types";

export interface XRayNodeData extends Record<string, unknown> {
  node: GraphNode;
  runtimeValues: RuntimeValue[];
  highlighted: boolean;
  focus: boolean;
  onPath: boolean;
}

export type XRayFlowNode = Node<XRayNodeData, "xray">;

const NODE_WIDTH = 220;
const NODE_HEIGHT = 64;

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
}

export function layoutGraph(input: LayoutInput): { nodes: XRayFlowNode[]; edges: Edge[] } {
  const graph = new dagre.graphlib.Graph();
  graph.setGraph({ rankdir: "LR", nodesep: 28, ranksep: 90, marginx: 20, marginy: 20 });
  graph.setDefaultEdgeLabel(() => ({}));

  const ids = new Set(input.nodes.map((n) => n.id));
  for (const node of input.nodes) {
    graph.setNode(node.id, { width: NODE_WIDTH, height: NODE_HEIGHT });
  }
  for (const edge of input.edges) {
    const [from, to] = flowPair(edge);
    if (ids.has(from) && ids.has(to) && from !== to) {
      graph.setEdge(from, to);
    }
  }

  dagre.layout(graph);

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
      },
      draggable: true,
    };
  });

  const edges: Edge[] = input.edges
    .filter((edge) => ids.has(edge.fromNodeId) && ids.has(edge.toNodeId))
    .map((edge) => {
      const [source, target] = flowPair(edge);
      const condition = edge.metadata?.condition;
      const relation = edge.relationType.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();
      const label = condition ? `${relation} · ${condition}` : relation;
      const structural = edge.direction === "structural";
      const highlighted = input.highlighted.has(edge.id);
      return {
        id: edge.id,
        source,
        target,
        label,
        type: "smoothstep",
        animated: highlighted && !structural,
        className: `edge-${edge.direction}${highlighted ? " edge-highlight" : ""}`,
        style: { strokeDasharray: structural ? "4 4" : undefined, strokeWidth: highlighted ? 2 : 1.2, opacity: edge.confidence < 0.8 ? 0.6 : 1 },
        labelStyle: { fontSize: 10, fill: condition ? "#7a1f1a" : "#555" },
        labelBgStyle: { fill: "#fff", fillOpacity: 0.85 },
        labelBgPadding: [4, 2] as [number, number],
        markerEnd: { type: "arrowclosed" as const, width: 14, height: 14 },
        data: { edge },
      };
    });

  return { nodes, edges };
}
