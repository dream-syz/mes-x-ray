import { useEffect, useState } from "react";
import { api } from "../../api/client";
import type { Explanation, ImpactResult } from "../../api/types";
import { relationLabel, TYPE_LABELS } from "../../lib/presentation";

export function ImpactView({ impact, onSelect }: { impact: ImpactResult; onSelect: (id: string) => void }) {
  const [summary, setSummary] = useState<Explanation | null>(null);

  useEffect(() => {
    let cancelled = false;
    setSummary(null);
    api
      .impactSummary(impact.origin.id)
      .then((r) => {
        if (!cancelled) setSummary(r.explanation);
      })
      .catch(() => {
        if (!cancelled) setSummary(null);
      });
    return () => {
      cancelled = true;
    };
  }, [impact.origin.id]);

  const byDepth = impact.affected.reduce<Map<number, ImpactResult["affected"]>>((acc, item) => {
    acc.set(item.depth, [...(acc.get(item.depth) ?? []), item]);
    return acc;
  }, new Map());

  return (
    <div className="impact-view">
      <h3>
        Impact Analysis · <code>{impact.origin.id}</code>
      </h3>
      {summary && <p className="summary">{summary.summary}</p>}

      <div className="summary-chips">
        {Object.entries(impact.summary).map(([type, count]) => (
          <span key={type} className="chip">
            {count} {type}
          </span>
        ))}
        {impact.truncated && <span className="warn-chip">truncated</span>}
      </div>

      <span className="section-label">Key paths to the surface</span>
      <ol className="key-paths">
        {impact.keyPaths.map((path) => (
          <li key={path.join(">")}>
            {path.map((id, i) => (
              <span key={id}>
                {i > 0 && <span className="path-arrow">→</span>}
                <button type="button" className="link" onClick={() => onSelect(id)} title={id}>
                  {id.replace(/^[a-z]+:/, "")}
                </button>
              </span>
            ))}
          </li>
        ))}
      </ol>

      <span className="section-label">Affected by distance</span>
      {Array.from(byDepth.entries())
        .sort((a, b) => a[0] - b[0])
        .map(([depth, items]) => (
          <div key={depth} className="depth-group">
            <div className="muted">depth {depth}</div>
            <ul>
              {items.map((item) => (
                <li key={item.node.id}>
                  <button type="button" className="link" onClick={() => onSelect(item.node.id)} title={item.node.id}>
                    <span className="hop-type">{TYPE_LABELS[item.node.type] ?? item.node.type}</span> {item.node.name}
                  </button>
                  {item.viaRelation && <span className="hop-relation">{relationLabel(item.viaRelation)}</span>}
                </li>
              ))}
            </ul>
          </div>
        ))}
    </div>
  );
}
