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
import { planFolds, type FoldPlan } from "./fold";
import { layoutGraph, type XRayFlowNode } from "./layout";
import { XRayNode } from "./XRayNode";

const nodeTypes = { xray: XRayNode };

const NO_FOLDS: ReadonlySet<string> = new Set<string>();

/** Fold state of the current trace: which functions the user opened, and what the camera should frame next. */
interface FoldState {
  scanKey: string;
  unfolded: ReadonlySet<string>;
  /** Node ids to frame after the next layout (the function just opened and its internals); null = the lit path. */
  reveal: string[] | null;
}

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

/** Camera frame around the given nodes (the whole graph when there are none), never closer than 1:1. */
const frameOptions = (ids: string[], padding: number) => ({ padding, maxZoom: 1, ...(ids.length > 0 ? { nodes: ids.map((id) => ({ id })) } : {}) });

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

  // Functions the lineage enters start folded; the state belongs to one trace and resets with the next one.
  const [foldState, setFoldState] = useState<FoldState>({ scanKey: "", unfolded: NO_FOLDS, reveal: null });
  const unfolded = foldState.scanKey === scanKey ? foldState.unfolded : NO_FOLDS;
  const reveal = foldState.scanKey === scanKey ? foldState.reveal : null;

  const layout = useMemo(() => {
    if (!subgraph) return { nodes: [] as XRayFlowNode[], edges: [], fitIds: [] as string[], fold: null as FoldPlan | null, focusId: null as string | null };
    const runtimeValues = new Map<string, RuntimeValue[]>();
    const highlighted = new Set<string>();
    const pathNodeIds = new Set<string>();
    let focusId: string | null = null;
    let fold: FoldPlan | null = null;

    if (state.mode !== "architecture" && state.trace && subgraph === state.trace.graph) {
      collectHopValues(state.trace.root, runtimeValues, highlighted);
      for (const n of state.trace.executionPath) pathNodeIds.add(n.id);
      focusId = state.trace.field.id;
      fold = planFolds(state.trace, unfolded);
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

    const result = layoutGraph({
      nodes: subgraph.nodes,
      edges: subgraph.edges,
      runtimeValues,
      highlighted,
      pathNodeIds,
      focusId,
      aspect,
      lang,
      hidden: fold?.hidden,
      folded: fold?.folded,
      foldable: fold ? new Set(fold.internals.keys()) : undefined,
    });
    // A trace is framed on its lit path (the traced lineage and the execution path), so the story is legible at first
    // sight and the dimmed structure around it is where the eye can wander; the other views are framed whole.
    return { ...result, fitIds: fold ? result.litIds : [], fold, focusId };
  }, [subgraph, state.mode, state.trace, state.impact, state.tab, state.live, state.scope, state.architecture, state.overview, aspect, lang, unfolded]);

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
    const visible = new Set(layout.nodes.map((n) => n.id));
    const target = (reveal ?? layout.fitIds).filter((id) => visible.has(id));
    const frame = frameOptions(target, target.length > 0 ? 0.12 : 0.08);
    let fallback: number | undefined;
    const handle = window.setTimeout(() => {
      let settled = false;
      void fitView({ ...frame, duration: 400 }).then(() => {
        settled = true;
      });
      setNodes((current) => [...current]);
      fallback = window.setTimeout(() => {
        if (!settled) {
          void fitView({ ...frame, duration: 0 });
          setNodes((current) => [...current]);
        }
      }, 700);
    }, 20);
    return () => {
      window.clearTimeout(handle);
      if (fallback !== undefined) window.clearTimeout(fallback);
    };
  }, [nodesInitialized, layoutVersion, layout.nodes, layout.fitIds, reveal, fitView, setNodes]);

  const fold = layout.fold;
  const foldedCount = fold ? [...fold.folded.values()].reduce((sum, n) => sum + n, 0) : 0;

  // A hop picked in the inspector is mirrored on the canvas: the node gets selected and the camera glides to it and
  // its direct neighbours at a legible zoom; a hop hidden inside a folded function opens that function first, and the
  // traced field itself (the root of the tree) brings the whole lit path back. Clicks on the canvas only select: that
  // node is in view already.
  const selectedNodeId = state.selectedNodeId;
  const canvasClickRef = useRef<string | null>(null);
  const followedRef = useRef<string | null>(null);
  useEffect(() => {
    if (layout.nodes.length === 0) return;
    setNodes((current) => current.map((n) => ((n.selected ?? false) === (n.id === selectedNodeId) ? n : { ...n, selected: n.id === selectedNodeId })));
    if (!selectedNodeId || followedRef.current === selectedNodeId) return;
    followedRef.current = selectedNodeId;
    if (canvasClickRef.current === selectedNodeId) {
      canvasClickRef.current = null;
      return;
    }
    const visible = new Set(layout.nodes.map((n) => n.id));
    if (selectedNodeId === layout.focusId) {
      void fitView({ ...frameOptions(layout.fitIds.filter((id) => visible.has(id)), 0.12), duration: 400 });
      return;
    }
    const neighbours = (subgraph?.edges ?? [])
      .filter((e) => e.fromNodeId === selectedNodeId || e.toNodeId === selectedNodeId)
      .map((e) => (e.fromNodeId === selectedNodeId ? e.toNodeId : e.fromNodeId));
    const frame = [selectedNodeId, ...neighbours];
    if (!visible.has(selectedNodeId)) {
      const owner = fold ? [...fold.internals].find(([, ids]) => ids.includes(selectedNodeId))?.[0] : undefined;
      if (owner) setFoldState({ scanKey, unfolded: new Set([...unfolded, owner]), reveal: frame });
      return;
    }
    void fitView({ ...frameOptions(frame.filter((id) => visible.has(id)), 0.25), duration: 400 });
  }, [selectedNodeId, layoutVersion, layout.nodes, layout.focusId, layout.fitIds, subgraph, fold, scanKey, unfolded, fitView, setNodes]);

  /**
   * Opens or closes the internals of one function. Opening dives the camera into the function's data path (the
   * RETURN expression down to the base columns); the branch conditions around it stay in the picture at its edges.
   */
  const toggleFold = (hopId: string) => {
    if (!fold || !fold.internals.has(hopId)) return;
    const next = new Set(unfolded);
    if (next.has(hopId)) {
      next.delete(hopId);
      setFoldState({ scanKey, unfolded: next, reveal: null });
    } else {
      next.add(hopId);
      setFoldState({ scanKey, unfolded: next, reveal: [hopId, ...(fold.spines.get(hopId) ?? [])] });
    }
  };
  const unfoldAll = () => {
    if (!fold) return;
    setFoldState({ scanKey, unfolded: new Set(fold.internals.keys()), reveal: [...fold.spines].flatMap(([id, ids]) => [id, ...ids]) });
  };
  const foldAll = () => setFoldState({ scanKey, unfolded: NO_FOLDS, reveal: null });

  const onNodeClick: NodeMouseHandler<XRayFlowNode> = (event, node) => {
    // The fold chip on a function node toggles its internals instead of selecting the node.
    if ((event.target as HTMLElement | null)?.closest?.(".xray-node-fold")) {
      toggleFold(node.id);
      return;
    }
    canvasClickRef.current = node.id;
    void selectNode(node.id);
  };

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
        {fold && fold.internals.size > 0 && (
          <button type="button" className="btn ghost small fold-toggle" onClick={foldedCount > 0 ? unfoldAll : foldAll} title={t("graph.foldHint")}>
            {foldedCount > 0 ? t("graph.unfoldAll", { n: foldedCount }) : t("graph.foldAll")}
          </button>
        )}
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
