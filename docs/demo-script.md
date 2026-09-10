# 1024 Demo script — Pick Order Details

Design §14. Everything below runs offline from `fixtures/pick-order-details`; nothing touches production.

## Before the demo

```bash
scripts/xray.sh start --open      # builds, starts API :5080 + UI :5173, waits for health, opens Scene 3
scripts/xray.sh status            # api/web "healthy" before you begin
```

(Manual alternative: `dotnet run --project src/MesXray.Api` and `cd src/MesXray.Web && npm run dev`.)

Check `GET http://localhost:5080/api/xray/health` → `status: ok`, 183 nodes, 307 edges, linker `unmappedColumns: []`.
Pick order for the demo: **PICK0843858**, material **T12288** (Bracket, left, zinc plated), runtime trace `trace-demo-001`.

Each scene has a deep link so you can recover instantly if a click goes wrong.

## Scene 1 — "It's just a 0" (Architecture)

Open <http://localhost:5173/>. The Architecture view shows the whole chain: Web VP page → `GET /cwp/v1/picking/pickOrder` → `PickingController` → `PickOrderService` → `PickOrderQuery` → 4 stored procedures → tables, UDFs, system parameters.

Talking point: the page shows `availableQuantity = 0`. How many layers stand behind that zero?

## Scene 2 — Trace Source

Click **`availableQuantity`** in the Response Tree (bold = key field). Inspector → *Trace Source*:

- Execution path: page → API → `GetPickOrder` → `GetPickOrderDetails` → `GetPickOrderRows` → `PickOrderQuery.GetPickOrderRows` → `AP_Pick_GetPickOrderRows` → `AF_Pick_GetAvailableQuantity`
- Upstream lineage: JSON field → property → result column → CTE column `TotalAvailableQuantity` = `SUM(MR.AvailableQuantity)` → expression
  `CASE @WMS_Enabled WHEN 0 THEN ISNULL(SUM(APPQD.Quantity) * 10, 1000) WHEN 1 THEN ISNULL(dbo.AF_Pick_GetAvailableQuantity(...), 0) END`
- Both branches are visible with their conditions `@WMS_Enabled = 0` / `@WMS_Enabled = 1`; the parameter is a `controlled by` hop.

Deep link: `/?field=availableQuantity`

## Scene 3 — Live Trace

Header: pick order `PICK0843858` → **Trace**, material `T12288`. The Response Tree now shows the real (sanitized) response; nodes on the graph carry value chips:

- `availableQuantity = 0`, `pickQuantity = 18`, `onHandQuantity = 0`, `allocatedQuantity = 18`, `WMS_Enabled = "1"`.

Talking point: Pick = 18, OnHand = 0, Allocated = 18, but Available comes from a *different* source (the UDF branch), which is why it is 0 while the others are not.

Deep link: `/?order=PICK0843858&field=availableQuantity&scope=T12288`

## Scene 4 — The UDF and "Need More Evidence"

Inspector → **Investigate**. The rule-based investigator (or the LLM, validated the same way) returns:

- Verdict **Need More Evidence**, confidence 75 %
- Known facts, each with evidence chips (`ev-067`, edge ids, node ids) — click a chip to jump to the node
- Fact: *Active branch: `@WMS_Enabled = 1` …* (derived from the live parameter value)
- Hypothesis (unverified): *AvailableQuantity is determined inside `AF_Pick_GetAvailableQuantity`, whose definition is not available* — with a read-only suggested check
- Hypothesis (unverified): the observed 0 may be the `ISNULL(..., 0)` fallback
- Unknown: `AF_Pick_GetAvailableQuantity` — dashed node on the graph

Talking point: the AI does not guess. Without the UDF definition it says so, and every sentence it does assert is bound to an evidence id.

Deep link: `/?order=PICK0843858&field=availableQuantity&scope=T12288&investigate=1`

## Scene 5 — Impact of WMS_Enabled

Click the `WMS_Enabled` node (or chip) → **Impact**. Key paths show
`WMS_Enabled → AP_Pick_GetPickOrderRows → … → GET /cwp/v1/picking/pickOrder → Web VP Pick Order Details` and
`WMS_Enabled → CASE expression → CTE columns → result column → CWPPickOrderRow.AvailableQuantity → json availableQuantity`.

Talking point: a configuration flag reaches a number on the page; this is what you check *before* flipping it.

Deep link: `/?impact=param:WMS_Enabled`

## Scene 6 — storageBin (FIFO / STRING_AGG)

Response Tree → `pickStorageBin.storageBin`. Lineage: `STRING_AGG(...)` over `#LocationList.StorageBin` ← CASE on `WL_Description_Replacement` (`ELSE` → `WAREHOUSE_LOCATION.Location`) with the FIFO ordering; live value `"A-01-02,A-01-05"`.

Optional contrast: `destinationWagon.storageBin.location` ends explicitly at the **Pending** `GetStorageBin` method and the runtime-computed SP — Unknown by design, not a dead end.

Deep link: `/?order=PICK0843858&field=json:pickOrderRows.pickStorageBin.storageBin&scope=T12288`

## Scene 7 — Back to the whole graph

Header → **Architecture**. Closing point: X-Ray is not a chat UI; it is an explainability engine — scanners build the evidence graph, runtime traces bind values to it, and AI is only allowed to speak from that evidence. The status bar shows the policy: read-only, whitelisted tools, no SQL execution, no parameter or data changes.

## Recovery

- API not reachable → UI shows "Cannot reach the X-Ray API"; run `scripts/xray.sh restart --api-only --no-build` (logs: `scripts/xray.sh logs --api-only`).
- Wrong pick order → 404 "No runtime data" in the status bar; use `PICK0843858`.
- Anything else → reload the scene's deep link.
