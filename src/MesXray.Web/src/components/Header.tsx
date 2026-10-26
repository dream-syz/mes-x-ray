import { useEffect, useState, type FormEvent } from "react";
import { MagnifyingGlass, Question } from "@phosphor-icons/react";
import { LANGS } from "../lib/i18n";
import { useI18n } from "../lib/I18nContext";
import { SEARCH_SHORTCUT } from "../lib/presentation";
import type { XRayController } from "../state/useXRay";

const ORDERS_LIST_ID = "known-pick-orders";

export function Header({ controller }: { controller: XRayController }) {
  const { state, scopes, knownOrders, setMode, startLiveTrace, stopLiveTrace, setScope, setLang, openDialog } = controller;
  const { t } = useI18n();
  const [orderNo, setOrderNo] = useState(state.overview?.traces[0]?.entityKey ?? "PICK0843858");

  // A live trace started elsewhere (deep link, start guide) shows its order in the box, so the box tells the truth.
  const liveOrder = state.live?.entityKey;
  useEffect(() => {
    if (liveOrder) setOrderNo(liveOrder);
  }, [liveOrder]);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (orderNo.trim()) void startLiveTrace(orderNo.trim());
  };

  return (
    <header className="header">
      <div className="brand">
        <span className="brand-mark" aria-hidden>
          X
        </span>
        <div>
          <div className="brand-title">MES X-Ray</div>
          <div className="brand-sub">{state.overview?.case.title ?? t("header.tagline")}</div>
        </div>
      </div>

      <nav className="mode-switch" aria-label={t("header.viewMode")}>
        <button type="button" className={state.mode === "architecture" ? "active" : ""} title={t("header.modeHintArchitecture")} onClick={() => setMode("architecture")}>
          {t("header.architecture")}
        </button>
        <button type="button" className={state.mode === "trace" ? "active" : ""} title={t("header.modeHintTrace")} onClick={() => setMode("trace")}>
          {t("header.liveTrace")}
        </button>
      </nav>

      <form className="pick-order" onSubmit={submit}>
        <label title={t("header.pickOrderHint", { orders: knownOrders.length > 0 ? knownOrders.join(", ") : "-" })}>
          {t("header.pickOrder")}
          <input value={orderNo} onChange={(e) => setOrderNo(e.target.value)} placeholder={knownOrders[0] ?? "PICK0843858"} list={ORDERS_LIST_ID} spellCheck={false} autoComplete="off" />
          <datalist id={ORDERS_LIST_ID}>
            {knownOrders.map((order) => (
              <option key={order} value={order} />
            ))}
          </datalist>
        </label>
        <button type="submit" className="btn primary" disabled={!!state.busy.live || !orderNo.trim()}>
          {state.busy.live ? t("header.tracing") : t("header.trace")}
        </button>
        {state.live && (
          <>
            <label title={t("header.materialHint")}>
              {t("header.material")}
              <select value={state.scope ?? ""} onChange={(e) => setScope(e.target.value || null)}>
                <option value="">{t("header.orderLevel")}</option>
                {scopes.map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
            </label>
            <span className={`live-badge env-${state.live.environment.toLowerCase()}`} title={t("header.liveTitle", { traceId: state.live.traceId, observedAt: state.live.observedAt })}>
              <span className="dot" aria-hidden />
              {state.live.environment} {state.live.traceId}
            </span>
            <button type="button" className="btn ghost" title={t("header.clearHint")} onClick={stopLiveTrace}>
              {t("header.clear")}
            </button>
          </>
        )}
      </form>

      <div className="header-tools">
        <button type="button" className="btn ghost tool-btn" title={`${t("header.search")} (${SEARCH_SHORTCUT})`} aria-label={t("header.search")} onClick={() => openDialog("search")}>
          <MagnifyingGlass size={15} weight="bold" />
          <kbd>{SEARCH_SHORTCUT}</kbd>
        </button>
        <button type="button" className="btn ghost tool-btn" title={`${t("header.help")} (?)`} aria-label={t("header.help")} onClick={() => openDialog("help")}>
          <Question size={16} weight="bold" />
        </button>
      </div>

      <nav className="mode-switch lang-switch" aria-label={t("header.language")}>
        {LANGS.map((lang) => (
          <button key={lang.id} type="button" lang={lang.id === "zh" ? "zh-CN" : "en"} className={state.lang === lang.id ? "active" : ""} aria-pressed={state.lang === lang.id} onClick={() => setLang(lang.id)}>
            {lang.label}
          </button>
        ))}
      </nav>
    </header>
  );
}
