import type { FieldTrace, GraphEdge, TraceHop } from "../../api/types";
import { flowPair } from "./layout";

/**
 * Replay of a trace as motion: the request goes down the execution path (page -> API -> service -> procedure ->
 * function), then the value comes back up the lineage one layer at a time until it lands in the traced field. Each
 * step lights a set of nodes and sends a pulse along the edges those nodes feed; the next step lights what the pulses
 * reached. The plan is computed over the nodes that are visible, so a folded function is one hop, an unfolded one
 * plays out through its RETURN expression, CTEs and base columns.
 */
export interface ReplayEdge {
  id: string;
  /**
   * The pulse travels against the SVG path. Edges are drawn in data-flow direction; a call travels the other way
   * (from the caller down to the callee), the returning value travels with it.
   */
  reverse: boolean;
}

export interface ReplayStep {
  nodeIds: string[];
  edges: ReplayEdge[];
}

const pairKey = (a: string, b: string): string => (a < b ? `${a}\u0000${b}` : `${b}\u0000${a}`);

/** A pulse from `from` to `to` over `edge`: with the drawn path when the path starts at `from`, against it otherwise. */
const travel = (edge: GraphEdge, from: string): ReplayEdge => ({ id: edge.id, reverse: flowPair(edge)[0] !== from });

export function planReplay(trace: FieldTrace, hidden: ReadonlySet<string>): ReplayStep[] {
  const edgesById = new Map(trace.graph.edges.map((e) => [e.id, e]));
  const edgesByPair = new Map<string, GraphEdge[]>();
  for (const edge of trace.graph.edges) {
    if (edge.direction === "structural" || edge.fromNodeId === edge.toNodeId) continue;
    const key = pairKey(edge.fromNodeId, edge.toNodeId);
    edgesByPair.set(key, [...(edgesByPair.get(key) ?? []), edge]);
  }

  // Leg 1: the call goes down the execution path, one node per step, pulsing the edge it arrives through.
  const steps: ReplayStep[] = [];
  const path = trace.executionPath.map((n) => n.id).filter((id) => !hidden.has(id));
  path.forEach((id, i) => {
    const previous = i > 0 ? path[i - 1] : null;
    const edge = previous ? edgesByPair.get(pairKey(previous, id))?.[0] : undefined;
    steps.push({ nodeIds: [id], edges: edge && previous ? [travel(edge, previous)] : [] });
  });

  // Leg 2: the value comes back up the lineage. Breadth-first from the field gives every visible hop its depth; the
  // wave then runs from the deepest layer to the field, each layer pulsing the edges that carry its values upward.
  const depthOf = new Map<string, number>();
  const upward = new Map<string, ReplayEdge[]>();
  const queue: { hop: TraceHop; depth: number }[] = [{ hop: trace.root, depth: 0 }];
  while (queue.length > 0) {
    const { hop, depth } = queue.shift()!;
    if (hidden.has(hop.nodeId)) continue;
    if (depthOf.has(hop.nodeId)) continue;
    depthOf.set(hop.nodeId, depth);
    for (const source of hop.sources) {
      if (hidden.has(source.nodeId)) continue;
      const edge = source.viaEdgeId ? edgesById.get(source.viaEdgeId) : undefined;
      if (edge) upward.set(source.nodeId, [...(upward.get(source.nodeId) ?? []), travel(edge, source.nodeId)]);
      queue.push({ hop: source, depth: depth + 1 });
    }
  }
  const deepest = Math.max(0, ...depthOf.values());
  if (path.length > 0 && depthOf.size > 0) steps.push({ nodeIds: [], edges: [] }); // a beat: the call has arrived, the value starts back
  for (let depth = deepest; depth >= 0; depth -= 1) {
    const nodeIds = [...depthOf].filter(([, d]) => d === depth).map(([id]) => id);
    if (nodeIds.length === 0) continue;
    steps.push({ nodeIds, edges: nodeIds.flatMap((id) => upward.get(id) ?? []) });
  }
  return steps;
}
