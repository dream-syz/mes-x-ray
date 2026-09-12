import { EvidenceGraph } from "./components/graph/EvidenceGraph";
import { Header } from "./components/Header";
import { Inspector } from "./components/inspector/Inspector";
import { ResponseTree } from "./components/ResponseTree";
import { StatusBar } from "./components/StatusBar";
import { I18nProvider } from "./lib/I18nContext";
import { translate } from "./lib/i18n";
import { useXRay } from "./state/useXRay";

export default function App() {
  const controller = useXRay();
  const { lang } = controller.state;

  if (controller.state.busy.boot && !controller.state.overview) {
    return (
      <div className="boot">
        <div className="brand-mark large" aria-hidden>
          X
        </div>
        {controller.state.error ? <p className="status-error">{translate(lang, "app.apiUnreachable", { error: controller.state.error })}</p> : <p>{translate(lang, "app.booting")}</p>}
        {!controller.state.error && <div className="boot-bar" aria-hidden />}
      </div>
    );
  }

  return (
    <I18nProvider lang={lang}>
      <div className="app">
        <Header controller={controller} />
        <main className="workspace">
          <ResponseTree controller={controller} />
          <EvidenceGraph controller={controller} />
          <Inspector controller={controller} />
        </main>
        <StatusBar controller={controller} />
      </div>
    </I18nProvider>
  );
}
