import { useState, type FormEvent } from "react";
import type { XRayController } from "../state/useXRay";

export function Header({ controller }: { controller: XRayController }) {
  const { state, scopes, setMode, startLiveTrace, stopLiveTrace, setScope } = controller;
  const [orderNo, setOrderNo] = useState(state.overview?.traces[0]?.entityKey ?? "PICK0843858");

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (orderNo.trim()) void startLiveTrace(orderNo.trim());
  };

  return (
    <header className="header">
      <div className="brand">
        <span className="brand-mark">X</span>
        <div>
          <div className="brand-title">MES X-Ray</div>
          <div className="brand-sub">{state.overview?.case.title ?? "System explainability & data lineage"}</div>
        </div>
      </div>

      <nav className="mode-switch" aria-label="View mode">
        <button type="button" className={state.mode === "architecture" ? "active" : ""} onClick={() => setMode("architecture")}>
          Architecture
        </button>
        <button type="button" className={state.mode === "trace" ? "active" : ""} onClick={() => setMode("trace")}>
          Live Trace
        </button>
      </nav>

      <form className="pick-order" onSubmit={submit}>
        <label>
          Pick order
          <input value={orderNo} onChange={(e) => setOrderNo(e.target.value)} placeholder="PICK0843858" spellCheck={false} />
        </label>
        <button type="submit" disabled={!!state.busy.live}>
          {state.busy.live ? "Tracing..." : "Trace"}
        </button>
        {state.live && (
          <>
            <label>
              Material
              <select value={state.scope ?? ""} onChange={(e) => setScope(e.target.value || null)}>
                <option value="">(order level)</option>
                {scopes.map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
            </label>
            <span className={`env-badge env-${state.live.environment.toLowerCase()}`} title={`Trace ${state.live.traceId} observed ${state.live.observedAt}`}>
              {state.live.environment} · {state.live.traceId}
            </span>
            <button type="button" className="ghost" onClick={stopLiveTrace}>
              Clear
            </button>
          </>
        )}
      </form>
    </header>
  );
}
