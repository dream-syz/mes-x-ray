import { useI18n } from "../lib/I18nContext";
import type { XRayController } from "../state/useXRay";

export function StatusBar({ controller }: { controller: XRayController }) {
  const { state, dismissError } = controller;
  const { t } = useI18n();
  const build = state.overview?.build;
  const gaps = state.overview?.knownGaps ?? [];

  return (
    <footer className="statusbar">
      {state.error ? (
        <span className="status-error" role="alert">
          {state.error}
          <button type="button" className="btn ghost small" onClick={dismissError}>
            {t("status.dismiss")}
          </button>
        </span>
      ) : state.notice ? (
        <span className="status-notice">{state.notice}</span>
      ) : (
        <span>{t("status.readonly")}</span>
      )}
      {build && (
        <span className="status-metrics" title={t("status.buildTitle", { at: build.builtAt, mode: build.mode })}>
          <span className="metric">
            <b>{build.nodes}</b> {t("metric.nodes")}
          </span>
          <span className="metric">
            <b>{build.edges}</b> {t("metric.edges")}
          </span>
          <span className="metric">
            <b>{build.lineages}</b> {t("metric.lineage")}
          </span>
          <span className="metric">
            <b>{build.unknownNodes}</b> {t("metric.unknown")}
          </span>
          <span className="metric">
            <b>{build.pendingNodes}</b> {t("metric.pending")}
          </span>
          {gaps.length > 0 && (
            <span className="metric status-gaps" title={gaps.map((g) => `${g.priority} ${g.nodeId}: ${g.reason}`).join("\n")}>
              <b>{gaps.length}</b> {gaps.length === 1 ? t("metric.knownGapOne") : t("metric.knownGaps")}
            </span>
          )}
        </span>
      )}
    </footer>
  );
}
