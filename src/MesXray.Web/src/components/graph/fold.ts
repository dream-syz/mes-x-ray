import type { FieldTrace, TraceHop } from "../../api/types";

/**
 * Folding keeps a deep trace readable. When the lineage enters a SQL function, everything the function does inside
 * (its RETURN expression, CTEs, CASE branches, the base columns it reads) is one subtree of the trace. Folded, that
 * subtree collapses into the function node with a "+n inside" chip; unfolded, it is laid out in full.
 */
export interface FoldPlan {
  /** Hop node id -> the node ids inside it (its upstream subtree in the trace), in tree order. */
  internals: Map<string, string[]>;
  /**
   * Hop node id -> the data path inside it: the nodes the value itself flows through (RETURN expression, CTE column,
   * base columns), without the columns that merely decide a branch. Narrow, so the camera can dive into it.
   */
  spines: Map<string, string[]>;
  /** Node ids folded away in the current state. */
  hidden: Set<string>;
  /** Hop node id -> number of nodes folded into it; only hops that are currently folded. */
  folded: Map<string, number>;
}

/** Hops that own a foldable subtree: scanned SQL functions the lineage continues into. */
const isFoldable = (hop: TraceHop): boolean => hop.node.type === "function" && hop.sources.length > 0 && !hop.isRepeat;

/** Relations through which a value only decides a branch or a filter; they are not the data path. */
const isControl = (hop: TraceHop): boolean => hop.viaRelation === "controlledBy" || hop.viaRelation === "usesParameter" || hop.viaRelation === "reads";

export function planFolds(trace: FieldTrace, unfolded: ReadonlySet<string>): FoldPlan {
  const internals = new Map<string, Set<string>>();
  const spines = new Map<string, string[]>();
  // Nodes that also appear outside any function stay visible whatever the fold state (the parameter a CASE and the
  // function both read, the execution path, the focus field).
  const outside = new Set<string>([trace.field.id, ...trace.executionPath.map((n) => n.id)]);

  const collect = (hop: TraceHop, into: Set<string>): void => {
    for (const source of hop.sources) {
      into.add(source.nodeId);
      collect(source, into);
    }
  };
  const spineOf = (hop: TraceHop, into: string[]): void => {
    for (const source of hop.sources) {
      if (isControl(source)) continue;
      if (!into.includes(source.nodeId)) into.push(source.nodeId);
      spineOf(source, into);
    }
  };
  const visit = (hop: TraceHop, inside: boolean): void => {
    const foldable = isFoldable(hop);
    if (foldable && !internals.has(hop.nodeId)) {
      const ids = new Set<string>();
      collect(hop, ids);
      ids.delete(hop.nodeId);
      internals.set(hop.nodeId, ids);
      const spine: string[] = [];
      spineOf(hop, spine);
      spines.set(hop.nodeId, spine);
    }
    if (!inside) outside.add(hop.nodeId);
    for (const source of hop.sources) visit(source, inside || foldable);
  };
  visit(trace.root, false);

  const hidden = new Set<string>();
  const folded = new Map<string, number>();
  for (const [hopId, ids] of internals) {
    if (unfolded.has(hopId)) continue;
    let count = 0;
    for (const id of ids) {
      if (outside.has(id)) continue;
      hidden.add(id);
      count += 1;
    }
    folded.set(hopId, count);
  }
  // A node shared with an unfolded function is shown, whichever function it was first attributed to.
  for (const [hopId, ids] of internals) {
    if (!unfolded.has(hopId)) continue;
    for (const id of ids) hidden.delete(id);
  }

  return { internals: new Map([...internals].map(([id, ids]) => [id, [...ids]])), spines, hidden, folded };
}
