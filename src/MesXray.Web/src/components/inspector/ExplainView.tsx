import { useMemo, useState, type CSSProperties } from "react";
import type { EvidenceItem, ExplainResponse } from "../../api/types";
import type { MessageKey } from "../../lib/i18n";
import { useI18n } from "../../lib/I18nContext";
import { percent } from "../../lib/presentation";

const VERDICT: Record<ExplainResponse["explanation"]["verdict"], { label: MessageKey; className: string }> = {
  known: { label: "verdict.known", className: "verdict-known" },
  needMoreEvidence: { label: "verdict.needMoreEvidence", className: "verdict-partial" },
  unknown: { label: "verdict.unknown", className: "verdict-unknown" },
};

const HYPOTHESIS_STATUS: Record<string, MessageKey | undefined> = {
  unverified: "hypothesis.unverified",
  supported: "hypothesis.supported",
  refuted: "hypothesis.refuted",
};

function EvidenceChips({ ids, index, onSelect }: { ids: string[]; index: Map<string, EvidenceItem>; onSelect: (id: string) => void }) {
  return (
    <span className="evidence-chips">
      {ids.map((id) => {
        const item = index.get(id);
        const nodeId = item?.nodeId ?? (id.includes(":") && !id.startsWith("edge:") && !id.startsWith("lineage:") ? id : null);
        return (
          <button key={id} type="button" className={`evidence-chip kind-${item?.kind ?? "missing"}`} title={item?.text ?? id} onClick={() => nodeId && onSelect(nodeId)} disabled={!nodeId}>
            {id.length > 34 ? `${id.slice(0, 34)}...` : id}
          </button>
        );
      })}
    </span>
  );
}

export function ExplainView({ response, onSelect }: { response: ExplainResponse; onSelect: (id: string) => void }) {
  const { explanation, evidence } = response;
  const { t } = useI18n();
  const [showEvidence, setShowEvidence] = useState(false);
  const index = useMemo(() => new Map(evidence.map((e) => [e.id, e])), [evidence]);
  const verdict = VERDICT[explanation.verdict];

  return (
    <div className="explain-view">
      <div className="view-title">
        <h3>{explanation.audit.provider === "rules" ? t("explain.title") : t("explain.aiTitle")}</h3>
        <code>{response.focusNodeId}</code>
      </div>

      <div className={`verdict ${verdict.className}`} style={{ "--confidence": percent(explanation.confidence) } as CSSProperties}>
        <div className="verdict-label">{t(verdict.label)}</div>
        <div className="verdict-meta">
          <span className="metric" title={t("explain.confidenceHint")}>
            <b>{percent(explanation.confidence)}</b> {t("metric.confidence")}
          </span>
          <span className="metric">
            <b>{explanation.evidenceCount}</b> {t("metric.evidenceItems")}
          </span>
          <span className="metric">
            <b>{explanation.knownFacts.length}</b> {t("metric.facts")}
          </span>
          <span className="metric">
            <b>{explanation.unknowns.length}</b> {t("metric.unknowns")}
          </span>
        </div>
      </div>
      <p className="summary">{explanation.summary}</p>

      <span className="section-label">{t("section.steps")}</span>
      <ol className="steps">
        {explanation.steps.map((s) => (
          <li key={s}>{s}</li>
        ))}
      </ol>

      <span className="section-label">{t("section.facts")}</span>
      <ul className="facts">
        {explanation.knownFacts.map((f) => (
          <li key={f.text}>
            <div>{f.text}</div>
            <EvidenceChips ids={f.evidenceIds} index={index} onSelect={onSelect} />
          </li>
        ))}
      </ul>

      <span className="section-label">{t("section.hypotheses")}</span>
      {explanation.hypotheses.length === 0 ? (
        <p className="muted">{t("explain.noHypotheses")}</p>
      ) : (
        <ul className="hypotheses">
          {explanation.hypotheses.map((h) => {
            const statusKey = HYPOTHESIS_STATUS[h.status];
            return (
              <li key={h.text}>
                <span className={`status-chip hypothesis-${h.status}`}>{statusKey ? t(statusKey) : h.status}</span> {h.text}
                {h.suggestedCheck && <div className="muted">{t("explain.check", { check: h.suggestedCheck })}</div>}
                {h.evidenceIds && h.evidenceIds.length > 0 && <EvidenceChips ids={h.evidenceIds} index={index} onSelect={onSelect} />}
              </li>
            );
          })}
        </ul>
      )}

      <span className="section-label">{t("section.unknowns")}</span>
      {explanation.unknowns.length === 0 ? (
        <p className="ok">{t("explain.noGaps")}</p>
      ) : (
        <ul className="unknown-list">
          {explanation.unknowns.map((u) => (
            <li key={u}>
              <span className="status-chip unknown">{t("chip.needMoreEvidence")}</span> {u}
            </li>
          ))}
        </ul>
      )}

      {explanation.nextSteps.length > 0 && (
        <>
          <span className="section-label">{t("section.nextSteps")}</span>
          <ul className="next-steps">
            {explanation.nextSteps.map((s) => (
              <li key={s}>{s}</li>
            ))}
          </ul>
        </>
      )}

      <div className="audit">
        <span className="section-label">{t("section.audit")}</span>
        <dl className="metadata">
          <div>
            <dt>{t("audit.provider")}</dt>
            <dd>
              <code>{explanation.audit.provider}</code>
            </dd>
          </div>
          <div>
            <dt>{t("audit.model")}</dt>
            <dd>
              <code>{explanation.audit.model}</code>
            </dd>
          </div>
          <div>
            <dt>{t("audit.prompt")}</dt>
            <dd>
              <code>{explanation.audit.promptVersion}</code>
            </dd>
          </div>
          <div>
            <dt>{t("audit.timestamp")}</dt>
            <dd>{new Date(explanation.audit.timestamp).toLocaleString()}</dd>
          </div>
          <div>
            <dt>{t("audit.evidenceIds")}</dt>
            <dd>{explanation.audit.evidenceIds.length}</dd>
          </div>
          {explanation.audit.note && (
            <div>
              <dt>{t("audit.note")}</dt>
              <dd>{explanation.audit.note}</dd>
            </div>
          )}
        </dl>
      </div>

      <button type="button" className="btn ghost small" style={{ marginTop: 10 }} onClick={() => setShowEvidence((v) => !v)}>
        {showEvidence ? t("explain.hideBundle", { n: evidence.length }) : t("explain.showBundle", { n: evidence.length })}
      </button>
      {showEvidence && (
        <ul className="evidence-bundle">
          {evidence.map((e) => (
            <li key={e.id}>
              <code>{e.id}</code> <span className={`kind kind-${e.kind}`}>{e.kind}</span> {e.text}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
