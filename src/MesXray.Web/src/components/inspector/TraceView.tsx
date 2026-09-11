import { ArrowRight } from "@phosphor-icons/react";
import type { FieldTrace, TraceHop } from "../../api/types";
import { formatValue, relationLabel, STATUS_LABELS, TYPE_LABELS } from "../../lib/presentation";

function Hop({ hop, onSelect }: { hop: TraceHop; onSelect: (id: string) => void }) {
  return (
    <li className={`hop status-${hop.status}`}>
      <div className="hop-row">
        {hop.viaRelation && <span className="hop-relation">{relationLabel(hop.viaRelation)}</span>}
        {hop.condition && <span className="hop-condition">{hop.condition}</span>}
        <button type="button" className="link" onClick={() => onSelect(hop.nodeId)} title={hop.nodeId}>
          <span className="hop-type">{TYPE_LABELS[hop.node.type] ?? hop.node.type}</span>
          {hop.node.name}
        </button>
        {hop.status !== "known" && <span className={`status-chip ${hop.status}`}> {STATUS_LABELS[hop.status]}</span>}
        {hop.isRepeat && <span className="muted"> (already expanded)</span>}
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
  return (
    <div className="trace-view">
      <div className="view-title">
        <h3>Trace Source</h3>
        <code>{trace.field.id}</code>
        {trace.traceId && (
          <span className="status-chip known">
            live {trace.traceId}
            {trace.scope ? ` @ ${trace.scope}` : ""}
          </span>
        )}
      </div>

      {trace.executionPath.length > 0 && (
        <div className="execution-path">
          <span className="section-label">Execution path</span>
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

      <span className="section-label">Upstream lineage</span>
      <ul className="hops">
        <Hop hop={trace.root} onSelect={onSelect} />
      </ul>

      <span className="section-label">Unknowns</span>
      {trace.unknowns.length === 0 ? (
        <p className="ok">Every hop on this path is Known.</p>
      ) : (
        <ul className="unknown-list">
          {trace.unknowns.map((u) => (
            <li key={`${u.nodeId}:${u.reason}`}>
              <button type="button" className="link" onClick={() => onSelect(u.nodeId)}>
                <span className="hop-type">{TYPE_LABELS[u.type] ?? u.type}</span>
                {u.name}
              </button>{" "}
              <span className="status-chip unknown">Need More Evidence</span>
              <div className="muted">{u.reason}</div>
            </li>
          ))}
        </ul>
      )}
      <div className="muted evidence-count">{trace.evidenceIds.length} evidence ids</div>
    </div>
  );
}
