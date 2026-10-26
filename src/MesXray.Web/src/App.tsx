import { useEffect, type CSSProperties } from "react";
import { WarningCircle } from "@phosphor-icons/react";
import { EvidenceGraph } from "./components/graph/EvidenceGraph";
import { Header } from "./components/Header";
import { HelpDialog } from "./components/HelpDialog";
import { Inspector } from "./components/inspector/Inspector";
import { PaneResizer, usePaneWidths } from "./components/PaneResizer";
import { ResponseTree } from "./components/ResponseTree";
import { SearchPalette } from "./components/SearchPalette";
import { StatusBar } from "./components/StatusBar";
import { I18nProvider } from "./lib/I18nContext";
import { translate } from "./lib/i18n";
import { useXRay, type XRayController } from "./state/useXRay";

const isTyping = (target: EventTarget | null): boolean => {
  const element = target as HTMLElement | null;
  if (!element) return false;
  return element.tagName === "INPUT" || element.tagName === "TEXTAREA" || element.tagName === "SELECT" || element.isContentEditable;
};

/** Global shortcuts: Cmd/Ctrl+K and `/` open the node search, `?` the help; none of them fire while typing in a field. */
function useShortcuts(controller: XRayController) {
  const { openDialog } = controller;
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && !event.altKey && event.key.toLowerCase() === "k") {
        event.preventDefault();
        openDialog("search");
        return;
      }
      if (event.metaKey || event.ctrlKey || event.altKey || isTyping(event.target)) return;
      if (event.key === "/") {
        event.preventDefault();
        openDialog("search");
      } else if (event.key === "?") {
        event.preventDefault();
        openDialog("help");
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [openDialog]);
}

export default function App() {
  const controller = useXRay();
  const { state, busyAny, dismissError } = controller;
  const { lang } = state;
  const { widths, resize, reset } = usePaneWidths();
  useShortcuts(controller);

  if (state.busy.boot && !state.overview) {
    return (
      <div className="boot">
        <div className="brand-mark large" aria-hidden>
          X
        </div>
        {state.error ? <p className="status-error">{translate(lang, "app.apiUnreachable", { error: state.error })}</p> : <p>{translate(lang, "app.booting")}</p>}
        {!state.error && <div className="boot-bar" aria-hidden />}
      </div>
    );
  }

  return (
    <I18nProvider lang={lang}>
      <div className="app">
        <Header controller={controller} />
        {state.error && (
          <div className="alert-banner status-error" role="alert">
            <WarningCircle size={16} weight="bold" aria-hidden />
            <span className="alert-text">{state.error}</span>
            <button type="button" className="btn ghost small" onClick={dismissError}>
              {translate(lang, "status.dismiss")}
            </button>
          </div>
        )}
        <main className="workspace" style={{ "--left-w": `${widths.left}px`, "--right-w": `${widths.right}px` } as CSSProperties}>
          {busyAny && <div className="busy-bar" aria-hidden />}
          <ResponseTree controller={controller} />
          <PaneResizer side="left" width={widths.left} label={translate(lang, "pane.resize")} onResize={(w) => resize("left", w)} onReset={() => reset("left")} />
          <EvidenceGraph controller={controller} />
          <PaneResizer side="right" width={widths.right} label={translate(lang, "pane.resize")} onResize={(w) => resize("right", w)} onReset={() => reset("right")} />
          <Inspector controller={controller} />
        </main>
        <StatusBar controller={controller} />
        {state.dialog === "search" && <SearchPalette controller={controller} />}
        {state.dialog === "help" && <HelpDialog controller={controller} />}
      </div>
    </I18nProvider>
  );
}
