import { ArrowRight } from "@phosphor-icons/react";
import type { FieldTrace, TraceHop } from "../../api/types";
import { useI18n } from "../../lib/I18nContext";
import { formatValue } from "../../lib/presentation";

function Hop({ hop, onSelect }: { hop: TraceHop; onSelect: (id: string) => void }) {
  const { t, type, status, relation } = useI18n();
  return (
    <li className={`hop status-${hop.status}`}>
      <div className="hop-row">
        {hop.viaRelation && <span className="hop-relation">{relation(hop.viaRelation)}</span>}
        {hop.condition && <span className="hop-condition">{hop.condition}</span>}
        <button type="button" className="link" onClick={() => onSelect(hop.nodeId)} title={hop.nodeId}>
          <span className="hop-type">{type(hop.node.type)}</span>
          {hop.node.name}
        </button>
        {hop.status !== "known" && <span className={`status-chip ${hop.status}`}> {status(hop.status)}</span>}
        {hop.isRepeat && <span className="muted"> {t("trace.alreadyExpanded")}</span>}
      </div>
      {hop.expression && hop.node.type === "expression" && <pre className="hop-expression">{hop.expression}</pre>}
      {hop.runtimeValues.length > 0 && (
        <div className="hop-values">
          {hop.runtimeValues.map((v) => (
            <span key={v.evidenceId} className="value-chip" title={`${v.evidenceId} (${v.evidenceType})`}>
              {v.label} = {formatValue(v.value)}
            </span>
          ))}
        </div>
      )}
      {hop.sources.length > 0 && (
        <ul className="hop-children">
          {hop.sources.map((s) => (
            <Hop key={`${hop.nodeId}->${s.nodeId}:${s.condition ?? ""}`} hop={s} onSelect={onSelect} />
          ))}
        </ul>
      )}
    </li>
  );
}

export function TraceView({ trace, onSelect }: { trace: FieldTrace; onSelect: (id: string) => void }) {
  const { t, type } = useI18n();
  return (
    <div className="trace-view">
      <div className="view-title">
        <h3>{t("trace.title")}</h3>
        <code>{trace.field.id}</code>
        {trace.traceId && (
          <span className="status-chip known">
            {t("trace.live", { traceId: trace.traceId })}
            {trace.scope ? ` @ ${trace.scope}` : ""}
          </span>
        )}
      </div>

      {trace.executionPath.length > 0 && (
        <div className="execution-path">
          <span className="section-label">{t("section.executionPath")}</span>
          <div className="path-chain">
            {trace.executionPath.map((n, i) => (
              <span key={n.id} style={{ display: "contents" }}>
                {i > 0 && (
                  <span className="path-arrow" aria-hidden>
                    <ArrowRight size={12} />
                  </span>
                )}
                <button type="button" className={`path-node status-${n.status}`} onClick={() => onSelect(n.id)} title={n.id}>
                  {n.name}
                </button>
              </span>
            ))}
          </div>
        </div>
      )}

      <span className="section-label">{t("section.upstream")}</span>
      <ul className="hops">
        <Hop hop={trace.root} onSelect={onSelect} />
      </ul>

      <span className="section-label">{t("section.unknowns")}</span>
      {trace.unknowns.length === 0 ? (
        <p className="ok">{t("trace.allKnown")}</p>
      ) : (
        <ul className="unknown-list">
          {trace.unknowns.map((u) => (
            <li key={`${u.nodeId}:${u.reason}`}>
              <button type="button" className="link" onClick={() => onSelect(u.nodeId)}>
                <span className="hop-type">{type(u.type)}</span>
                {u.name}
              </button>{" "}
              <span className="status-chip unknown">{t("chip.needMoreEvidence")}</span>
              <div className="muted">{u.reason}</div>
            </li>
          ))}
        </ul>
      )}
      <div className="muted evidence-count">{t("trace.evidenceIds", { n: trace.evidenceIds.length })}</div>
    </div>
  );
}
