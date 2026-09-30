import { expect, type Page } from "@playwright/test";
import { translate, type Lang, type MessageKey, type Vars } from "../src/lib/i18n";

export type { Lang };

/** The seven scenes of docs/demo-script.md; `shot` is the file name under docs/demo (language suffix added for `en`). */
export interface Scene {
  id: string;
  query: string;
  shot: string;
}

export const SCENES: Record<"architecture" | "traceSource" | "liveTrace" | "liveTraceReplay" | "liveTraceArmed" | "investigate" | "impact" | "storageBin" | "destinationWagon", Scene> = {
  architecture: { id: "1 architecture", query: "", shot: "01-architecture" },
  traceSource: { id: "2 trace source", query: "field=availableQuantity", shot: "02-trace-source" },
  liveTrace: { id: "3 live trace", query: "order=PICK0843858&field=availableQuantity&scope=T12288", shot: "03-live-trace" },
  // Replay of the flow (video kit): `replay=1` plays as soon as the trace is on screen, `replay=hold` darkens the path and waits.
  liveTraceReplay: { id: "3r live trace replay", query: "order=PICK0843858&field=availableQuantity&scope=T12288&replay=1", shot: "03-live-trace-replay" },
  liveTraceArmed: { id: "3a live trace armed", query: "order=PICK0843858&field=availableQuantity&scope=T12288&replay=hold", shot: "03-live-trace-armed" },
  investigate: { id: "4 investigate", query: "order=PICK0843858&field=availableQuantity&scope=T12288&investigate=1", shot: "04-investigate" },
  impact: { id: "5 impact", query: "impact=param:WMS_Enabled", shot: "05-impact" },
  storageBin: { id: "6 storage bin", query: "order=PICK0843858&field=json:pickOrderRows.pickStorageBin.storageBin&scope=T12288", shot: "06-storage-bin" },
  destinationWagon: { id: "6b destination wagon", query: "field=json:pickOrderRows.destinationWagon.storageBin.location&explain=1", shot: "06b-destination-wagon" },
};

export const LANGS: Lang[] = ["en", "zh"];

export const sceneUrl = (scene: Scene, lang: Lang): string => `/?${scene.query ? `${scene.query}&` : ""}lang=${lang}`;

/** The UI dictionary, so that assertions follow the language under test instead of duplicating the strings. */
export const labels = (lang: Lang) => (key: MessageKey, vars?: Vars) => translate(lang, key, vars);

/**
 * Opens a scene deep link and waits until the app has booted (status bar shows the build metrics) and the deep-linked
 * scenario has finished loading (no pending API calls, no busy button labels).
 */
export async function openScene(page: Page, scene: Scene, lang: Lang): Promise<void> {
  await page.goto(sceneUrl(scene, lang));
  await expect(page.locator(".status-metrics")).toBeVisible();
  await page.waitForLoadState("networkidle");
  await expect(page.locator(".status-error")).toHaveCount(0);
}

/** Waits for the X-ray sweep and the camera glide to finish so that a screenshot shows the settled frame. */
export async function settle(page: Page): Promise<void> {
  await expect(page.locator(".scan-sweep")).toHaveCount(0, { timeout: 10_000 });
  await page.waitForTimeout(900);
}

/** The trace hop button for a graph node id (the button's title is the node id). */
export const hop = (page: Page, nodeId: string) => page.locator(`.trace-view button.link[title="${nodeId}"]`);

/** The `li.hop` element of that node: its own row, expression and live values (children hops are nested deeper). */
export const hopItem = (page: Page, nodeId: string) => hop(page, nodeId).locator("xpath=ancestor::li[contains(@class, 'hop')][1]");

/** Reads the numbers shown in the verdict header of the Explain view: confidence, evidence items, facts, unknowns. */
export async function verdictMetrics(page: Page): Promise<{ confidence: string; evidence: number; facts: number; unknowns: number }> {
  const values = await page.locator(".explain-view .verdict-meta .metric b").allTextContents();
  return { confidence: values[0] ?? "", evidence: Number(values[1]), facts: Number(values[2]), unknowns: Number(values[3]) };
}

export const evidenceChipIds = (page: Page) => page.locator(".explain-view .facts .evidence-chip").allTextContents();
