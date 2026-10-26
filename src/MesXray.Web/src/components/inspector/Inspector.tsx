import { useState } from "react";
import { Broadcast, Check, Lightbulb, MagnifyingGlass, Scan, ShareNetwork } from "@phosphor-icons/react";
import type { MessageKey } from "../../lib/i18n";
import { useI18n } from "../../lib/I18nContext";
import { SEARCH_SHORTCUT } from "../../lib/presentation";
import type { InspectorTab, XRayController, XRayState } from "../../state/useXRay";
import { DetailsView } from "./DetailsView";
import { ExplainView } from "./ExplainView";
import { ImpactView } from "./ImpactView";
import { TraceView } from "./TraceView";

interface TabSpec {
  id: InspectorTab;
  label: MessageKey;
  hint: MessageKey;
  busy: keyof XRayState["busy"];
  busyLabel: MessageKey;
}

const TABS: Record<InspectorTab, TabSpec> = {
  details: { id: "details", label: "tab.details", hint: "tab.detailsHint", busy: "details", busyLabel: "busy.details" },
  trace: { id: "trace", label: "tab.trace", hint: "tab.traceHint", busy: "trace", busyLabel: "busy.trace" },
  impact: { id: "impact", label: "tab.impact", hint: "tab.impactHint", busy: "impact", busyLabel: "busy.impact" },
  explain: { id: "explain", label: "tab.explain", hint: "tab.explainHint", busy: "explain", busyLabel: "busy.explain" },
};
const TAB_ORDER: InspectorTab[] = ["details", "trace", "impact", "explain"];

