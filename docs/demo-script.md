# 1024 Demo script — Pick Order Details

Design §14. Everything below runs offline from `fixtures/pick-order-details`; nothing touches production.

## Before the demo

```bash
(cd src/MesXray.Web && npm run e2e)   # the day before: every scene below, in EN and ZH, through the real API and UI
scripts/xray.sh demo              # builds, starts API :5080 + UI :5173, waits for health, prints every scene's
                                  # deep link on the port actually in use, opens Scene 1
scripts/xray.sh status            # api/web "healthy" before you begin
```

The e2e run starts its own API (:5090) and Vite (:5199) and drives the locally installed Chrome, so it neither needs nor disturbs the demo stack; it fails loudly if a verdict, a value or a path on any scene has changed.

If :5173 is busy (another Vite project, for example) the script moves the UI to the next free port and says so; use the printed URLs rather than the ones below. `XRAY_LANG=zh scripts/xray.sh demo` prints the links with `&lang=zh` so the UI starts in Chinese; the header has an EN / 中文 switch as well, and the AI text follows the switch.

(Manual alternative: `dotnet run --project src/MesXray.Api` and `cd src/MesXray.Web && npm run dev`.)

Check `GET http://localhost:5080/api/xray/health` → `status: ok`, 210 nodes, 363 edges, linker `unmappedColumns: []`.
Pick order for the demo: **PICK0843858**, material **T12288** (Bracket, left, zinc plated), runtime trace `trace-demo-001`.

Each scene has a deep link so you can recover instantly if a click goes wrong. The spoken version of this script, with a time budget and a screenshot per scene, is [`demo/talk-track.md`](demo/talk-track.md).

## Scene 1 — "It's just a 0" (Architecture)

Open <http://localhost:5173/>. The Architecture view shows the whole chain: Web VP page → `GET /cwp/v1/picking/pickOrder` → `PickingController` → `PickOrderService` → `PickOrderQuery` → 4 stored procedures → tables, UDFs, system parameters.

Talking point: the page shows `availableQuantity = 0`. How many layers stand behind that zero?

## Scene 2 — Trace Source

Click **`availableQuantity`** in the Response Tree (bold = key field). Inspector → *Trace Source*:

- Execution path: page → API → `GetPickOrder` → `GetPickOrderDetails` → `GetPickOrderRows` → `PickOrderQuery.GetPickOrderRows` → `AP_Pick_GetPickOrderRows` → `AF_Pick_GetAvailableQuantity`
- Upstream lineage: JSON field → property → result column → CTE column `TotalAvailableQuantity` = `SUM(MR.AvailableQuantity)` → expression
  `CASE @WMS_Enabled WHEN 0 THEN ISNULL(SUM(APPQD.Quantity) * 10, 1000) WHEN 1 THEN ISNULL(dbo.AF_Pick_GetAvailableQuantity(...), 0) END`
- Both branches are visible with their conditions `@WMS_Enabled = 0` / `@WMS_Enabled = 1`; the parameter is a `controlled by` hop.
- The `@WMS_Enabled = 1` branch does not stop at the function call: `AF_Pick_GetAvailableQuantity` is Known (its sanitized definition is in the fixture) and the trace continues through its RETURN expression
  `IF EXISTS (DET2_ILG_ProductDeliveryMethod WHERE DIP.DeliveryMethod = 'LVP' ...) RETURN 1000 ELSE RETURN ISNULL(SUM(QuantityOnHand), 0)`
  into the `InventoryData` CTE (`CASE WHEN @UseQuantityAllocated = 1 ... WHEN PG.Group_ = 'ECU' ... ELSE ISNULL(I.QuantityOnHand, 0) END`) down to `INVENTORY2.QuantityOnHand` / `QuantityAllocated`, `PRODUCT_GROUP.Group_` and the `DET2_ILG_ProductDeliveryMethod` columns that decide the branches. `trace.unknowns` is empty.

Deep link: `/?field=availableQuantity`

## Scene 3 — Live Trace

Header: pick order `PICK0843858` → **Trace**, material `T12288`. The Response Tree now shows the real (sanitized) response; nodes on the graph carry value chips:

- `availableQuantity = 0`, `pickQuantity = 18`, `onHandQuantity = 0`, `allocatedQuantity = 18`, `WMS_Enabled = "1"`.

Talking point: Pick = 18, OnHand = 0, Allocated = 18, but Available comes from a *different* source (the UDF branch), which is why it is 0 while the others are not.

Deep link: `/?order=PICK0843858&field=availableQuantity&scope=T12288`

## Scene 4 — Inside the UDF: a hypothesis, not a guess

Inspector → **Investigate**. The rule-based investigator (or the LLM, validated the same way) returns:

