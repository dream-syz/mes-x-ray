import { useEffect, useState } from "react";
import { ArrowRight } from "@phosphor-icons/react";
import { api } from "../../api/client";
import type { Explanation, ImpactResult, NodeType } from "../../api/types";
import { useI18n } from "../../lib/I18nContext";

export function ImpactView({ impact, onSelect }: { impact: ImpactResult; onSelect: (id: string) => void }) {
  const { t, lang, type, relation } = useI18n();
  const [summary, setSummary] = useState<Explanation | null>(null);

  // The summary text follows the UI language; the structural result (paths, counts) is language independent.
  useEffect(() => {
    let cancelled = false;
    setSummary(null);
    api
      .impactSummary(impact.origin.id, lang)
      .then((r) => {
        if (!cancelled) setSummary(r.explanation);
      })
      .catch(() => {
        if (!cancelled) setSummary(null);
      });
    return () => {
      cancelled = true;
    };
  }, [impact.origin.id, lang]);

  const byDepth = impact.affected.reduce<Map<number, ImpactResult["affected"]>>((acc, item) => {
    acc.set(item.depth, [...(acc.get(item.depth) ?? []), item]);
    return acc;
  }, new Map());

  return (
    <div className="impact-view">
      <div className="view-title">
        <h3>{t("impact.title")}</h3>
        <code>{impact.origin.id}</code>
      </div>

      <div className="verdict verdict-known">
        <div className="verdict-label">{impact.affected.length === 1 ? t("impact.affectedOne") : t("impact.affectedMany", { n: impact.affected.length })}</div>
        <div className="verdict-meta">
          {Object.entries(impact.summary).map(([nodeType, count]) => (
            <span key={nodeType} className="metric">
              {/* Summary keys are the API's enum names (PascalCase); the UI type labels are keyed camelCase. */}
              <b>{count}</b> {type((nodeType.charAt(0).toLowerCase() + nodeType.slice(1)) as NodeType)}
            </span>
          ))}
          {impact.truncated && <span className="chip-warn">{t("graph.truncated")}</span>}
        </div>
      </div>
      {summary && <p className="summary">{summary.summary}</p>}

      <span className="section-label">{t("section.keyPaths")}</span>
      <ol className="key-paths">
        {impact.keyPaths.map((path) => (
          <li key={path.join(">")}>
            <span className="path-chain">
              {path.map((id, i) => (
                <span key={id} style={{ display: "contents" }}>
                  {i > 0 && (
                    <span className="path-arrow" aria-hidden>
                      <ArrowRight size={12} />
                    </span>
                  )}
                  <button type="button" className="link" onClick={() => onSelect(id)} title={id}>
                    {id.replace(/^[a-z]+:/, "")}
                  </button>
                </span>
              ))}
            </span>
          </li>
        ))}
      </ol>

      <span className="section-label">{t("section.byDistance")}</span>
      {Array.from(byDepth.entries())
        .sort((a, b) => a[0] - b[0])
        .map(([depth, items]) => (
          <div key={depth} className="depth-group">
            <div className="depth">{t("impact.depth", { n: depth })}</div>
            <ul>
              {items.map((item) => (
                <li key={item.node.id}>
                  <button type="button" className="link" onClick={() => onSelect(item.node.id)} title={item.node.id}>
                    <span className="hop-type">{type(item.node.type)}</span>
                    {item.node.name}
                  </button>
                  {item.viaRelation && <span className="hop-relation"> {relation(item.viaRelation)}</span>}
                </li>
              ))}
            </ul>
          </div>
        ))}
    </div>
  );
}
