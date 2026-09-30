import { expect, test } from "@playwright/test";
import { relationLabel } from "../src/lib/i18n";
import { LANGS, SCENES, evidenceChipIds, hop, hopItem, labels, openScene, verdictMetrics } from "./scenes";

// The seven scenes of docs/demo-script.md, driven through their deep links exactly as the talk track does. Numbers
// are compared with what the API returns for the same request rather than hard-coded, so the fixture may grow; the
// facts the talk track quotes (branches, hypotheses, values) are asserted literally.
for (const lang of LANGS) {
  const t = labels(lang);
  const modeSwitch = ".header nav.mode-switch:not(.lang-switch) button.active";

  test.describe(`demo scenes [${lang}]`, () => {
    test("1 architecture: the whole chain behind the 0, with the build metrics of the API", async ({ page, request }) => {
      await openScene(page, SCENES.architecture, lang);

      await expect(page.locator(".brand-title")).toHaveText("MES X-Ray");
      await expect(page.locator(".brand-sub")).toContainText("Pick Order Details");
      await expect(page.locator(modeSwitch)).toHaveText(t("header.architecture"));
      await expect(page.locator(".pane-center .pane-sub")).toHaveText(t("graph.architecture"));

      // The status bar mirrors the API build report and the case's known gaps.
      const health = await (await request.get("/api/xray/health")).json();
      const overview = await (await request.get("/api/xray/cases/pick-order-details")).json();
      await expect(page.locator(".status-metrics .metric b")).toHaveText([
        String(health.graph.nodes),
        String(health.graph.edges),
        String(health.graph.lineages),
        String(health.graph.unknownNodes),
        String(health.graph.pendingNodes),
        String(overview.knownGaps.length),
      ]);
      await expect(page.locator(".statusbar")).toContainText(t("status.readonly"));

      // Static field list with the key fields in accent; the graph is anchored on the page node.
      await expect(page.locator(".pane-left .pane-sub")).toHaveText(t("tree.static"));
      await expect(page.locator(".tree-name.key-field", { hasText: /^availableQuantity$/ })).toBeVisible();
      await expect(page.locator(".xray-node.focus")).toContainText("Pick Order Details");
      expect(await page.locator(".xray-node").count()).toBeGreaterThan(40);
      await expect(page.locator(".pane-right .empty")).toHaveText(t("empty.details"));
    });

    test("2 trace source: availableQuantity through both WMS branches down to INVENTORY2", async ({ page }) => {
      await openScene(page, SCENES.traceSource, lang);

      await expect(page.locator(".tabs button.active")).toContainText(t("tab.trace"));
      await expect(page.locator(".trace-view .view-title code")).toHaveText("json:pickOrderRows.availableQuantity");

      const path = page.locator(".execution-path .path-node");
      await expect(path).toHaveCount(8);
      await expect(path.first()).toHaveText("Web VP - Pick Order Details");
      await expect(path.last()).toHaveText("AF_Pick_GetAvailableQuantity");

      const conditions = await page.locator(".trace-view .hop-condition").allTextContents();
      expect(conditions).toEqual(expect.arrayContaining(["@WMS_Enabled = 0", "@WMS_Enabled = 1", "ELSE"]));
      await expect(page.locator(".hop-expression", { hasText: "CASE @WMS_Enabled" })).toBeVisible();
      await expect(page.locator(".hop-expression", { hasText: "RETURN 1000 ELSE RETURN ISNULL(SUM(QuantityOnHand), 0)" })).toBeVisible();
      await expect(hop(page, "param:WMS_Enabled")).toBeVisible();
      await expect(hop(page, "udf:dbo.AF_Pick_GetAvailableQuantity")).toBeVisible();
      await expect(hop(page, "column:dbo.INVENTORY2.QuantityOnHand")).toBeVisible();
      await expect(hop(page, "column:dbo.DET2_ILG_ProductDeliveryMethod.DeliveryMethod").first()).toBeVisible();

      // Every hop is Known: the unknowns section says so and the tab shows a check mark instead of a count.
      await expect(page.locator(".trace-view .ok")).toHaveText(t("trace.allKnown"));
      await expect(page.locator(".tabs .count.warn")).toHaveCount(0);
      await expect(page.locator(".trace-view .status-chip.unknown")).toHaveCount(0);

      // The graph switches to the lineage of the field; the function sits on the lit path, nothing is a gap.
      await expect(page.locator(modeSwitch)).toHaveText(t("header.liveTrace"));
      await expect(page.locator(".pane-center .pane-sub")).toContainText("json:pickOrderRows.availableQuantity");
      await expect(page.locator(".xray-node.focus")).toContainText("availableQuantity");
      await expect(page.locator(".xray-node.highlighted .xray-node-name", { hasText: /^AF_Pick_GetAvailableQuantity$/ })).toHaveCount(1);
      await expect(page.locator(".xray-node.highlighted .xray-node-gap")).toHaveCount(0);

      // Function internals start folded so the path stays readable; the inspector still lists every hop.
      const foldChip = page.locator(".xray-node-fold");
      await expect(foldChip).toHaveClass(/is-folded/);
      await expect(foldChip).toHaveText(lang === "zh" ? /内部 \+\d+/ : /\+\d+ inside/);
      await expect(page.locator(".fold-toggle")).toContainText(lang === "zh" ? "展开函数内部" : "Unfold function internals");
      await expect(page.locator(".xray-node .xray-node-name", { hasText: /^QuantityOnHand$/ })).toHaveCount(0);

      await foldChip.click();
      await expect(foldChip).not.toHaveClass(/is-folded/);
      await expect(foldChip).toHaveText(t("graph.unfolded"));
      await expect(page.locator(".xray-node .xray-node-name", { hasText: /^QuantityOnHand$/ }).first()).toBeVisible();
      await expect(page.locator('.react-flow__node[data-id="expr:dbo.AF_Pick_GetAvailableQuantity.$.RETURN"]')).toBeVisible();
      await expect(page.locator(".fold-toggle")).toHaveText(t("graph.foldAll"));

      await page.locator(".fold-toggle").click();
      await expect(foldChip).toHaveClass(/is-folded/);
      await expect(page.locator(".xray-node .xray-node-name", { hasText: /^QuantityOnHand$/ })).toHaveCount(0);

      // Picking a hop in the inspector selects it on the canvas; a hop hidden inside the folded function opens it.
      const selected = page.locator(".react-flow__node.selected");
      await expect(selected).toHaveAttribute("data-id", "json:pickOrderRows.availableQuantity");
      await hop(page, "ctecol:dbo.AF_Pick_GetAvailableQuantity.InventoryData.QuantityOnHand").click();
      await expect(foldChip).not.toHaveClass(/is-folded/);
      await expect(selected).toHaveAttribute("data-id", "ctecol:dbo.AF_Pick_GetAvailableQuantity.InventoryData.QuantityOnHand");
      await expect(page.locator(".pane-right .pane-sub")).toHaveText("ctecol:dbo.AF_Pick_GetAvailableQuantity.InventoryData.QuantityOnHand");
    });

    test("3 live trace: PICK0843858 / T12288 values overlaid on the same lineage", async ({ page }) => {
      await openScene(page, SCENES.liveTrace, lang);

      await expect(page.locator(".live-badge")).toHaveText(/FIXTURE\s+trace-demo-001/);
      await expect(page.locator(".pick-order select")).toHaveValue("T12288");
      await expect(page.locator(modeSwitch)).toHaveText(t("header.liveTrace"));
      await expect(page.locator(".pane-left .pane-sub")).toHaveText("PickOrder PICK0843858");

      // The response tree shows the sanitized response of the order; the traced row is selected.
      const selected = page.locator(".tree-row.selected");
      await expect(selected).toHaveCount(1);
      await expect(selected).toContainText("availableQuantity");
      await expect(selected.locator(".tree-value")).toHaveText("0");
      const pickQuantity = page.locator(".tree-row", { has: page.locator(".tree-name", { hasText: /^pickQuantity$/ }) }).first();
      await expect(pickQuantity.locator(".tree-value")).toHaveText("18");

      // The trace view carries the live values: the field is 0, WMS_Enabled is "1".
      await expect(page.locator(".trace-view .view-title .status-chip")).toContainText("trace-demo-001 @ T12288");
      await expect(page.locator(".trace-view .hops > .hop > .hop-values .value-chip").first()).toContainText("= 0");
      await expect(hopItem(page, "param:WMS_Enabled").first().locator(":scope > .hop-values .value-chip")).toHaveText('WMS_Enabled = "1"');
      await expect(hopItem(page, "spcol:dbo.AP_Pick_GetPickOrderRows.AvailableQuantity").locator(":scope > .hop-values .value-chip")).toHaveText("T12288.AvailableQuantity = 0");

      // Graph chips carry the row scope.
      await expect(page.locator(".xray-node.focus .value-chip")).toContainText("T12288: 0");
    });

    test("3r replay: the call goes down the execution path, the value comes back up into the field", async ({ page }) => {
      // `replay=hold` arms the replay: the traced path is dark, values are hidden, nothing has moved yet.
      await openScene(page, SCENES.liveTraceArmed, lang);
      const toggle = page.locator(".replay-toggle");
      const node = (id: string) => page.locator(`.react-flow__node[data-id="${id}"] .xray-node`);
      const field = node("json:pickOrderRows.availableQuantity");
      const pageNode = node("page:WebVP.PickOrderDetails");
      const procedure = node("sp:dbo.AP_Pick_GetPickOrderRows");

      await expect(toggle).toHaveText(t("graph.replay"));
      await expect(field).toHaveClass(/replay-pending/);
      await expect(field.locator(".value-chip")).toBeHidden();
      expect(await page.locator(".xray-node.replay-pending").count()).toBeGreaterThan(10);
      expect(await page.locator(".react-flow__edge.edge-replay-dark").count()).toBeGreaterThan(10);

      // Start: the page lights first, the procedure and the field are still dark; the pulse travels the edges.
      await toggle.click();
      await expect(toggle).toHaveText(t("graph.replayStop"));
      await expect(pageNode).toHaveClass(/replay-(current|visited)/);
      await expect(procedure).toHaveClass(/replay-pending/);
      await expect(field).toHaveClass(/replay-pending/);
      await expect(page.locator(".edge-pulse").first()).toBeAttached();

      // The value lands: the field rings with its live value, then the replay clears and the ordinary lit look is back.
      await expect(field).toHaveClass(/replay-arrived/);
      await expect(field.locator(".value-chip")).toHaveText("T12288: 0");
      await expect(page.locator(".xray-node[class*='replay-']")).toHaveCount(0);
      await expect(toggle).toHaveText(t("graph.replay"));
      await expect(page.locator(".xray-node.focus .value-chip")).toContainText("T12288: 0");

      // Stopping halfway drops every replay state at once.
      await toggle.click();
      await expect(toggle).toHaveText(t("graph.replayStop"));
      await toggle.click();
      await expect(toggle).toHaveText(t("graph.replay"));
      await expect(page.locator(".xray-node[class*='replay-']")).toHaveCount(0);
      await expect(page.locator(".edge-pulse")).toHaveCount(0);

      // `replay=1` plays by itself once the trace is on screen.
      await openScene(page, SCENES.liveTraceReplay, lang);
      await expect(toggle).toHaveText(t("graph.replayStop"));
      await expect(field).toHaveClass(/replay-arrived/);
      await expect(toggle).toHaveText(t("graph.replay"));
    });

    test("4 investigate: inside the UDF, the 0 is explained by a hypothesis, so Need More Evidence", async ({ page, request }) => {
      await openScene(page, SCENES.investigate, lang);
      const view = page.locator(".explain-view");

      await expect(view.locator(".view-title h3")).toHaveText(t("explain.title"));
      await expect(view.locator(".view-title code")).toHaveText("json:pickOrderRows.availableQuantity");
      await expect(view.locator(".verdict")).toHaveClass(/verdict-partial/);
      await expect(view.locator(".verdict-label")).toHaveText(t("verdict.needMoreEvidence"));

      // The header numbers are the API's numbers for the same request (the talk track quotes 80 %, 21 facts, 1 unknown).
      const response = await request.post("/api/xray/ai/investigate", {
        data: { traceId: "trace-demo-001", focusNodeId: "json:pickOrderRows.availableQuantity", scope: "T12288", language: lang },
      });
      const api = (await response.json()).explanation;
      const metrics = await verdictMetrics(page);
      expect(metrics.confidence).toBe(`${Math.round(api.confidence * 100)}%`);
      expect(metrics.facts).toBe(api.knownFacts.length);
      expect(metrics.unknowns).toBe(api.unknowns.length);
      expect(metrics.facts).toBeGreaterThanOrEqual(15);
      expect(metrics.unknowns).toBe(1);
      await expect(view.locator(".summary")).toHaveText(api.summary);
      await expect(page.locator(".tabs button.active .count")).toHaveText(String(metrics.facts));

      await expect(view.locator(".steps li")).toHaveCount(6);
      const facts = view.locator(".facts li");
      await expect(facts.filter({ hasText: "WMS_Enabled = 1" }).first()).toBeVisible();
      await expect(facts.filter({ hasText: "RETURN 1000 ELSE RETURN ISNULL(SUM(QuantityOnHand), 0)" })).toHaveCount(1);
      await expect(facts.filter({ hasText: lang === "zh" ? "返回常量 1000" : "returns the constant 1000" })).toHaveCount(1);
      await expect(facts.filter({ hasText: "@UseQuantityAllocated = 0 -> 1 WHEN EXISTS" })).toHaveCount(1);

      const hypotheses = view.locator(".hypotheses li");
      await expect(hypotheses).toHaveCount(1);
      await expect(hypotheses.first()).toContainText("ISNULL(SUM(QuantityOnHand), 0)");
      await expect(hypotheses.first().locator(".status-chip")).toHaveText(t("hypothesis.unverified"));
      await expect(hypotheses.first()).toContainText("TEST");

      await expect(view.locator(".unknown-list li")).toHaveCount(1);
      await expect(view.locator(".unknown-list li").first()).toContainText("AF_Pick_GetAvailableQuantity");
      await expect(view.locator(".next-steps li").filter({ hasText: "dbo.DET2_ILG_ProductDeliveryMethod" })).toHaveCount(1);

      // Every fact is bound to evidence, the runtime evidence is cited, and the audit block names the provider.
      const chips = await evidenceChipIds(page);
      expect(chips.length).toBeGreaterThan(20);
      expect(chips.some((id) => /^ev-\d+/.test(id))).toBe(true);
      expect(await facts.evaluateAll((items) => items.every((li) => li.querySelectorAll(".evidence-chip").length > 0))).toBe(true);
      await expect(view.locator(".audit dd code").first()).toHaveText("rules");
    });

    test("5 impact: WMS_Enabled reaches the page and the availableQuantity field", async ({ page, request }) => {
      await openScene(page, SCENES.impact, lang);
      const view = page.locator(".impact-view");

      await expect(view.locator(".view-title code")).toHaveText("param:WMS_Enabled");
      const impact = await (await request.get("/api/xray/impact?id=param:WMS_Enabled")).json();
      expect(impact.affected.length).toBeGreaterThanOrEqual(15);
      await expect(view.locator(".verdict-label")).toHaveText(t("impact.affectedMany", { n: impact.affected.length }));
      await expect(page.locator(".tabs button.active .count")).toHaveText(String(impact.affected.length));

      const paths = view.locator(".key-paths li");
      await expect(paths).toHaveCount(impact.keyPaths.length);
      await expect(paths.filter({ hasText: "WebVP.PickOrderDetails" }).first()).toBeVisible();
      await expect(paths.filter({ hasText: "pickOrderRows.availableQuantity" }).first()).toBeVisible();
      // Affected nodes are grouped by distance and each one names the relation the change travels through.
      const groups = view.locator(".depth-group");
      await expect(groups.first().locator(".depth")).toHaveText(t("impact.depth", { n: 1 }));
      expect(await groups.count()).toBeGreaterThan(3);
      const firstHop = groups.first().locator("li").first();
      await expect(firstHop).toContainText("CASE @WMS_Enabled");
      await expect(firstHop.locator(".hop-relation")).toHaveText(relationLabel(lang, "controlledBy"));
      expect(await view.locator(".depth-group li").count()).toBe(impact.affected.length);
      await expect(view.locator(".summary")).toBeVisible();

      await expect(page.locator(".pane-center .pane-sub")).toHaveText(t("graph.impactOf", { name: "WMS_Enabled" }));
      await expect(page.locator(".xray-node.focus")).toContainText("WMS_Enabled");
    });

    test("6 storage bin: STRING_AGG over the FIFO list, with the live value", async ({ page }) => {
      await openScene(page, SCENES.storageBin, lang);

      await expect(page.locator(".trace-view .view-title code")).toHaveText("json:pickOrderRows.pickStorageBin.storageBin");
      await expect(page.locator(".hop-expression", { hasText: "STRING_AGG" }).first()).toBeVisible();
      await expect(hop(page, "tmpcol:dbo.AP_Pick_GetPickStorageBin.#LocationList.StorageBin").first()).toBeVisible();
      await expect(hop(page, "param:WL_Description_Replacement").first()).toBeVisible();
      await expect(hop(page, "column:dbo.WAREHOUSE_LOCATION.Location").first()).toBeVisible();
      await expect(page.locator(".trace-view .hops > .hop > .hop-values .value-chip").first()).toContainText('"A-01-02,A-01-05"');
      await expect(page.locator(".tree-row.selected .tree-value")).toHaveText('"A-01-02,A-01-05"');
      await expect(page.locator(".trace-view .ok")).toHaveText(t("trace.allKnown"));
    });

    test("6b destination wagon: stops at the Pending AP_Pick_GetPutStorageBin named by site configuration, no guess", async ({ page }) => {
      await openScene(page, SCENES.destinationWagon, lang);
      const view = page.locator(".explain-view");

      // The site configuration names the procedure (P1 input, 2026-09-17); its definition is the remaining gap.
      await expect(view.locator(".verdict-label")).toHaveText(t("verdict.needMoreEvidence"));
      await expect(view.locator(".unknown-list li").filter({ hasText: "AP_Pick_GetPutStorageBin" }).first()).toBeVisible();
      await expect(view.locator(".unknown-list li").filter({ hasText: "PickOrderService.GetStorageBin" })).toHaveCount(0);
      await expect(view.locator(".unknown-list li .status-chip").first()).toHaveText(t("chip.needMoreEvidence"));
      await expect(view.locator(".facts li").filter({ hasText: /StorageBin returns/ })).toHaveCount(0);
      await expect(view.locator(".next-steps li").filter({ hasText: "AP_Pick_GetPutStorageBin" }).first()).toBeVisible();

      // The trace tab counts the unknown hop and the graph marks the pending procedure as the gap; the method that
      // calls it is Known and the execution path runs through the configured Dapper call down to the procedure.
      await expect(page.locator(".tabs .count.warn")).toBeVisible();
      await expect(page.locator(".xray-node.status-pending").filter({ hasText: "AP_Pick_GetPutStorageBin" }).first()).toBeVisible();
      await expect(page.locator(".xray-node.status-pending .xray-node-gap").first()).toBeVisible();
      await expect(page.locator(".xray-node.status-pending").filter({ hasText: /^GetStorageBin/ })).toHaveCount(0);
      await page.locator(".tabs button", { hasText: t("tab.trace") }).click();
      await expect(page.locator(".execution-path .path-node").last()).toHaveText("AP_Pick_GetPutStorageBin");
      await expect(hop(page, "sp:dbo.AP_Pick_GetPutStorageBin").first()).toBeVisible();

      // The procedure's incoming edge cites the configuration entry as its evidence.
      await hop(page, "sp:dbo.AP_Pick_GetPutStorageBin").first().click();
      await page.locator(".tabs button", { hasText: t("tab.details") }).click();
      const executes = page.locator(".details-view .edges li").filter({ hasText: "method:PickOrderQuery.GetStorageBin" });
      await expect(executes).toContainText("(configuration)");
    });
  });
}

