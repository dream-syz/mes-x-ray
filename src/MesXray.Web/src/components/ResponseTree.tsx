import { useMemo, useState } from "react";
import { CaretDown, CaretRight, MagnifyingGlass, X } from "@phosphor-icons/react";
import type { GraphNode } from "../api/types";
import type { XRayController } from "../state/useXRay";
import { useI18n } from "../lib/I18nContext";
import { formatValue } from "../lib/presentation";

interface TreeLeaf {
  key: string;
  label: string;
  nodeId: string | null;
  value?: unknown;
  scope: string | null;
  depth: number;
  children: TreeLeaf[];
}

/** Turns a JSON path into the graph id convention: array markers dropped, `json:` prefix. */
const toNodeId = (segments: string[]): string => `json:${segments.join(".")}`;

function fromResponse(value: unknown, segments: string[], scope: string | null, depth: number, known: Set<string>): TreeLeaf[] {
  if (Array.isArray(value)) {
    return value.map((item, index) => {
      const itemScope = typeof item === "object" && item !== null && "materialNumber" in item ? String((item as Record<string, unknown>).materialNumber) : scope;
      return {
        key: `${segments.join(".")}[${index}]`,
        label: `[${index}]${itemScope && itemScope !== scope ? ` ${itemScope}` : ""}`,
        nodeId: null,
        scope: itemScope,
        depth,
        children: fromResponse(item, segments, itemScope, depth + 1, known),
      };
    });
  }
  if (typeof value === "object" && value !== null) {
    return Object.entries(value as Record<string, unknown>).map(([name, child]) => {
      const path = [...segments, name];
      const isLeaf = typeof child !== "object" || child === null || (Array.isArray(child) && child.length === 0);
      const nodeId = toNodeId(path);
      return {
        key: path.join(".") + (scope ? `@${scope}` : ""),
        label: name,
        nodeId: known.has(nodeId) ? nodeId : isLeaf ? nodeId : null,
        value: isLeaf ? child : undefined,
        scope,
        depth,
        children: isLeaf ? [] : fromResponse(child, path, scope, depth + 1, known),
      };
    });
  }
  return [];
}

function fromFields(fields: GraphNode[]): TreeLeaf[] {
  const root: TreeLeaf = { key: "$", label: "$", nodeId: null, scope: null, depth: -1, children: [] };
  for (const field of fields) {
    const segments = field.id.replace(/^json:/, "").split(".");
    let cursor = root;
    segments.forEach((segment, index) => {
      let next = cursor.children.find((c) => c.label === segment);
      if (!next) {
        next = { key: segments.slice(0, index + 1).join("."), label: segment, nodeId: index === segments.length - 1 ? field.id : null, scope: null, depth: index, children: [] };
        cursor.children.push(next);
      }
      cursor = next;
    });
  }
  return root.children;
}

