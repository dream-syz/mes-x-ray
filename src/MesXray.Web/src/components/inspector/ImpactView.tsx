import { useEffect, useState } from "react";
import { ArrowRight } from "@phosphor-icons/react";
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
      <div className="view-title">
        <h3>Impact Analysis</h3>
        <code>{impact.origin.id}</code>
      </div>

      <div className="verdict verdict-known">
        <div className="verdict-label">
          {impact.affected.length} affected node{impact.affected.length === 1 ? "" : "s"}
        </div>
        <div className="verdict-meta">
          {Object.entries(impact.summary).map(([type, count]) => (
            <span key={type} className="metric">
              <b>{count}</b> {type}
            </span>
          ))}
          {impact.truncated && <span className="chip-warn">truncated</span>}
        </div>
      </div>
      {summary && <p className="summary">{summary.summary}</p>}

      <span className="section-label">Key paths to the surface</span>
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

      <span className="section-label">Affected by distance</span>
      {Array.from(byDepth.entries())
        .sort((a, b) => a[0] - b[0])
        .map(([depth, items]) => (
          <div key={depth} className="depth-group">
            <div className="depth">depth {depth}</div>
            <ul>
              {items.map((item) => (
                <li key={item.node.id}>
                  <button type="button" className="link" onClick={() => onSelect(item.node.id)} title={item.node.id}>
                    <span className="hop-type">{TYPE_LABELS[item.node.type] ?? item.node.type}</span>
                    {item.node.name}
                  </button>
                  {item.viaRelation && <span className="hop-relation"> {relationLabel(item.viaRelation)}</span>}
                </li>
              ))}
            </ul>
          </div>
        ))}
    </div>
  );
}
