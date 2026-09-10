import { EvidenceGraph } from "./components/graph/EvidenceGraph";
import { Header } from "./components/Header";
import { Inspector } from "./components/inspector/Inspector";
import { ResponseTree } from "./components/ResponseTree";
import { StatusBar } from "./components/StatusBar";
import { useXRay } from "./state/useXRay";

export default function App() {
  const controller = useXRay();

  if (controller.state.busy.boot && !controller.state.overview) {
    return (
      <div className="boot">
        <div className="brand-mark large">X</div>
        <p>{controller.state.error ? `Cannot reach the X-Ray API: ${controller.state.error}` : "Loading the evidence graph…"}</p>
      </div>
    );
  }

  return (
    <div className="app">
      <Header controller={controller} />
      <main className="workspace">
        <ResponseTree controller={controller} />
        <EvidenceGraph controller={controller} />
        <Inspector controller={controller} />
      </main>
      <StatusBar controller={controller} />
    </div>
  );
}