export function ResponseTree({ controller }: { controller: XRayController }) {
  const { state, traceField } = controller;
  const { t } = useI18n();
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});
  const known = useMemo(() => new Set(state.responseFields.map((f) => f.id)), [state.responseFields]);
  const descriptions = useMemo(() => new Map(state.responseFields.filter((f) => f.description).map((f) => [f.id, f.description!])), [state.responseFields]);
  const keyFields = useMemo(() => new Set(state.overview?.case.keyFields ?? []), [state.overview]);

  const tree = useMemo(() => {
    if (state.live?.response !== undefined && state.live?.response !== null) {
      return fromResponse(state.live.response, [], null, 0, known);
    }
    return fromFields(state.responseFields);
  }, [state.live, state.responseFields, known]);

  const isSelected = (leaf: TreeLeaf) => leaf.nodeId !== null && leaf.nodeId === state.selectedNodeId && (leaf.scope === null || leaf.scope === state.scope);

  // Branches on the way to the selected field open automatically (unless the user closed them by hand).
  const autoOpen = useMemo(() => {
    const keys = new Set<string>();
    const visit = (leaf: TreeLeaf, ancestors: string[]): void => {
      if (isSelected(leaf)) for (const key of ancestors) keys.add(key);
      for (const child of leaf.children) visit(child, [...ancestors, leaf.key]);
    };
    for (const leaf of tree) visit(leaf, []);
    return keys;
  }, [tree, state.selectedNodeId, state.scope]);

  // Filter: a branch stays when it, or anything below it, matches the typed text (field name or live value); every
  // kept branch is open so that the matches are in sight.
  const [filter, setFilter] = useState("");
  const needle = filter.trim().toLowerCase();
  const kept = useMemo(() => {
    if (!needle) return null;
    const keys = new Set<string>();
    const visit = (leaf: TreeLeaf): boolean => {
      let hit = leaf.label.toLowerCase().includes(needle) || (leaf.value !== undefined && formatValue(leaf.value).toLowerCase().includes(needle));
      for (const child of leaf.children) if (visit(child)) hit = true;
      if (hit) keys.add(leaf.key);
      return hit;
    };
    for (const leaf of tree) visit(leaf);
    return keys;
  }, [tree, needle]);

  const isOpen = (leaf: TreeLeaf) => (kept ? true : expanded[leaf.key] ?? (leaf.depth < 2 || autoOpen.has(leaf.key)));
  const visibleChildren = (leaf: TreeLeaf) => (kept ? leaf.children.filter((child) => kept.has(child.key)) : leaf.children);
  const roots = kept ? tree.filter((leaf) => kept.has(leaf.key)) : tree;

  const renderLeaf = (leaf: TreeLeaf) => {
    const selected = isSelected(leaf);
    const traceable = leaf.nodeId !== null && known.has(leaf.nodeId);
    const children = visibleChildren(leaf);
    const value = leaf.value !== undefined ? formatValue(leaf.value) : null;
    const description = leaf.nodeId ? descriptions.get(leaf.nodeId) : undefined;
    const labelTitle = description ?? (traceable ? t("tree.traceFor", { id: leaf.nodeId }) : leaf.nodeId ? t("tree.notTraceable", { id: leaf.nodeId }) : undefined);
    return (
      <li key={leaf.key}>
        <div className={`tree-row ${selected ? "selected" : ""} ${traceable ? "traceable" : ""}`} style={{ paddingLeft: "4px" }}>
          {leaf.children.length > 0 ? (
            <button type="button" className="tree-toggle" onClick={() => setExpanded((e) => ({ ...e, [leaf.key]: !isOpen(leaf) }))} aria-label={isOpen(leaf) ? t("tree.collapse") : t("tree.expand")}>
              {isOpen(leaf) ? <CaretDown size={11} weight="bold" /> : <CaretRight size={11} weight="bold" />}
            </button>
          ) : (
            <span className="tree-toggle" />
          )}
          <button
            type="button"
            className="tree-label"
            disabled={!traceable}
            title={labelTitle}
            onClick={() => leaf.nodeId && void traceField(leaf.nodeId, leaf.scope ?? (state.live ? null : undefined))}
          >
            <span className={`tree-name ${leaf.nodeId && keyFields.has(leaf.nodeId) ? "key-field" : ""}`}>{leaf.label}</span>
            {value !== null && (
              <span className="tree-value" title={description ? `${description}\n${value}` : value}>
                {value}
              </span>
            )}
          </button>
        </div>
        {children.length > 0 && isOpen(leaf) && <ul>{children.map(renderLeaf)}</ul>}
      </li>
    );
  };

  return (
    <section className="pane pane-left">
      <div className="pane-header">
        <h2>{t("tree.title")}</h2>
        <span className="pane-sub" title={state.live ? `${state.live.entityType} ${state.live.entityKey}` : t("tree.static")}>
          {state.live ? `${state.live.entityType} ${state.live.entityKey}` : t("tree.static")}
        </span>
      </div>
      <div className="pane-tools">
        <MagnifyingGlass size={13} weight="bold" aria-hidden />
        <input className="tree-filter" value={filter} onChange={(e) => setFilter(e.target.value)} placeholder={t("tree.filter")} aria-label={t("tree.filter")} spellCheck={false} />
        {filter && (
          <button type="button" className="btn ghost small icon" onClick={() => setFilter("")} aria-label={t("tree.filterClear")} title={t("tree.filterClear")}>
            <X size={12} weight="bold" />
          </button>
        )}
      </div>
      <div className="pane-body">
        {tree.length === 0 ? <p className="muted">{t("tree.loading")}</p> : roots.length === 0 ? <p className="muted empty">{t("tree.noMatch", { q: filter.trim() })}</p> : <ul className="tree">{roots.map(renderLeaf)}</ul>}
      </div>
      <div className="pane-footer">{t("tree.footer")}</div>
    </section>
  );
}
