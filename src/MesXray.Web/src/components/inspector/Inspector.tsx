import { useState } from "react";
import { Broadcast, Check, Lightbulb, Scan, ShareNetwork } from "@phosphor-icons/react";
import type { MessageKey } from "../../lib/i18n";
import { useI18n } from "../../lib/I18nContext";
import type { InspectorTab, XRayController } from "../../state/useXRay";
import { DetailsView } from "./DetailsView";
import { ExplainView } from "./ExplainView";
import { ImpactView } from "./ImpactView";
import { TraceView } from "./TraceView";

const TABS: { id: InspectorTab; label: MessageKey }[] = [
  { id: "details", label: "tab.details" },
  { id: "trace", label: "tab.trace" },
  { id: "impact", label: "tab.impact" },
  { id: "explain", label: "tab.explain" },
];

export function Inspector({ controller }: { controller: XRayController }) {
  const { state, selectNode, traceField, analyzeImpact, explain, setTab } = controller;
  const { t } = useI18n();
  const [question, setQuestion] = useState("");
  const selected = state.selectedNodeId;
  const canInvestigate = !!state.live;

  const select = (id: string) => void selectNode(id);

  return (
    <section className="pane pane-right">
      <div className="pane-header">
        <h2>{t("inspector.title")}</h2>
        <span className="pane-sub">{selected ?? t("inspector.selectHint")}</span>
      </div>

      <div className="actions">
        <button type="button" className="btn" disabled={!selected || !!state.busy.trace} onClick={() => selected && void traceField(selected)}>
          <Scan size={14} weight="bold" />
          {state.busy.trace ? t("inspector.tracing") : t("inspector.traceSource")}
        </button>
        <button type="button" className="btn" disabled={!selected || !!state.busy.impact} onClick={() => selected && void analyzeImpact(selected)}>
          <ShareNetwork size={14} weight="bold" />
          {state.busy.impact ? t("inspector.analyzing") : t("inspector.impact")}
        </button>
        <span className="spacer" />
        <button type="button" className="btn primary" disabled={!selected || !!state.busy.explain} onClick={() => selected && void explain(selected, false, question || undefined)}>
          <Lightbulb size={14} weight="bold" />
          {state.busy.explain ? t("inspector.explaining") : t("inspector.explain")}
        </button>
        <button
          type="button"
          className="btn primary"
          disabled={!selected || !canInvestigate || !!state.busy.explain}
          title={canInvestigate ? t("inspector.investigateHint") : t("inspector.runLiveFirst")}
          onClick={() => selected && void explain(selected, true, question || undefined)}
        >
          <Broadcast size={14} weight="bold" />
          {t("inspector.investigate")}
        </button>
      </div>
      <input className="question" value={question} onChange={(e) => setQuestion(e.target.value)} placeholder={t("inspector.questionPlaceholder")} aria-label={t("inspector.questionAria")} />

      <nav className="tabs" aria-label={t("inspector.views")}>
        {TABS.map((tab) => (
          <button key={tab.id} type="button" className={state.tab === tab.id ? "active" : ""} onClick={() => setTab(tab.id)}>
            {t(tab.label)}
            {tab.id === "trace" &&
              state.trace &&
              (state.trace.unknowns.length > 0 ? (
                <span className="count warn" title={t("tab.unknownHops", { n: state.trace.unknowns.length })}>
                  {state.trace.unknowns.length}
                </span>
              ) : (
                <span className="count" title={t("tab.allKnown")}>
                  <Check size={10} weight="bold" />
                </span>
              ))}
            {tab.id === "impact" && state.impact && (
              <span className="count" title={t("tab.affected", { n: state.impact.affected.length })}>
                {state.impact.affected.length}
              </span>
            )}
            {tab.id === "explain" && state.explanation && (
              <span className="count" title={t("tab.facts", { n: state.explanation.explanation.knownFacts.length })}>
                {state.explanation.explanation.knownFacts.length}
              </span>
            )}
          </button>
        ))}
      </nav>

      <div className="pane-body">
        <div key={state.tab} className="tab-panel">
          {state.tab === "details" && (state.details ? <DetailsView response={state.details} onSelect={select} /> : <Empty text={t("empty.details")} />)}
          {state.tab === "trace" && (state.trace ? <TraceView trace={state.trace} onSelect={select} /> : <Empty text={t("empty.trace")} />)}
          {state.tab === "impact" && (state.impact ? <ImpactView impact={state.impact} onSelect={select} /> : <Empty text={t("empty.impact")} />)}
          {state.tab === "explain" && (state.explanation ? <ExplainView response={state.explanation} onSelect={select} /> : <Empty text={t("empty.explain")} />)}
        </div>
      </div>
    </section>
  );
}

function Empty({ text }: { text: string }) {
  return <p className="muted empty">{text}</p>;
}
