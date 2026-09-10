import { useMemo, useState } from "react";
import type { EvidenceItem, ExplainResponse } from "../../api/types";
import { percent } from "../../lib/presentation";

const VERDICT: Record<ExplainResponse["explanation"]["verdict"], { label: string; className: string }> = {
  known: { label: "Known", className: "verdict-known" },
  needMoreEvidence: { label: "Need More Evidence", className: "verdict-partial" },
  unknown: { label: "Unknown", className: "verdict-unknown" },
};

function EvidenceChips({ ids, index, onSelect }: { ids: string[]; index: Map<string, EvidenceItem>; onSelect: (id: string) => void }) {
  return (
    <span className="evidence-chips">
      {ids.map((id) => {
        const item = index.get(id);
        const nodeId = item?.nodeId ?? (id.includes(":") && !id.startsWith("edge:") && !id.startsWith("lineage:") ? id : null);
        return (
          <button key={id} type="button" className={`evidence-chip kind-${item?.kind ?? "missing"}`} title={item?.text ?? id} onClick={() => nodeId && onSelect(nodeId)} disabled={!nodeId}>
            {id.length > 34 ? `${id.slice(0, 34)}…` : id}
          </button>
        );
      })}
    </span>
  );
}

export function ExplainView({ response, onSelect }: { response: ExplainResponse; onSelect: (id: string) => void }) {
  const { explanation, evidence } = response;
  const [showEvidence, setShowEvidence] = useState(false);
  const index = useMemo(() => new Map(evidence.map((e) => [e.id, e])), [evidence]);
  const verdict = VERDICT[explanation.verdict];

  return (
    <div className="explain-view">
      <h3>
        AI Explain · <code>{response.focusNodeId}</code>
      </h3>
      <div className="verdict-row">
        <span className={`verdict ${verdict.className}`}>{verdict.label}</span>
        <span className="confidence" title="Confidence is capped by the evidence-binding validator">
          confidence {percent(explanation.confidence)}
        </span>
        <span className="muted">{explanation.evidenceCount} evidence items</span>
      </div>
      <p className="summary">{explanation.summary}</p>

      <span className="section-label">Auditable steps</span>
      <ol className="steps">
        {explanation.steps.map((s) => (
          <li key={s}>{s}</li>
        ))}
      </ol>

      <span className="section-label">Known facts ({explanation.knownFacts.length}) — each bound to evidence</span>
      <ul className="facts">
        {explanation.knownFacts.map((f) => (
          <li key={f.text}>
            <div>{f.text}</div>
            <EvidenceChips ids={f.evidenceIds} index={index} onSelect={onSelect} />
          </li>
        ))}
      </ul>

      <span className="section-label">Hypotheses ({explanation.hypotheses.length})</span>
      {explanation.hypotheses.length === 0 ? (
        <p className="muted">None — every claim is evidenced.</p>
      ) : (
        <ul className="hypotheses">
          {explanation.hypotheses.map((h) => (
            <li key={h.text}>
              <span className={`status-chip hypothesis-${h.status}`}>{h.status}</span> {h.text}
              {h.suggestedCheck && <div className="muted">Check (read-only): {h.suggestedCheck}</div>}
              {h.evidenceIds && h.evidenceIds.length > 0 && <EvidenceChips ids={h.evidenceIds} index={index} onSelect={onSelect} />}
            </li>
          ))}
        </ul>
      )}

      <span className="section-label">Unknowns ({explanation.unknowns.length})</span>
      {explanation.unknowns.length === 0 ? (
        <p className="ok">No gaps on this path.</p>
      ) : (
        <ul className="unknown-list">
          {explanation.unknowns.map((u) => (
            <li key={u}>
              <span className="status-chip unknown">Need More Evidence</span> {u}
            </li>
          ))}
        </ul>
      )}

      {explanation.nextSteps.length > 0 && (
        <>
          <span className="section-label">Next steps</span>
          <ul className="next-steps">
            {explanation.nextSteps.map((s) => (
              <li key={s}>{s}</li>
            ))}
          </ul>
        </>
      )}

      <div className="audit">
        <span className="section-label">Audit</span>
        <div className="muted">
          provider <code>{explanation.audit.provider}</code> · model <code>{explanation.audit.model}</code> · prompt <code>{explanation.audit.promptVersion}</code> ·{" "}
          {new Date(explanation.audit.timestamp).toLocaleString()} · {explanation.audit.evidenceIds.length} evidence ids
          {explanation.audit.note && <> · {explanation.audit.note}</>}
        </div>
      </div>

      <button type="button" className="ghost small" onClick={() => setShowEvidence((v) => !v)}>
        {showEvidence ? "Hide" : "Show"} evidence bundle ({evidence.length})
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
