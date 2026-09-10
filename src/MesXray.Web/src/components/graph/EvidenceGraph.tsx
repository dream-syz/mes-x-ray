import { useEffect, useMemo } from "react";
import { Background, Controls, MiniMap, ReactFlow, useEdgesState, useNodesState, useReactFlow, ReactFlowProvider, type NodeMouseHandler } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import type { RuntimeValue, Subgraph, TraceHop } from "../../api/types";
import type { XRayController } from "../../state/useXRay";
import { LAYER_COLORS } from "../../lib/presentation";
import { layoutGraph, type XRayFlowNode } from "./layout";
import { XRayNode } from "./XRayNode";

const nodeTypes = { xray: XRayNode };

function collectHopValues(hop: TraceHop, into: Map<string, RuntimeValue[]>, highlighted: Set<string>) {
  highlighted.add(hop.nodeId);
  if (hop.viaEdgeId) highlighted.add(hop.viaEdgeId);
  if (hop.runtimeValues.length > 0 && !into.has(hop.nodeId)) into.set(hop.nodeId, hop.runtimeValues);
  for (const source of hop.sources) collectHopValues(source, into, highlighted);
}

function pickSubgraph(controller: XRayController): { subgraph: Subgraph | null; title: string } {
  const { state } = controller;
  if (state.mode === "architecture") return { subgraph: state.architecture, title: "Architecture · page → API → service → SQL" };
  if (state.tab === "impact" && state.impact) return { subgraph: state.impact.graph, title: `Impact of ${state.impact.origin.name}` };
  if (state.trace) return { subgraph: state.trace.graph, title: `Trace Source · ${state.trace.field.id}${state.trace.scope ? ` @ ${state.trace.scope}` : ""}` };
  return { subgraph: state.architecture, title: "Architecture (run a trace to see field lineage)" };
}

function Canvas({ controller }: { controller: XRayController }) {
  const { state, selectNode } = controller;
  const { subgraph, title } = pickSubgraph(controller);
  const { fitView } = useReactFlow();

  const layout = useMemo(() => {
    if (!subgraph) return { nodes: [] as XRayFlowNode[], edges: [] };
    const runtimeValues = new Map<string, RuntimeValue[]>();
    const highlighted = new Set<string>();
    const pathNodeIds = new Set<string>();
    let focusId: string | null = null;

    if (state.mode !== "architecture" && state.trace && subgraph === state.trace.graph) {
      collectHopValues(state.trace.root, runtimeValues, highlighted);
      for (const n of state.trace.executionPath) pathNodeIds.add(n.id);
      focusId = state.trace.field.id;
    } else if (state.tab === "impact" && state.impact && subgraph === state.impact.graph) {
      focusId = state.impact.origin.id;
      for (const path of state.impact.keyPaths) for (const id of path) highlighted.add(id);
    }

    // Live values for nodes not on the traced path (e.g. parameters) come straight from the runtime trace.
    if (state.live) {
      for (const evidence of state.live.evidence) {
        if (!runtimeValues.has(evidence.nodeId) && (evidence.scope === null || evidence.scope === undefined || evidence.scope === state.scope)) {
          const existing = runtimeValues.get(evidence.nodeId) ?? [];
          runtimeValues.set(evidence.nodeId, [...existing, { evidenceId: evidence.id, scope: evidence.scope ?? null, label: evidence.label, value: evidence.value, evidenceType: evidence.evidenceType }]);
        }
      }
    }

    return layoutGraph({ nodes: subgraph.nodes, edges: subgraph.edges, runtimeValues, highlighted, pathNodeIds, focusId });
  }, [subgraph, state.mode, state.trace, state.impact, state.tab, state.live, state.scope]);

  const [nodes, setNodes, onNodesChange] = useNodesState<XRayFlowNode>(layout.nodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(layout.edges);

  useEffect(() => {
    setNodes(layout.nodes);
    setEdges(layout.edges);
    const handle = window.setTimeout(() => void fitView({ padding: 0.15, duration: 300 }), 30);
    return () => window.clearTimeout(handle);
  }, [layout, setNodes, setEdges, fitView]);

  const onNodeClick: NodeMouseHandler<XRayFlowNode> = (_event, node) => void selectNode(node.id);

  return (
    <>
      <div className="pane-header">
        <h2>Evidence Graph</h2>
        <span className="pane-sub">{title}</span>
        {subgraph?.truncated && <span className="warn-chip">truncated</span>}
      </div>
      <div className="graph-canvas">
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          onNodesChange={onNodesChange}
          onEdgesChange={onEdgesChange}
          onNodeClick={onNodeClick}
          fitView
          minZoom={0.15}
          nodesConnectable={false}
          proOptions={{ hideAttribution: true }}
        >
          <Background gap={18} size={1} />
          <MiniMap pannable zoomable nodeColor={(n) => LAYER_COLORS[(n as XRayFlowNode).data.node.layer]?.border ?? "#999"} />
          <Controls showInteractive={false} />
        </ReactFlow>
      </div>
      <div className="pane-footer legend">
        {Object.entries(LAYER_COLORS).map(([layer, palette]) => (
          <span key={layer} className="legend-item">
            <i style={{ background: palette.fill, borderColor: palette.border }} /> {palette.label}
          </span>
        ))}
        <span className="legend-item">
          <i className="legend-gap" /> Unknown / Pending
        </span>
        <span className="legend-item">
          <i className="legend-flow" /> data flow (dependsOn edges drawn in flow direction)
        </span>
      </div>
    </>
  );
}

export function EvidenceGraph({ controller }: { controller: XRayController }) {
  return (
    <section className="pane pane-center">
      <ReactFlowProvider>
        <Canvas controller={controller} />
      </ReactFlowProvider>
    </section>
  );
}
