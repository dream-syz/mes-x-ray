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
          <button type="button" className="btn ghost small" onClick={dismissError}>
            dismiss
          </button>
        </span>
      ) : state.notice ? (
        <span className="status-notice">{state.notice}</span>
      ) : (
        <span>Read-only: whitelisted runtime tools only, no SQL execution, no parameter or data changes.</span>
      )}
      {build && (
        <span className="status-metrics" title={`built ${build.builtAt}, mode ${build.mode}`}>
          <span className="metric">
            <b>{build.nodes}</b> nodes
          </span>
          <span className="metric">
            <b>{build.edges}</b> edges
          </span>
          <span className="metric">
            <b>{build.lineages}</b> lineage
          </span>
          <span className="metric">
            <b>{build.unknownNodes}</b> unknown
          </span>
          <span className="metric">
            <b>{build.pendingNodes}</b> pending
          </span>
          {gaps.length > 0 && (
            <span className="metric status-gaps" title={gaps.map((g) => `${g.priority} ${g.nodeId}: ${g.reason}`).join("\n")}>
              <b>{gaps.length}</b> known gap{gaps.length === 1 ? "" : "s"}
            </span>
          )}
        </span>
      )}
    </footer>
  );
}
