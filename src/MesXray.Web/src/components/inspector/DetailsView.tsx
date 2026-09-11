import { ArrowLeft, ArrowRight } from "@phosphor-icons/react";
import type { NodeDetailsResponse } from "../../api/types";
import { formatValue, LAYERS, relationLabel, STATUS_LABELS, TYPE_LABELS } from "../../lib/presentation";

export function DetailsView({ response, onSelect }: { response: NodeDetailsResponse; onSelect: (id: string) => void }) {
  const { details, runtimeValues } = response;
  const node = details.node;
  const metadata = Object.entries(node.metadata ?? {}).filter(([key]) => key !== "reason");

  return (
    <div className="details-view">
      <div className="node-title">
        <span>{LAYERS[node.layer]?.label ?? node.layer}</span>
        <span>{TYPE_LABELS[node.type] ?? node.type}</span>
        <span className={`status-chip ${node.status}`}>{STATUS_LABELS[node.status]}</span>
      </div>
      <h3 title={node.id}>{node.name}</h3>
      <code className="node-id">{node.id}</code>
      {node.qualifiedName && node.qualifiedName !== node.name && <div className="muted qualified">{node.qualifiedName}</div>}
      {node.description && <p className="description">{node.description}</p>}
      {node.metadata?.reason && <p className="gap-reason">{node.metadata.reason}</p>}
      {node.source && (
        <div className="source">
          <span className="section-label">Source</span>
          <code>
            {node.source.path}
            {node.source.startLine ? `:L${node.source.startLine}${node.source.endLine && node.source.endLine !== node.source.startLine ? `-L${node.source.endLine}` : ""}` : ""}
          </code>
          {node.scanVersion && <span className="muted"> ({node.scanVersion})</span>}
        </div>
      )}

      {runtimeValues.length > 0 && (
        <>
          <span className="section-label">Live values</span>
          <ul className="runtime-values">
            {runtimeValues.map((v) => (
              <li key={v.evidenceId}>
                <span className="value-chip">
                  {v.label} = {formatValue(v.value)}
                </span>{" "}
                <code className="muted">{v.evidenceId}</code> <span className="muted">({v.evidenceType})</span>
              </li>
            ))}
          </ul>
        </>
      )}

      {metadata.length > 0 && (
        <>
          <span className="section-label">Metadata</span>
          <dl className="metadata">
            {metadata.map(([key, value]) => (
              <div key={key}>
                <dt>{key}</dt>
                <dd>{value}</dd>
              </div>
            ))}
          </dl>
        </>
      )}

      {details.lineageAsOutput.length > 0 && (
        <>
          <span className="section-label">Lineage (as output)</span>
          <ul className="lineage">
            {details.lineageAsOutput.map((l) => (
              <li key={l.id}>
                <span className="chip">{l.transformType}</span>
                {l.condition && <span className="hop-condition">{l.condition}</span>}
                {l.sourceFieldId ? (
                  <button type="button" className="link" onClick={() => onSelect(l.sourceFieldId!)}>
                    {l.sourceFieldId}
                  </button>
                ) : (
                  <span className="muted">(no source: {l.transformType === "literal" ? "literal value" : "unresolved"})</span>
                )}
                {l.expression && <pre className="hop-expression">{l.expression}</pre>}
              </li>
            ))}
          </ul>
        </>
      )}

      {details.container && (
        <div className="container">
          <span className="section-label">Contained in</span>
          <button type="button" className="link" onClick={() => onSelect(details.container!.id)}>
            {details.container.name}
          </button>
        </div>
      )}

      <span className="section-label">Edges ({details.incoming.length + details.outgoing.length})</span>
      <ul className="edges">
        {details.outgoing.map((e) => (
          <li key={e.id}>
            <span className="edge-dir" aria-label="outgoing">
              <ArrowRight size={12} />
            </span>
            <span className="hop-relation">{relationLabel(e.relationType)}</span>
            <button type="button" className="link" onClick={() => onSelect(e.toNodeId)}>
              {e.toNodeId}
            </button>
            {e.metadata?.condition && <span className="hop-condition">{e.metadata.condition}</span>}
            <span className="muted">({e.evidenceType})</span>
          </li>
        ))}
        {details.incoming.map((e) => (
          <li key={e.id}>
            <span className="edge-dir" aria-label="incoming">
              <ArrowLeft size={12} />
            </span>
            <span className="hop-relation">{relationLabel(e.relationType)}</span>
            <button type="button" className="link" onClick={() => onSelect(e.fromNodeId)}>
              {e.fromNodeId}
            </button>
            {e.metadata?.condition && <span className="hop-condition">{e.metadata.condition}</span>}
            <span className="muted">({e.evidenceType})</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
