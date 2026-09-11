import { useMemo, useState } from "react";
import { CaretDown, CaretRight } from "@phosphor-icons/react";
import type { GraphNode } from "../api/types";
import type { XRayController } from "../state/useXRay";
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
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});
  const known = useMemo(() => new Set(state.responseFields.map((f) => f.id)), [state.responseFields]);
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

  const isOpen = (leaf: TreeLeaf) => expanded[leaf.key] ?? (leaf.depth < 2 || autoOpen.has(leaf.key));

  const renderLeaf = (leaf: TreeLeaf) => {
    const selected = isSelected(leaf);
    const traceable = leaf.nodeId !== null && known.has(leaf.nodeId);
    return (
      <li key={leaf.key}>
        <div className={`tree-row ${selected ? "selected" : ""} ${traceable ? "traceable" : ""}`} style={{ paddingLeft: "4px" }}>
          {leaf.children.length > 0 ? (
            <button type="button" className="tree-toggle" onClick={() => setExpanded((e) => ({ ...e, [leaf.key]: !isOpen(leaf) }))} aria-label={isOpen(leaf) ? "collapse" : "expand"}>
              {isOpen(leaf) ? <CaretDown size={11} weight="bold" /> : <CaretRight size={11} weight="bold" />}
            </button>
          ) : (
            <span className="tree-toggle" />
          )}
          <button
            type="button"
            className="tree-label"
            disabled={!traceable}
            title={traceable ? `Trace Source for ${leaf.nodeId}` : leaf.nodeId ?? undefined}
            onClick={() => leaf.nodeId && void traceField(leaf.nodeId, leaf.scope ?? (state.live ? null : undefined))}
          >
            <span className={`tree-name ${leaf.nodeId && keyFields.has(leaf.nodeId) ? "key-field" : ""}`}>{leaf.label}</span>
            {leaf.value !== undefined && <span className="tree-value">{formatValue(leaf.value)}</span>}
          </button>
        </div>
        {leaf.children.length > 0 && isOpen(leaf) && <ul>{leaf.children.map(renderLeaf)}</ul>}
      </li>
    );
  };

  return (
    <section className="pane pane-left">
      <div className="pane-header">
        <h2>Response Tree</h2>
        <span className="pane-sub">{state.live ? `${state.live.entityType} ${state.live.entityKey}` : "GET /cwp/v1/picking/pickOrder (static)"}</span>
      </div>
      <div className="pane-body">
        {tree.length === 0 ? <p className="muted">Loading response fields</p> : <ul className="tree">{tree.map(renderLeaf)}</ul>}
      </div>
      <div className="pane-footer">Click a field to run Trace Source. Key demo fields are in accent.</div>
    </section>
  );
}