export function Inspector({ controller }: { controller: XRayController }) {
  const { state, selectNode, traceField, analyzeImpact, explain, setTab } = controller;
  const { t, type, layer } = useI18n();
  const [question, setQuestion] = useState("");
  const selected = state.selectedNodeId;
  const canInvestigate = !!state.live;
  const node = state.details?.details.node ?? null;
  const tab = TABS[state.tab];
  const tabBusy = !!state.busy[tab.busy];
  // Nothing selected yet: the guide takes the place of the (empty) tab panels, unless a request is already on its way.
  const pendingLoad = (["live", "trace", "impact", "explain", "details"] as const).find((key) => state.busy[key]);

  const select = (id: string) => void selectNode(id);

  return (
    <section className="pane pane-right">
      <div className="pane-header">
        <h2>{t("inspector.title")}</h2>
        <span className="pane-sub" title={selected ?? undefined}>
          {selected ?? t("inspector.selectHint")}
        </span>
      </div>

      <div className="actions">
        <button type="button" className="btn" disabled={!selected || !!state.busy.trace} title={t("inspector.traceHint")} onClick={() => selected && void traceField(selected)}>
          <Scan size={14} weight="bold" />
          {state.busy.trace ? t("inspector.tracing") : t("inspector.traceSource")}
        </button>
        <button type="button" className="btn" disabled={!selected || !!state.busy.impact} title={t("inspector.impactHint")} onClick={() => selected && void analyzeImpact(selected)}>
          <ShareNetwork size={14} weight="bold" />
          {state.busy.impact ? t("inspector.analyzing") : t("inspector.impact")}
        </button>
        <span className="spacer" />
        <button
          type="button"
          className="btn primary"
          disabled={!selected || !!state.busy.explain}
          title={t("inspector.explainHint")}
          onClick={() => selected && void explain(selected, false, question || undefined)}
        >
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

      {/* What the four buttons will act on, in words rather than a node id. */}
      <div className="selection-line" aria-live="polite">
        {selected ? (
          <>
            <span className="muted">{t("inspector.selected")}</span>
            {node && node.id === selected ? (
              <>
                <span className="chip">{layer(node.layer)}</span>
                <span className="chip">{type(node.type)}</span>
                <b title={node.id}>{node.name}</b>
              </>
            ) : (
              <code>{selected}</code>
            )}
          </>
        ) : (
          <span className="muted">{t("inspector.nothingSelected", { shortcut: SEARCH_SHORTCUT })}</span>
        )}
      </div>
      {selected && !canInvestigate && <p className="action-note">{t("inspector.investigateNeedsLive")}</p>}

      <input className="question" value={question} onChange={(e) => setQuestion(e.target.value)} placeholder={t("inspector.questionPlaceholder")} aria-label={t("inspector.questionAria")} />

      <nav className="tabs" aria-label={t("inspector.views")}>
        {TAB_ORDER.map((id) => TABS[id]).map((item) => (
          <button key={item.id} type="button" className={state.tab === item.id ? "active" : ""} title={t(item.hint)} onClick={() => setTab(item.id)}>
            {t(item.label)}
            {item.id === "trace" &&
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
            {item.id === "impact" && state.impact && (
              <span className="count" title={t("tab.affected", { n: state.impact.affected.length })}>
                {state.impact.affected.length}
              </span>
            )}
            {item.id === "explain" && state.explanation && (
              <span className="count" title={t("tab.facts", { n: state.explanation.explanation.knownFacts.length })}>
                {state.explanation.explanation.knownFacts.length}
              </span>
            )}
          </button>
        ))}
      </nav>

      <div className="pane-body">
        {!selected ? (
          pendingLoad ? (
            <Loading text={t(`busy.${pendingLoad}`)} />
          ) : (
            <StartGuide controller={controller} />
          )
        ) : (
          <div key={state.tab} className="tab-panel">
            {state.tab === "details" && (state.details ? <DetailsView response={state.details} onSelect={select} /> : <Pending busy={tabBusy} busyText={t(tab.busyLabel)} text={t("empty.details")} />)}
            {state.tab === "trace" && (state.trace ? <TraceView trace={state.trace} onSelect={select} /> : <Pending busy={tabBusy} busyText={t(tab.busyLabel)} text={t("empty.trace")} />)}
            {state.tab === "impact" && (state.impact ? <ImpactView impact={state.impact} onSelect={select} /> : <Pending busy={tabBusy} busyText={t(tab.busyLabel)} text={t("empty.impact")} />)}
            {state.tab === "explain" && (state.explanation ? <ExplainView response={state.explanation} onSelect={select} /> : <Pending busy={tabBusy} busyText={t(tab.busyLabel)} text={t("empty.explain")} />)}
          </div>
        )}
      </div>
    </section>
  );
}

function Pending({ busy, busyText, text }: { busy: boolean; busyText: string; text: string }) {
  return busy ? <Loading text={busyText} /> : <p className="muted empty">{text}</p>;
}

function Loading({ text }: { text: string }) {
  return (
    <p className="muted empty loading" role="status">
      <span className="spinner" aria-hidden />
      {text}
    </p>
  );
}

/**
 * The first thing a new user sees in the inspector: what X-Ray does, the three moves that cover the whole demo, and
 * one-click ways to make them (key fields of the case, the sample pick order, the node search).
 */
function StartGuide({ controller }: { controller: XRayController }) {
  const { state, knownOrders, startLiveTrace, traceField, openDialog } = controller;
  const { t } = useI18n();
  const keyFields = state.overview?.keyFields ?? [];
  const sampleOrder = knownOrders[0];

  return (
    <div className="start-guide">
      <h3>{t("guide.title")}</h3>
      <p className="guide-intro">{t("guide.intro")}</p>
      <ol className="guide-steps">
        <li>
          <b>{t("guide.step1Title")}</b>
          <span>{t("guide.step1")}</span>
        </li>
        <li>
          <b>{t("guide.step2Title")}</b>
          <span>{t("guide.step2")}</span>
        </li>
        <li>
          <b>{t("guide.step3Title")}</b>
          <span>{t("guide.step3")}</span>
        </li>
      </ol>

      <span className="section-label">{t("guide.try")}</span>
      <div className="guide-actions">
        {sampleOrder && !state.live && (
          <button type="button" className="btn primary" disabled={!!state.busy.live} onClick={() => void startLiveTrace(sampleOrder)}>
            <Broadcast size={14} weight="bold" />
            {t("guide.loadSample", { order: sampleOrder })}
          </button>
        )}
        <button type="button" className="btn" onClick={() => openDialog("search")}>
          <MagnifyingGlass size={14} weight="bold" />
          {t("guide.searchAny")}
          <kbd>{SEARCH_SHORTCUT}</kbd>
        </button>
      </div>

      {keyFields.length > 0 && (
        <>
          <span className="section-label">{t("guide.keyFields")}</span>
          <div className="guide-fields">
            {keyFields.map((field) => (
              <button key={field.id} type="button" className="btn small" title={t("tree.traceFor", { id: field.id })} disabled={!!state.busy.trace} onClick={() => void traceField(field.id)}>
                <Scan size={12} weight="bold" />
                {t("guide.traceField", { field: field.name })}
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  );
}
