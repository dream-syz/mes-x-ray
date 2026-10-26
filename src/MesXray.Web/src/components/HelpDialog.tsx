import { useI18n } from "../lib/I18nContext";
import { SEARCH_SHORTCUT } from "../lib/presentation";
import type { XRayController } from "../state/useXRay";
import { Dialog } from "./Dialog";

/** One screen that explains the workflow, what the colours mean and the keyboard shortcuts. */
export function HelpDialog({ controller }: { controller: XRayController }) {
  const { closeDialog } = controller;
  const { t } = useI18n();

  return (
    <Dialog label={t("help.title")} title={t("help.title")} className="help-dialog" closeLabel={t("help.close")} onClose={closeDialog}>
      <div className="help-body">
        <section>
          <h4>{t("help.flowTitle")}</h4>
          <ol className="help-flow">
            <li>{t("help.flow1")}</li>
            <li>{t("help.flow2")}</li>
            <li>{t("help.flow3")}</li>
            <li>{t("help.flow4")}</li>
            <li>{t("help.flow5")}</li>
          </ol>
        </section>

        <section>
          <h4>{t("help.legendTitle")}</h4>
          <ul className="help-legend">
            <li>
              <i className="swatch swatch-accent" aria-hidden />
              {t("help.legendAccent")}
            </li>
            <li>
              <i className="swatch swatch-warn" aria-hidden />
              {t("help.legendWarn")}
            </li>
            <li>
              <i className="swatch swatch-danger" aria-hidden />
              {t("help.legendDanger")}
            </li>
            <li>
              <i className="swatch swatch-grey" aria-hidden />
              {t("help.legendGrey")}
            </li>
          </ul>
        </section>

        <section>
          <h4>{t("help.keysTitle")}</h4>
          <dl className="help-keys">
            <div>
              <dt>
                <kbd>{SEARCH_SHORTCUT}</kbd> <kbd>/</kbd>
              </dt>
              <dd>{t("help.keySearch")}</dd>
            </div>
            <div>
              <dt>
                <kbd>?</kbd>
              </dt>
              <dd>{t("help.keyHelp")}</dd>
            </div>
            <div>
              <dt>
                <kbd>Enter</kbd>
              </dt>
              <dd>{t("help.keyEnter")}</dd>
            </div>
            <div>
              <dt>
                <kbd>Esc</kbd>
              </dt>
              <dd>{t("help.keyClose")}</dd>
            </div>
          </dl>
        </section>
      </div>
      <div className="dialog-footer">
        <span />
        <button type="button" className="btn small" onClick={closeDialog}>
          {t("help.close")}
        </button>
      </div>
    </Dialog>
  );
}
