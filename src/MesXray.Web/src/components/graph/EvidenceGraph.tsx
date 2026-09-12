import { useEffect, useLayoutEffect, useMemo, useRef, useState, type RefObject } from "react";
import {
  Background,
  BackgroundVariant,
  Controls,
  MiniMap,
  ReactFlow,
  ReactFlowProvider,
  useEdgesState,
  useNodesInitialized,
  useNodesState,
  useOnViewportChange,
  useReactFlow,
  type NodeMouseHandler,
  type Viewport,
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import type { RuntimeValue, Subgraph, TraceHop } from "../../api/types";
import type { XRayController } from "../../state/useXRay";
import { useI18n, type I18n } from "../../lib/I18nContext";
import { GRAPH_COLORS } from "../../lib/presentation";
import { layoutGraph, type XRayFlowNode } from "./layout";
import { XRayNode } from "./XRayNode";

const nodeTypes = { xray: XRayNode };

function collectHopValues(hop: TraceHop, into: Map<string, RuntimeValue[]>, highlighted: Set<string>) {
  highlighted.add(hop.nodeId);
  if (hop.viaEdgeId) highlighted.add(hop.viaEdgeId);
  if (hop.runtimeValues.length > 0 && !into.has(hop.nodeId)) into.set(hop.nodeId, hop.runtimeValues);
  for (const source of hop.sources) collectHopValues(source, into, highlighted);
}

function pickSubgraph(controller: XRayController, t: I18n["t"]): { subgraph: Subgraph | null; title: string; scanKey: string } {
  const { state } = controller;
  if (state.mode === "architecture") return { subgraph: state.architecture, title: t("graph.architecture"), scanKey: "architecture" };
  if (state.tab === "impact" && state.impact) return { subgraph: state.impact.graph, title: t("graph.impactOf", { name: state.impact.origin.name }), scanKey: `impact:${state.impact.origin.id}` };
  if (state.trace) {
    return {
      subgraph: state.trace.graph,
      title: `${t("graph.traceOf", { field: state.trace.field.id })}${state.trace.scope ? ` @ ${state.trace.scope}` : ""}`,
      scanKey: `trace:${state.trace.field.id}:${state.trace.scope ?? ""}:${state.trace.traceId ?? ""}`,
    };
  }
  return { subgraph: state.architecture, title: t("graph.architectureHint"), scanKey: "architecture" };
}

/** A single left-to-right sweep when a new trace or impact result arrives: the X-ray has just been taken. */
function ScanSweep({ scanKey }: { scanKey: string }) {
  const [active, setActive] = useState<string | null>(null);
  useEffect(() => {
    if (scanKey.startsWith("architecture")) return;
    setActive(scanKey);
  }, [scanKey]);
  if (!active) return null;
  return <div key={active} className="scan-sweep" aria-hidden onAnimationEnd={() => setActive(null)} />;
}

/**
 * Below this zoom the nodes drop to a name-only rendering and edge labels are hidden (see `.zoom-far` in styles.css).
 * At 0.7 the 10px detail text would already be under 7px on screen, so the large name is the more readable choice.
 */
const FAR_ZOOM = 0.7;

/** Applies the level-of-detail class straight to the DOM so that panning and zooming never re-render React. */
function LevelOfDetail({ target }: { target: RefObject<HTMLDivElement | null> }) {
  useOnViewportChange({
    onChange: (viewport: Viewport) => target.current?.classList.toggle("zoom-far", viewport.zoom < FAR_ZOOM),
  });
  return null;
}

function Canvas({ controller }: { controller: XRayController }) {
  const { state, selectNode } = controller;
  const { t, lang } = useI18n();
  const { subgraph, title, scanKey } = pickSubgraph(controller, t);
  const { fitView } = useReactFlow();
  const canvasRef = useRef<HTMLDivElement | null>(null);

  // Canvas aspect ratio, measured before the first paint and then on resize (rounded so that resizing does not
  // trigger a relayout on every pixel).
  const [aspect, setAspect] = useState(1.6);
  const measure = (width: number, height: number) => {
    if (width > 0 && height > 0) setAspect(Math.round((width / height) * 10) / 10);
  };
  useLayoutEffect(() => {
    const element = canvasRef.current;
    if (!element) return;
    const rect = element.getBoundingClientRect();
    measure(rect.width, rect.height);
    const observer = new ResizeObserver((entries) => {
      const box = entries[0]?.contentRect;
      if (box) measure(box.width, box.height);
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

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
    } else if (subgraph === state.architecture && state.overview) {
      // The architecture map gets one anchor: the page the case starts from.
      focusId = state.overview.case.rootNodeId;
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

    return layoutGraph({ nodes: subgraph.nodes, edges: subgraph.edges, runtimeValues, highlighted, pathNodeIds, focusId, aspect, lang });
  }, [subgraph, state.mode, state.trace, state.impact, state.tab, state.live, state.scope, state.architecture, state.overview, aspect, lang]);

  const [nodes, setNodes, onNodesChange] = useNodesState<XRayFlowNode>(layout.nodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(layout.edges);
  const nodesInitialized = useNodesInitialized();
  const [layoutVersion, setLayoutVersion] = useState(0);

  useEffect(() => {
    setNodes(layout.nodes);
    setEdges(layout.edges);
    setLayoutVersion((v) => v + 1);
  }, [layout, setNodes, setEdges]);

  // Fit once the new nodes have been measured; fitting earlier would frame only the nodes React Flow already knows.
  // React Flow queues the fit and flushes it on the next node update (or in a requestAnimationFrame, which throttled
  // or headless tabs may never run); nudging the nodes state flushes it right away. Should the glide still not settle
  // (interrupted gesture), the camera snaps into place instead of stopping halfway.
  useEffect(() => {
    if (!nodesInitialized || layout.nodes.length === 0) return;
    let fallback: number | undefined;
    const handle = window.setTimeout(() => {
      let settled = false;
      void fitView({ padding: 0.08, duration: 400 }).then(() => {
        settled = true;
      });
      setNodes((current) => [...current]);
      fallback = window.setTimeout(() => {
        if (!settled) {
          void fitView({ padding: 0.08, duration: 0 });
          setNodes((current) => [...current]);
        }
      }, 700);
    }, 20);
    return () => {
      window.clearTimeout(handle);
      if (fallback !== undefined) window.clearTimeout(fallback);
    };
  }, [nodesInitialized, layoutVersion, layout.nodes.length, fitView, setNodes]);

  const onNodeClick: NodeMouseHandler<XRayFlowNode> = (_event, node) => void selectNode(node.id);

  const minimapColor = (n: XRayFlowNode) => {
    if (n.data.focus || n.data.highlighted || n.data.onPath) return GRAPH_COLORS.accent;
    if (n.data.node.status !== "known") return GRAPH_COLORS.warn;
    return n.data.dimmed ? GRAPH_COLORS.nodeDim : GRAPH_COLORS.nodeIdle;
  };

  return (
    <>
      <div className="pane-header">
        <h2>{t("graph.title")}</h2>
        <span className="pane-sub">{title}</span>
        {subgraph?.truncated && <span className="chip-warn">{t("graph.truncated")}</span>}
        <div className="legend">
          <span className="legend-item">
            <i /> {t("graph.legendPath")}
          </span>
          <span className="legend-item">
            <i className="legend-value" /> {t("graph.legendValue")}
          </span>
          <span className="legend-item">
            <i className="legend-gap" /> {t("graph.legendGap")}
          </span>
          <span className="legend-item">
            <i className="legend-structural" /> {t("graph.legendStructural")}
          </span>
        </div>
      </div>
      <div className="graph-canvas" ref={canvasRef}>
        <ReactFlow
          colorMode="dark"
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
          <Background variant={BackgroundVariant.Dots} gap={24} size={1} color="rgba(255,255,255,0.09)" />
          <MiniMap pannable zoomable style={{ width: 150, height: 110 }} nodeColor={(n) => minimapColor(n as XRayFlowNode)} nodeStrokeWidth={0} maskColor={GRAPH_COLORS.mask} />
          <Controls showInteractive={false} />
          <LevelOfDetail target={canvasRef} />
        </ReactFlow>
        <ScanSweep scanKey={scanKey} />
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