- Verdict **Need More Evidence**, confidence 80 %, 21 facts, 63 cited evidence ids, 1 unknown
- Known facts, each with evidence chips (`ev-068`, edge ids, node ids, lineage ids) — click a chip to jump to the node
- Fact: *Active branch: `@WMS_Enabled = 1` …* (derived from the live parameter value)
- Facts inside the function: `AF_Pick_GetAvailableQuantity = IF EXISTS (... 'LVP' ...) RETURN 1000 ELSE RETURN ISNULL(SUM(QuantityOnHand), 0)`; *Branch `EXISTS (...)` returns the constant 1000*; *Which branch applies depends on row data: `DET2_ILG_ProductDeliveryMethod.DeliveryMethod`, … these rows were not captured in the trace*; the local variable `@UseQuantityAllocated = 0 -> 1 WHEN EXISTS (... IN ('LVS_MPA','SLFR_MPA','LVS_LINE','SLFR_LINE') ...)`
- Hypothesis (unverified): *`AF_Pick_GetAvailableQuantity` equals the ISNULL fallback 0 of `ISNULL(SUM(QuantityOnHand), 0)`; the inner value was probably NULL (no matching row …)* — with a read-only suggested check in TEST
- Unknown (runtime detail, not a static gap): the function was evaluated inside SQL Server; its return value per material and the INVENTORY2 rows were not captured
- Next step: query `DET2_ILG_ProductDeliveryMethod`, `PRODUCT`, `PRODUCT_GROUP` for this material in TEST (read-only) to determine the active branch

Talking point: the AI does not guess. The static lineage is complete down to the base tables (**Explain** on the same field is *Known*, 90 %); the *value* 0 is explained by a hypothesis, so **Investigate** says Need More Evidence and tells you which read-only check settles it. Every sentence it asserts is bound to an evidence id.

Contrast: material `T55102` shows `availableQuantity = 1000`; Investigate for `scope=T55102` yields the hypothesis *equals the constant 1000 returned when EXISTS (... DeliveryMethod = 'LVP' ...)*. `T40917` (860) matches neither a constant nor an ISNULL default: verdict Known.

Language: flip EN / 中文 here if you want to show it. The explanation is re-requested in the other language; the verdict, confidence, evidence ids and values do not change, only the sentences do.

Deep link: `/?order=PICK0843858&field=availableQuantity&scope=T12288&investigate=1`

## Scene 5 — Impact of WMS_Enabled

Click the `WMS_Enabled` node (or chip) → **Impact**. Key paths show
`WMS_Enabled → AP_Pick_GetPickOrderRows → … → GET /cwp/v1/picking/pickOrder → Web VP Pick Order Details` and
`WMS_Enabled → CASE expression → CTE columns → result column → CWPPickOrderRow.AvailableQuantity → json availableQuantity`.

Talking point: a configuration flag reaches a number on the page; this is what you check *before* flipping it.

Deep link: `/?impact=param:WMS_Enabled`

## Scene 6 — storageBin (FIFO / STRING_AGG)

Response Tree → `pickStorageBin.storageBin`. Lineage: `STRING_AGG(...)` over `#LocationList.StorageBin` ← CASE on `WL_Description_Replacement` (`ELSE` → `WAREHOUSE_LOCATION.Location`) with the FIFO ordering; live value `"A-01-02,A-01-05"`.

Optional contrast: `destinationWagon.storageBin.location` ends explicitly at the **Pending** `GetStorageBin` method and the runtime-computed SP — Unknown by design, not a dead end. (This is what `availableQuantity` looked like before the `AF_Pick_GetAvailableQuantity` definition was delivered.)

Deep links: `/?order=PICK0843858&field=json:pickOrderRows.pickStorageBin.storageBin&scope=T12288`; contrast `/?field=json:pickOrderRows.destinationWagon.storageBin.location&explain=1`

## Scene 7 — Back to the whole graph

Header → **Architecture**. Closing point: X-Ray is not a chat UI; it is an explainability engine — scanners build the evidence graph, runtime traces bind values to it, and AI is only allowed to speak from that evidence. The status bar shows the policy: read-only, whitelisted tools, no SQL execution, no parameter or data changes.

## Recovery

- API not reachable → UI shows "Cannot reach the X-Ray API"; run `scripts/xray.sh restart --api-only --no-build` (logs: `scripts/xray.sh logs --api-only`).
- Wrong pick order → 404 "No runtime data" in the status bar; use `PICK0843858`.
- UI came up on another port (5174, 5175 …) → that is the fallback; `scripts/xray.sh status` shows the URL in use. Never kill the other program during the demo.
- UI language wrong → click EN / 中文 in the header, or add `&lang=en` / `&lang=zh` to the deep link.
- Anything else → reload the scene's deep link.
