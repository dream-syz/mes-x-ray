import { useEffect, useRef, useState, type KeyboardEvent, type MouseEvent } from "react";
import { MagnifyingGlass, Scan, ShareNetwork } from "@phosphor-icons/react";
import { api } from "../api/client";
import type { GraphNode, NodeType } from "../api/types";
import { useI18n } from "../lib/I18nContext";
import type { XRayController } from "../state/useXRay";
import { Dialog } from "./Dialog";

const MIN_CHARS = 2;
const DEBOUNCE_MS = 120;
const LIMIT = 30;

/** Node types that carry a value, i.e. for which Trace Source has an upstream lineage to show. */
const TRACEABLE: ReadonlySet<NodeType> = new Set<NodeType>(["jsonField", "field", "resultColumn", "column", "intermediateColumn", "expression", "systemParameter"]);

type Pick = "select" | "trace" | "impact";

/**
 * Command-palette style node search (Cmd/Ctrl+K or `/`): type a fragment of a field, procedure, table, column or
 * parameter name, move with the arrow keys, Enter selects the node; each hit also offers Trace Source and Impact.
 */
export function SearchPalette({ controller }: { controller: XRayController }) {
  const { closeDialog, selectNode, traceField, analyzeImpact, setTab } = controller;
  const { t, type, layer, statusShort } = useI18n();
  const [query, setQuery] = useState("");
  const [hits, setHits] = useState<GraphNode[]>([]);
  const [active, setActive] = useState(0);
  const [searching, setSearching] = useState(false);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const listRef = useRef<HTMLUListElement | null>(null);
  const term = query.trim();

  useEffect(() => inputRef.current?.focus(), []);

  useEffect(() => {
    if (term.length < MIN_CHARS) {
      setHits([]);
      setSearching(false);
      return;
    }
    const abort = new AbortController();
    setSearching(true);
    const handle = window.setTimeout(() => {
      api
        .search(term, LIMIT, abort.signal)
        .then((result) => {
          if (abort.signal.aborted) return;
          setHits(result);
          setActive(0);
        })
        .catch(() => {
          if (!abort.signal.aborted) setHits([]);
        })
        .finally(() => {
          if (!abort.signal.aborted) setSearching(false);
        });
    }, DEBOUNCE_MS);
    return () => {
      window.clearTimeout(handle);
      abort.abort();
    };
  }, [term]);

  useEffect(() => {
    listRef.current?.children[active]?.scrollIntoView({ block: "nearest" });
  }, [active]);

  const pick = (node: GraphNode, action: Pick) => {
    closeDialog();
    if (action === "trace") {
      void traceField(node.id);
    } else if (action === "impact") {
      void selectNode(node.id).then(() => analyzeImpact(node.id));
    } else {
      void selectNode(node.id);
      setTab("details");
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setActive((i) => Math.min(i + 1, Math.max(hits.length - 1, 0)));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setActive((i) => Math.max(i - 1, 0));
    } else if (event.key === "Enter" && hits[active]) {
      event.preventDefault();
      pick(hits[active], event.shiftKey && TRACEABLE.has(hits[active].type) ? "trace" : "select");
    }
  };

  const act = (event: MouseEvent, node: GraphNode, action: Pick) => {
    event.stopPropagation();
    pick(node, action);
  };

  return (
    <Dialog label={t("search.title")} className="search-dialog" closeLabel={t("help.close")} onClose={closeDialog}>
      <div className="search-input">
        <MagnifyingGlass size={16} weight="bold" aria-hidden />
        <input
          ref={inputRef}
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          onKeyDown={onKeyDown}
          placeholder={t("search.placeholder")}
          aria-label={t("search.title")}
          role="combobox"
          aria-expanded={hits.length > 0}
          aria-controls="search-results"
          aria-activedescendant={hits[active] ? `search-hit-${active}` : undefined}
          autoComplete="off"
          spellCheck={false}
        />
        <kbd>Esc</kbd>
      </div>

      <div className="search-body">
        {term.length < MIN_CHARS ? (
          <p className="muted search-note">{t("search.minChars")}</p>
        ) : hits.length === 0 ? (
          <p className="muted search-note" role="status">
            {searching ? t("search.searching") : t("search.empty", { q: term })}
          </p>
        ) : (
          <ul id="search-results" ref={listRef} className="search-results" role="listbox" aria-label={t("search.results", { n: hits.length })}>
            {hits.map((node, index) => (
              <li
                key={node.id}
                id={`search-hit-${index}`}
                role="option"
                aria-selected={index === active}
                className={`search-hit ${index === active ? "active" : ""}`}
                onMouseEnter={() => setActive(index)}
                onClick={() => pick(node, "select")}
              >
                <div className="search-hit-main">
                  <div className="search-hit-title">
                    <span className="search-hit-name">{node.name}</span>
                    <span className="search-hit-meta">
                      <span>{layer(node.layer)}</span>
                      <span>{type(node.type)}</span>
                      {node.status !== "known" && <span className={`status-chip ${node.status}`}>{statusShort(node.status)}</span>}
                    </span>
                  </div>
                  <code className="search-hit-id">{node.id}</code>
                </div>
                <div className="search-hit-actions">
                  {TRACEABLE.has(node.type) && (
                    <button type="button" className="btn small" title={t("inspector.traceHint")} onClick={(event) => act(event, node, "trace")}>
                      <Scan size={11} weight="bold" />
                      {t("search.trace")}
                    </button>
                  )}
                  <button type="button" className="btn small" title={t("inspector.impactHint")} onClick={(event) => act(event, node, "impact")}>
                    <ShareNetwork size={11} weight="bold" />
                    {t("search.impact")}
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="dialog-footer">
        <span>{t("search.hint")}</span>
        {hits.length > 0 && <span>{t("search.results", { n: hits.length })}</span>}
      </div>
    </Dialog>
  );
}