test("switching the language re-requests the explanation and keeps ids, values and verdict", async ({ page }) => {
  await openScene(page, SCENES.investigate, "en");
  const view = page.locator(".explain-view");
  await expect(view.locator(".verdict-label")).toHaveText(labels("en")("verdict.needMoreEvidence"));
  const before = { metrics: await verdictMetrics(page), chips: await evidenceChipIds(page) };
  expect(before.chips.length).toBeGreaterThan(0);

  await page.locator(".lang-switch button", { hasText: "中文" }).click();

  await expect(page.locator(".lang-switch button.active")).toHaveText("中文");
  await expect(page.locator("html")).toHaveAttribute("lang", "zh-CN");
  await expect(view.locator(".verdict-label")).toHaveText(labels("zh")("verdict.needMoreEvidence"));
  await expect(view.locator(".summary")).toContainText("需要更多证据");
  await expect(view.locator(".steps li").first()).toContainText("定位焦点节点");

  // Sentences changed language; the evidence ids, numbers and verdict did not. Identifiers and SQL stay as they are.
  expect(await verdictMetrics(page)).toEqual(before.metrics);
  expect(await evidenceChipIds(page)).toEqual(before.chips);
  await expect(view.locator(".facts li").filter({ hasText: "RETURN 1000 ELSE RETURN ISNULL(SUM(QuantityOnHand), 0)" })).toHaveCount(1);
  await expect(page.locator(".header nav.mode-switch:not(.lang-switch) button.active")).toHaveText(labels("zh")("header.liveTrace"));
  expect(new URL(page.url()).searchParams.get("lang")).toBe("zh");
});
