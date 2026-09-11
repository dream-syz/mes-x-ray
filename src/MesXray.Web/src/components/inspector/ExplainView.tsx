import { useMemo, useState, type CSSProperties } from "react";
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
            {id.length > 34 ? `${id.slice(0, 34)}...` : id}
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
      <div className="view-title">
        <h3>{explanation.audit.provider === "rules" ? "Explain" : "AI Explain"}</h3>
        <code>{response.focusNodeId}</code>
      </div>

      <div className={`verdict ${verdict.className}`} style={{ "--confidence": percent(explanation.confidence) } as CSSProperties}>
        <div className="verdict-label">{verdict.label}</div>
        <div className="verdict-meta">
          <span className="metric" title="Confidence is capped by the evidence-binding validator">
            <b>{percent(explanation.confidence)}</b> confidence
          </span>
          <span className="metric">
            <b>{explanation.evidenceCount}</b> evidence items
          </span>
          <span className="metric">
            <b>{explanation.knownFacts.length}</b> facts
          </span>
          <span className="metric">
            <b>{explanation.unknowns.length}</b> unknowns
          </span>
        </div>
      </div>
      <p className="summary">{explanation.summary}</p>

      <span className="section-label">Auditable steps</span>
      <ol className="steps">
        {explanation.steps.map((s) => (
          <li key={s}>{s}</li>
        ))}
      </ol>

      <span className="section-label">Known facts, each bound to evidence</span>
      <ul className="facts">
        {explanation.knownFacts.map((f) => (
          <li key={f.text}>
            <div>{f.text}</div>
            <EvidenceChips ids={f.evidenceIds} index={index} onSelect={onSelect} />
          </li>
        ))}
      </ul>

      <span className="section-label">Hypotheses</span>
      {explanation.hypotheses.length === 0 ? (
        <p className="muted">None. Every claim is evidenced.</p>
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

      <span className="section-label">Unknowns</span>
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
        <dl className="metadata">
          <div>
            <dt>provider</dt>
            <dd>
              <code>{explanation.audit.provider}</code>
            </dd>
          </div>
          <div>
            <dt>model</dt>
            <dd>
              <code>{explanation.audit.model}</code>
            </dd>
          </div>
          <div>
            <dt>prompt</dt>
            <dd>
              <code>{explanation.audit.promptVersion}</code>
            </dd>
          </div>
          <div>
            <dt>timestamp</dt>
            <dd>{new Date(explanation.audit.timestamp).toLocaleString()}</dd>
          </div>
          <div>
            <dt>evidence ids</dt>
            <dd>{explanation.audit.evidenceIds.length}</dd>
          </div>
          {explanation.audit.note && (
            <div>
              <dt>note</dt>
              <dd>{explanation.audit.note}</dd>
            </div>
          )}
        </dl>
      </div>

      <button type="button" className="btn ghost small" style={{ marginTop: 10 }} onClick={() => setShowEvidence((v) => !v)}>
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
