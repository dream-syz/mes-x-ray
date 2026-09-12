import { ArrowLeft, ArrowRight } from "@phosphor-icons/react";
import type { NodeDetailsResponse } from "../../api/types";
import { useI18n } from "../../lib/I18nContext";
import { formatValue } from "../../lib/presentation";

export function DetailsView({ response, onSelect }: { response: NodeDetailsResponse; onSelect: (id: string) => void }) {
  const { details, runtimeValues } = response;
  const { t, type, status, layer, relation } = useI18n();
  const node = details.node;
  const metadata = Object.entries(node.metadata ?? {}).filter(([key]) => key !== "reason");

  return (
    <div className="details-view">
      <div className="node-title">
        <span>{layer(node.layer)}</span>
        <span>{type(node.type)}</span>
        <span className={`status-chip ${node.status}`}>{status(node.status)}</span>
      </div>
      <h3 title={node.id}>{node.name}</h3>
      <code className="node-id">{node.id}</code>
      {node.qualifiedName && node.qualifiedName !== node.name && <div className="muted qualified">{node.qualifiedName}</div>}
      {node.description && <p className="description">{node.description}</p>}
      {node.metadata?.reason && <p className="gap-reason">{node.metadata.reason}</p>}
      {node.source && (
        <div className="source">
          <span className="section-label">{t("section.source")}</span>
          <code>
            {node.source.path}
            {node.source.startLine ? `:L${node.source.startLine}${node.source.endLine && node.source.endLine !== node.source.startLine ? `-L${node.source.endLine}` : ""}` : ""}
          </code>
          {node.scanVersion && <span className="muted"> ({node.scanVersion})</span>}
        </div>
      )}

      {runtimeValues.length > 0 && (
        <>
          <span className="section-label">{t("section.liveValues")}</span>
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
          <span className="section-label">{t("section.metadata")}</span>
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
          <span className="section-label">{t("section.lineageOut")}</span>
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
                  <span className="muted">{l.transformType === "literal" ? t("details.noSourceLiteral") : t("details.noSourceUnresolved")}</span>
                )}
                {l.expression && <pre className="hop-expression">{l.expression}</pre>}
              </li>
            ))}
          </ul>
        </>
      )}

      {details.container && (
        <div className="container">
          <span className="section-label">{t("section.containedIn")}</span>
          <button type="button" className="link" onClick={() => onSelect(details.container!.id)}>
            {details.container.name}
          </button>
        </div>
      )}

      <span className="section-label">{t("section.edges", { n: details.incoming.length + details.outgoing.length })}</span>
      <ul className="edges">
        {details.outgoing.map((e) => (
          <li key={e.id}>
            <span className="edge-dir" aria-label={t("details.outgoing")}>
              <ArrowRight size={12} />
            </span>
            <span className="hop-relation">{relation(e.relationType)}</span>
            <button type="button" className="link" onClick={() => onSelect(e.toNodeId)}>
              {e.toNodeId}
            </button>
            {e.metadata?.condition && <span className="hop-condition">{e.metadata.condition}</span>}
            <span className="muted">({e.evidenceType})</span>
          </li>
        ))}
        {details.incoming.map((e) => (
          <li key={e.id}>
            <span className="edge-dir" aria-label={t("details.incoming")}>
              <ArrowLeft size={12} />
            </span>
            <span className="hop-relation">{relation(e.relationType)}</span>
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
