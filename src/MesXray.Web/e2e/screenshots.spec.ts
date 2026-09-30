import { test } from "@playwright/test";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { SCENES, openScene, settle, type Lang, type Scene } from "./scenes";

// Re-shoots the talk-track illustrations (docs/demo/*.png) from the scene definitions the tests use, so that the
// pictures never drift from the demo. Run on demand: `npm run demo:shots` (Chinese UI, plus the English scene 4).
const outDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../docs/demo");

const shots: { scene: Scene; lang: Lang; file: string; unfold?: boolean; ready?: string }[] = [
  { scene: SCENES.architecture, lang: "zh", file: "01-architecture.png" },
  { scene: SCENES.traceSource, lang: "zh", file: "02-trace-source.png" },
  { scene: SCENES.traceSource, lang: "zh", file: "02-trace-source-unfolded.png", unfold: true },
  { scene: SCENES.liveTrace, lang: "zh", file: "03-live-trace.png" },
  // The armed replay: the same frame with the path dark. First frame of the flow for the video kit; 03-live-trace.png is its last.
  { scene: SCENES.liveTraceArmed, lang: "zh", file: "03-live-trace-armed.png", ready: ".xray-node.replay-pending" },
  { scene: SCENES.investigate, lang: "zh", file: "04-investigate.png" },
  { scene: SCENES.investigate, lang: "en", file: "04-investigate-en.png" },
  { scene: SCENES.impact, lang: "zh", file: "05-impact.png" },
  { scene: SCENES.storageBin, lang: "zh", file: "06-storage-bin.png" },
  { scene: SCENES.destinationWagon, lang: "zh", file: "06b-destination-wagon.png" },
];

for (const { scene, lang, file, unfold, ready } of shots) {
  test(`${file} (${scene.id}, ${lang}${unfold ? ", unfolded" : ""})`, async ({ page }) => {
    await openScene(page, scene, lang);
    if (unfold) await page.locator(".fold-toggle").click();
    if (ready) await page.locator(ready).first().waitFor();
    await settle(page);
    await page.screenshot({ path: path.join(outDir, file), animations: "disabled" });
  });
}
