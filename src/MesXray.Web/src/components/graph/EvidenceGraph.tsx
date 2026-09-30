import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type RefObject } from "react";
import { Play, Stop } from "@phosphor-icons/react";
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
import { layoutGraph, type ReplayMark, type XRayFlowEdge, type XRayFlowNode } from "./layout";
import { planReplay, type ReplayStep } from "./replay";
import { XRayEdge } from "./XRayEdge";
import { XRayNode } from "./XRayNode";

const nodeTypes = { xray: XRayNode };
const edgeTypes = { xray: XRayEdge };

const NO_FOLDS: ReadonlySet<string> = new Set<string>();

/** Replay pacing: one step per layer of the path, then a hold once the value has landed in the field. */
const REPLAY_STEP_MS = 520;
const REPLAY_ARRIVE_MS = 1800;
/** A deep-linked replay starts once the scan sweep (900 ms) and the camera glide (400 ms) are over. */
const REPLAY_AUTO_DELAY_MS = 1500;
/** A node on both legs keeps the stronger state (the function is called on the way down and returns on the way up). */
const REPLAY_WEIGHT: Record<ReplayMark, number> = { pending: 0, visited: 1, current: 2, arrived: 3 };

/** A running (or armed) replay of the current trace. */
interface ReplayState {
  scanKey: string;
  steps: ReplayStep[];
  /** Step on screen; -1 = armed (the path is dark, nothing has moved yet), steps.length = the value has arrived. */
  index: number;
  running: boolean;
}

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
    if (!subgraph) {
      return { nodes: [] as XRayFlowNode[], edges: [] as XRayFlowEdge[], fitIds: [] as string[], litIds: [] as string[], fold: null as FoldPlan | null, focusId: null as string | null };
    }
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
  const [edges, setEdges, onEdgesChange] = useEdgesState<XRayFlowEdge>(layout.edges);
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

  // Replay: the traced path goes dark and lights up step by step, down the call chain and back up the data flow
  // (replay.ts). It is a light show over the current layout, so folding, a new trace or a resize cancels it.
  const [replay, setReplay] = useState<ReplayState | null>(null);
  const replayable = fold !== null && state.trace !== null && layout.litIds.length > 1;
  const startReplay = useCallback(
    (armed = false) => {
      if (!fold || !state.trace) return;
      setReplay({ scanKey, steps: planReplay(state.trace, fold.hidden), index: armed ? -1 : 0, running: !armed });
    },
    [fold, state.trace, scanKey],
  );
  const stopReplay = () => setReplay(null);
  useEffect(() => setReplay(null), [layout]);

  useEffect(() => {
    if (!replay?.running) return;
    const arrived = replay.index >= replay.steps.length;
    const handle = window.setTimeout(() => {
      setReplay((r) => (!r || !r.running ? r : r.index >= r.steps.length ? null : { ...r, index: r.index + 1 }));
    }, arrived ? REPLAY_ARRIVE_MS : REPLAY_STEP_MS);
    return () => window.clearTimeout(handle);
  }, [replay]);

  // The replay state is painted onto the nodes and edges React Flow holds: a mark per node, and per edge either the
  // pulse of the current step or the dark class of a step still to come. Nothing else about them changes.
  const edgeBaseClass = useMemo(() => new Map(layout.edges.map((e) => [e.id, e.className ?? ""])), [layout.edges]);
  useEffect(() => {
    const marks = new Map<string, ReplayMark>();
    const pulses = new Map<string, { key: number; reverse: boolean }>();
    const ahead = new Set<string>();
    const behind = new Set<string>();
    if (replay && replay.scanKey === scanKey) {
      replay.steps.forEach((step, i) => {
        const mark: ReplayMark = i < replay.index ? "visited" : i === replay.index ? "current" : "pending";
        for (const id of step.nodeIds) {
          const previous = marks.get(id);
          if (!previous || REPLAY_WEIGHT[mark] > REPLAY_WEIGHT[previous]) marks.set(id, mark);
        }
        for (const e of step.edges) {
          if (i === replay.index) pulses.set(e.id, { key: i, reverse: e.reverse });
          (i > replay.index ? ahead : behind).add(e.id);
        }
      });
      if (replay.index >= replay.steps.length && layout.focusId) marks.set(layout.focusId, "arrived");
    }
    setNodes((current) => current.map((n) => (n.data.replay === marks.get(n.id) ? n : { ...n, data: { ...n.data, replay: marks.get(n.id) } })));
    setEdges((current) =>
      current.map((e) => {
        const pulse = pulses.get(e.id) ?? null;
        const dark = ahead.has(e.id) && !behind.has(e.id);
        const base = edgeBaseClass.get(e.id) ?? "";
        const className = dark ? `${base} edge-replay-dark` : pulse ? `${base} edge-replay-pulse` : base;
        const had = e.data?.pulse ?? null;
        const samePulse = had === pulse || (had !== null && pulse !== null && had.key === pulse.key && had.reverse === pulse.reverse);
        if (samePulse && className === (e.className ?? "")) return e;
        return { ...e, className, data: { edge: e.data!.edge, pulse: pulse ? { ...pulse, ms: REPLAY_STEP_MS } : null } };
      }),
    );
  }, [replay, scanKey, layoutVersion, layout.focusId, edgeBaseClass, setNodes, setEdges]);

  // Deep links (`replay=1` / `replay=hold`) replay every trace as it arrives, once the sweep and the glide are over.
  const startRef = useRef(startReplay);
  startRef.current = startReplay;
  const autoReplayedRef = useRef<string | null>(null);
  useEffect(() => {
    const mode = state.replayOnLoad;
    if (!mode || !replayable || !nodesInitialized || autoReplayedRef.current === scanKey) return;
    const handle = window.setTimeout(() => {
      autoReplayedRef.current = scanKey;
      startRef.current(mode === "hold");
    }, REPLAY_AUTO_DELAY_MS);
    return () => window.clearTimeout(handle);
  }, [state.replayOnLoad, replayable, nodesInitialized, scanKey]);

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
        {replayable && (
          <button
            type="button"
            className={`btn ghost small replay-toggle ${replay?.running ? "is-running" : ""}`}
            onClick={() => (replay?.running ? stopReplay() : startReplay())}
            title={t("graph.replayHint")}
          >
            {replay?.running ? <Stop size={11} weight="fill" /> : <Play size={11} weight="fill" />}
            {replay?.running ? t("graph.replayStop") : t("graph.replay")}
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
          edgeTypes={edgeTypes}
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
