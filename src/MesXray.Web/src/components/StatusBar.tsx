import type { XRayController } from "../state/useXRay";

export function StatusBar({ controller }: { controller: XRayController }) {
  const { state, dismissError } = controller;
  const build = state.overview?.build;
  const gaps = state.overview?.knownGaps ?? [];

  return (
    <footer className="statusbar">
      {state.error ? (
        <span className="status-error" role="alert">
          {state.error}
          <button type="button" className="ghost small" onClick={dismissError}>
            dismiss
          </button>
        </span>
      ) : state.notice ? (
        <span className="status-notice">{state.notice}</span>
      ) : (
        <span className="muted">Read-only: whitelisted runtime tools only, no SQL execution, no parameter or data changes.</span>
      )}
      {build && (
        <span className="status-build" title={`built ${build.builtAt} · mode ${build.mode}`}>
          graph {build.nodes} nodes · {build.edges} edges · {build.lineages} lineage · {build.unknownNodes} unknown · {build.pendingNodes} pending
        </span>
      )}
      {gaps.length > 0 && (
        <span className="status-gaps" title={gaps.map((g) => `${g.priority} ${g.nodeId}: ${g.reason}`).join("\n")}>
          {gaps.length} known gap{gaps.length === 1 ? "" : "s"}
        </span>
      )}
    </footer>
  );
}
