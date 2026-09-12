import { useState, type FormEvent } from "react";
import { LANGS } from "../lib/i18n";
import { useI18n } from "../lib/I18nContext";
import type { XRayController } from "../state/useXRay";

export function Header({ controller }: { controller: XRayController }) {
  const { state, scopes, setMode, startLiveTrace, stopLiveTrace, setScope, setLang } = controller;
  const { t } = useI18n();
  const [orderNo, setOrderNo] = useState(state.overview?.traces[0]?.entityKey ?? "PICK0843858");

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
        <button type="button" className={state.mode === "architecture" ? "active" : ""} onClick={() => setMode("architecture")}>
          {t("header.architecture")}
        </button>
        <button type="button" className={state.mode === "trace" ? "active" : ""} onClick={() => setMode("trace")}>
          {t("header.liveTrace")}
        </button>
      </nav>

      <form className="pick-order" onSubmit={submit}>
        <label>
          {t("header.pickOrder")}
          <input value={orderNo} onChange={(e) => setOrderNo(e.target.value)} placeholder="PICK0843858" spellCheck={false} />
        </label>
        <button type="submit" className="btn primary" disabled={!!state.busy.live}>
          {state.busy.live ? t("header.tracing") : t("header.trace")}
        </button>
        {state.live && (
          <>
            <label>
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
            <button type="button" className="btn ghost" onClick={stopLiveTrace}>
              {t("header.clear")}
            </button>
          </>
        )}
      </form>

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
