import { useState } from "react";
import type { InspectorTab, XRayController } from "../../state/useXRay";
import { DetailsView } from "./DetailsView";
import { ExplainView } from "./ExplainView";
import { ImpactView } from "./ImpactView";
import { TraceView } from "./TraceView";

const TABS: { id: InspectorTab; label: string }[] = [
  { id: "details", label: "Details" },
  { id: "trace", label: "Trace Source" },
  { id: "impact", label: "Impact" },
  { id: "explain", label: "Explain" },
];

export function Inspector({ controller }: { controller: XRayController }) {
  const { state, selectNode, traceField, analyzeImpact, explain, setTab } = controller;
  const [question, setQuestion] = useState("");
  const selected = state.selectedNodeId;
  const canInvestigate = !!state.live;

  const select = (id: string) => void selectNode(id);

  return (
    <section className="pane pane-right">
      <div className="pane-header">
        <h2>Inspector</h2>
        <span className="pane-sub">{selected ?? "select a node or a response field"}</span>
      </div>

      <div className="actions">
        <button type="button" disabled={!selected || !!state.busy.trace} onClick={() => selected && void traceField(selected)}>
          {state.busy.trace ? "Tracing…" : "Trace Source"}
        </button>
        <button type="button" disabled={!selected || !!state.busy.impact} onClick={() => selected && void analyzeImpact(selected)}>
          {state.busy.impact ? "Analyzing…" : "Impact"}
        </button>
        <button type="button" className="primary" disabled={!selected || !!state.busy.explain} onClick={() => selected && void explain(selected, false, question || undefined)}>
          {state.busy.explain ? "Explaining…" : "Explain"}
        </button>
        <button
          type="button"
          className="primary"
          disabled={!selected || !canInvestigate || !!state.busy.explain}
          title={canInvestigate ? "Explain with the live trace and generate hypotheses" : "Run a Live Trace first"}
          onClick={() => selected && void explain(selected, true, question || undefined)}
        >
          Investigate
        </button>
      </div>
      <input className="question" value={question} onChange={(e) => setQuestion(e.target.value)} placeholder="Optional question, e.g. Why is Available Quantity 0?" />

      <nav className="tabs">
        {TABS.map((tab) => (
          <button key={tab.id} type="button" className={state.tab === tab.id ? "active" : ""} onClick={() => setTab(tab.id)}>
            {tab.label}
            {tab.id === "trace" && state.trace && <em>{state.trace.unknowns.length > 0 ? ` ${state.trace.unknowns.length}?` : " ✓"}</em>}
            {tab.id === "explain" && state.explanation && <em> {state.explanation.explanation.knownFacts.length}</em>}
          </button>
        ))}
      </nav>

      <div className="pane-body">
        {state.tab === "details" && (state.details ? <DetailsView response={state.details} onSelect={select} /> : <Empty text="Select a node in the graph or a field in the response tree." />)}
        {state.tab === "trace" && (state.trace ? <TraceView trace={state.trace} onSelect={select} /> : <Empty text="Run Trace Source on a field to see its upstream lineage." />)}
        {state.tab === "impact" && (state.impact ? <ImpactView impact={state.impact} onSelect={select} /> : <Empty text="Run Impact on a parameter, column, function or procedure." />)}
        {state.tab === "explain" && (state.explanation ? <ExplainView response={state.explanation} onSelect={select} /> : <Empty text="Explain produces evidence-bound facts; anything without evidence is reported as Unknown / Need More Evidence." />)}
      </div>
    </section>
  );
}

function Empty({ text }: { text: string }) {
  return <p className="muted empty">{text}</p>;
}
