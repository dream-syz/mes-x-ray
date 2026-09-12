import { test } from "@playwright/test";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { SCENES, openScene, settle, type Lang, type Scene } from "./scenes";

// Re-shoots the talk-track illustrations (docs/demo/*.png) from the scene definitions the tests use, so that the
// pictures never drift from the demo. Run on demand: `npm run demo:shots` (Chinese UI, plus the English scene 4).
const outDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../docs/demo");

const shots: { scene: Scene; lang: Lang; file: string }[] = [
  { scene: SCENES.architecture, lang: "zh", file: "01-architecture.png" },
  { scene: SCENES.traceSource, lang: "zh", file: "02-trace-source.png" },
  { scene: SCENES.liveTrace, lang: "zh", file: "03-live-trace.png" },
  { scene: SCENES.investigate, lang: "zh", file: "04-investigate.png" },
  { scene: SCENES.investigate, lang: "en", file: "04-investigate-en.png" },
  { scene: SCENES.impact, lang: "zh", file: "05-impact.png" },
  { scene: SCENES.storageBin, lang: "zh", file: "06-storage-bin.png" },
  { scene: SCENES.destinationWagon, lang: "zh", file: "06b-destination-wagon.png" },
];

for (const { scene, lang, file } of shots) {
  test(`${file} (${scene.id}, ${lang})`, async ({ page }) => {
    await openScene(page, scene, lang);
    await settle(page);
    await page.screenshot({ path: path.join(outDir, file), animations: "disabled" });
  });
}
