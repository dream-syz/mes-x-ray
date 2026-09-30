# MES X-Ray

System explainability and data-lineage platform for MES. X-Ray answers "where does this number on the page come from, and what happens if I change that?" with **evidence**, not guesses: it scans .NET (Roslyn) and T-SQL (ScriptDom) into an Evidence Graph, overlays sanitized runtime traces, and lets an AI investigator explain fields **only** from evidence ids it can cite. Anything it cannot prove is reported as *Unknown / Need More Evidence*.

First case (and the 1024 demo): **Web Visual Picking – Pick Order Details** — `GET /cwp/v1/picking/pickOrder` → EBBA API → Dapper stored procedures → T-SQL expressions, tables, UDFs and system parameters.

Design: [`docs/design/MES_XRay_POC_Detailed_Design_CN.md`](docs/design/MES_XRay_POC_Detailed_Design_CN.md). Decisions: [`docs/adr`](docs/adr). Demo: [`docs/demo-script.md`](docs/demo-script.md). Security: [`docs/security.md`](docs/security.md).

## Contents

- [Quick start](#quick-start)
- [What you get](#what-you-get)
- [Solution layout](#solution-layout)
- [Fixtures: import a case](#fixtures-import-a-case)
- [Scanning and the graph build](#scanning-and-the-graph-build)
- [Running the demo](#running-the-demo)
- [API](#api)
- [AI provider](#ai-provider)
- [Tests and CI](#tests-and-ci)
- [Configuration](#configuration)

## Quick start

Prerequisites: .NET SDK 10.0 (see `global.json`), Node.js 20+.

One command (macOS / Linux / WSL / Git Bash) builds the API, starts it on :5080, installs the web packages on first run and starts the Vite dev server on :5173 — both in the background, with health checks:

```bash
scripts/xray.sh demo           # build + start, print every demo scene's deep link, open Scene 1 in the browser
scripts/xray.sh start          # same without the browser; --open launches the hero scene (full pipeline)
scripts/xray.sh status         # pids, ports, API health
scripts/xray.sh logs           # follow artifacts/run/logs/{api,web}.log
scripts/xray.sh restart        # stop + rebuild + start   (--api-only / --web-only to limit)
scripts/xray.sh stop           # --force also frees the ports from processes it did not start
scripts/xray.sh rehearse       # Playwright: every demo scene in EN and ZH against a private API/Vite pair (see Tests and CI)
```

If :5173 (or :5080) is already taken by another program, the script moves to the next free port and prints the URLs it actually uses (set `XRAY_API_PORT` / `XRAY_WEB_PORT` to insist on a port). Options: `--release` (Release build), `--no-build` (reuse last build), `XRAY_LANG=zh|en` (UI language of the printed deep links); a repo-root `.env` (copy of `.env.example`) is loaded automatically. Pid files, chosen ports and logs live in `artifacts/run/`.

Or run the two processes by hand:

```bash
# 1. API (scans the fixture, builds the graph, preloads the runtime fixture) — http://localhost:5080
dotnet run --project src/MesXray.Api

# 2. Web UI (Vite dev server with /api proxied to :5080) — http://localhost:5173
cd src/MesXray.Web && npm install && npm run dev
```

Everything runs offline from `fixtures/pick-order-details`; no database, no network, no credentials are needed. Open <http://localhost:5173/?order=PICK0843858&field=availableQuantity&scope=T12288&investigate=1> for the headline demo scene in one click (replace 5173 by the port the script printed if it had to move).

To serve the UI from the API instead of Vite: `npm run build` in `src/MesXray.Web` (output goes to `artifacts/web/`), copy `artifacts/web/*` to `src/MesXray.Api/wwwroot/`, and browse to <http://localhost:5080>.

## What you get

| Capability | Where | Design ref |
|---|---|---|
| Evidence Graph model: `Node`, `Edge`, `FieldLineage`, `RuntimeEvidence`, stable node ids, relation direction semantics | `src/MesXray.Domain` | §5, ADR-0001 |
| In-memory graph store with JSON snapshots, FillGaps/Authoritative merge, Dapper by-name linker, Trace Source, Impact Analysis, architecture view | `src/MesXray.Graph` | §3, §6, ADR-0003 |
| .NET scanner (Roslyn): endpoints, call chains, Dapper SP calls, models/properties, enrichment methods, branches, JSON serialization | `src/MesXray.Scanner.DotNet` | §4.1 |
| SQL scanner (ScriptDom): procedures/functions, tables, aliases, CTE/temp tables, CASE branch conditions, aggregates, UDF calls, system-parameter reads (scalar and table-valued readers), scalar-function `RETURN` lineage with `IF` guards, local-variable tracking | `src/MesXray.Scanner.Sql` | §4.2 |
| Runtime adapter (fixture-backed), evidence binder, **read-only tool gateway** with whitelist, argument validation, audit log and redaction | `src/MesXray.Runtime` | §4.3, §10 |
| AI investigator: evidence bundle, rule-based investigator (offline default), OpenAI-compatible structured-output client, **evidence-binding validator** | `src/MesXray.AI` | §7.1, §9 |
| HTTP API (minimal APIs, OpenAPI, ProblemDetails) | `src/MesXray.Api` | §7 |
| UI: Response Tree · Evidence Graph (React Flow) · Inspector with Trace Source / Impact / Explain, Architecture ↔ Live Trace | `src/MesXray.Web` | §8 |

## Solution layout

```
mes-x-ray/
├─ MesXray.slnx                       solution (projects + docs + build files as solution folders)
├─ Directory.Build.props              shared build settings; all output goes to /artifacts
├─ Directory.Packages.props           central package versions
├─ global.json · .editorconfig · .env.example · .gitignore
├─ .github/workflows/ci.yml           CI: dotnet build/test, web typecheck/build, Playwright demo scenes, secret scan
├─ scripts/xray.sh                    one-click start / stop / restart / status / logs
├─ src/
│  ├─ MesXray.Domain/          graph model, node-id conventions, ports (IRuntimeAdapter, IGraphRepository…)
│  ├─ MesXray.Graph/           store, snapshot IO, mergers, linker, queries (trace / impact / details)
│  ├─ MesXray.Scanner.DotNet/  Roslyn scanner
│  ├─ MesXray.Scanner.Sql/     ScriptDom scanner
│  ├─ MesXray.Runtime/         fixture adapter, evidence binder, tool gateway, redactor
│  ├─ MesXray.AI/              evidence bundle, investigators, validator, prompts (embedded)
│  ├─ MesXray.Api/             ASP.NET Core host, endpoints, bootstrapper
│  └─ MesXray.Web/             React + TypeScript + Vite UI; e2e/ = Playwright demo scenes + screenshot mode
├─ tests/
│  ├─ Directory.Build.props           test-project defaults (xunit, relaxed analyzers)
│  ├─ MesXray.Scanner.DotNet.Tests/   scanner unit tests against the fixture source
│  ├─ MesXray.Scanner.Sql.Tests/      scanner unit tests incl. 100 % ground-truth SQL-edge reproduction
│  ├─ MesXray.Graph.Tests/            graph assembly, trace, impact, runtime gateway, AI investigator
│  ├─ MesXray.Integration.Tests/      WebApplicationFactory tests for AC-01 … AC-08
│  └─ MesXray.TestSupport/            fixture paths, ground-truth helpers
├─ fixtures/pick-order-details/       sanitized case (see below)
├─ docs/
│  ├─ design/                         the design document (source of requirements)
│  ├─ adr/                            architecture decision records
│  ├─ demo/                           talk track (5 min) and a screenshot of every scene
│  ├─ demo-script.md · security.md
└─ artifacts/                         generated, git-ignored: bin/ obj/ (.NET), web/ (Vite), test-results/, run/ (pids, ports, logs)
```

Build output never lands next to the source: .NET uses `UseArtifactsOutput` (`artifacts/bin/<project>/<config>`, `artifacts/obj/<project>`), Vite writes to `artifacts/web`, CI test results go to `artifacts/test-results`. Delete `artifacts/` to reset a build.

## Fixtures: import a case

A case is a folder under `fixtures/<case-id>/`:

```
fixtures/pick-order-details/
├─ case.json                     id, title, root page / API / response model, key fields, system parameters,
│                                default fixture/trace/scope, known gaps with priorities
├─ source/dotnet/**/*.cs         sanitized C# (controllers, services, Dapper queries, models)
├─ source/sql/**/*.sql           sanitized T-SQL (CREATE PROCEDURE / FUNCTION)
├─ source/config/site-settings.json  optional: site configuration values keyed by options property
│                                (PickingOptions.StorageBinProcedure = dbo.AP_Pick_GetPutStorageBin); the .NET
│                                scanner resolves runtime-configured procedure names from it, cited as `configuration` evidence
├─ expected-graph/
│  ├─ ground-truth.json          curated nodes/edges/lineage the scanners must reproduce (design §2, appendix A)
│  └─ manual-overrides.json      human knowledge: Pending/Unknown statuses, descriptions, web→API edge
└─ runtime/*.json                sanitized runtime captures: parameters, SP result rows, API response
```

To import a new case:

1. Copy the relevant source files into `source/dotnet` and `source/sql`. **Sanitize first**: no hosts, credentials, JWTs, real user names or personal data (see `docs/security.md`). Keep procedure/table/column names intact — they are the join keys of the graph.
2. Write `case.json` (copy the existing one). `keyFields` are the fields the UI highlights; `knownGaps` are the objects you already know are missing.
3. Optionally write `expected-graph/ground-truth.json` for the edges you have confirmed by reading the code. The scanners are measured against it (`GraphAssemblyTests`, `SqlScannerTests`).
4. Capture a runtime fixture in a **non-production** environment (`environment` must be one of `XRay:Runtime:AllowedEnvironments`), redact it, and save it under `runtime/`. `entityKey` is the value users type into the UI (the pick order number).
5. Point the API at it: `XRay:Fixtures:Root=fixtures/<case-id>` (appsettings or environment variable `XRay__Fixtures__Root`).

## Scanning and the graph build

The API builds the graph at startup (`GraphBootstrapper`), in this order:

1. `dotnet-scanner` — Roslyn over `source/dotnet`
2. `sql-scanner` — ScriptDom over `source/sql` (merged with **FillGaps**: scanners never overwrite curated text)
3. `linker` — Dapper by-name mapping of SP result columns to model properties
4. `ground-truth` and `manual-overrides` — merged **Authoritative** (curation wins)

`GET /api/xray/health` returns the build report (nodes/edges/lineage per step, unknown/pending counts, linker result). With the shipped fixture the scanners reproduce every ground-truth edge except the manually curated `page → API` call, and the linker maps all 18 result columns; the merged graph has 210 nodes, 363 edges and 113 lineage records.

### Known gaps of the shipped case

`case.json` lists what is deliberately missing, and the case overview reports each gap's live status:

| Gap | Priority | Status |
|---|---|---|
| `dbo.AF_Pick_GetAvailableQuantity` definition (design §16, P0) | delivered 2026-09-12 | **Closed**: the sanitized definition is in `source/sql`; `availableQuantity` traces through the function's `RETURN` expression, the `InventoryData` CTE and the `DET2_ILG_ProductDeliveryMethod` / `PRODUCT_GROUP` / `INVENTORY2` columns. Explain is *Known*; Investigate of the observed `0` yields a hypothesis (ISNULL fallback of `SUM(QuantityOnHand)`) and *Need More Evidence*. |
| `dbo.AP_Pick_GetPutStorageBin` definition (storage bins of a destination wagon) | P1 | **Half closed** 2026-09-17: the site configuration names the procedure (`source/config/site-settings.json`, `PickingOptions.StorageBinProcedure`), so `GetStorageBin` is Known and the Dapper call resolves with `configuration` evidence (0.9). The procedure body is still outstanding: `destinationWagon.storageBin.location` stops explicitly at the Pending procedure |
| `dbo.AP_Pick_GetMultiPickOrderRows` (multi pick order flow) | P2 | Unknown: not part of the demo, left as a documented gap |

The typed parameter readers `AF_GetSystemParameterValueint` / `AF_GetSystemParameterValueListString` are curated as Pending: every call is resolved by parameter name, so their bodies are not needed for lineage.

Modes (`XRay:Graph:Mode`): `ScanAndCurate` (default), `ScanOnly` (measure the scanners alone), `CuratedOnly` (no scanning). Set `XRay:Graph:ExportPath` to write the merged snapshot as JSON — commit it next to the fixture to version the expected graph.

## Running the demo

Follow [`docs/demo-script.md`](docs/demo-script.md) (design §14); [`docs/demo/talk-track.md`](docs/demo/talk-track.md) is the five-minute talk track with a screenshot of every scene; [`docs/demo/video-kit.md`](docs/demo/video-kit.md) is the recorded-video kit (shot list, VO, titles, checklist) for 1024. `scripts/xray.sh demo` prints these deep links on the port actually in use and opens Scene 1:

| Scene | URL |
|---|---|
| 1 Architecture: the whole chain behind the 0 | `/` |
| 2 Trace Source `availableQuantity` (static) | `/?field=availableQuantity` |
| 3 Live Trace PICK0843858 / T12288 | `/?order=PICK0843858&field=availableQuantity&scope=T12288` |
| 3r Replay the flow of that trace (video kit) | `/?order=PICK0843858&field=availableQuantity&scope=T12288&replay=1` |
| 4 Investigate inside the UDF: hypothesis for the 0, Need More Evidence | `/?order=PICK0843858&field=availableQuantity&scope=T12288&investigate=1` |
| 5 `WMS_Enabled` impact back to the page | `/?impact=param:WMS_Enabled` |
| 6 FIFO / `STRING_AGG` storage-bin lineage | `/?order=PICK0843858&field=json:pickOrderRows.pickStorageBin.storageBin&scope=T12288` |
| 6b Pending by design: destination wagon storage bin | `/?field=json:pickOrderRows.destinationWagon.storageBin.location&explain=1` |

Add `&lang=zh` (or `lang=en`) to any link to fix the UI language; the header also has an EN / 中文 switch. The choice is remembered in the browser, and the AI explanation is re-requested in the new language while its evidence ids, values and verdict stay the same.

### How the UI reads

The UI is styled as an X-ray film: one dark theme, one accent. Everything the graph knows sits in grey; the accent is reserved for what the X-ray reveals, i.e. the traced path, the selected node and live evidence. Amber marks Unknown / Pending / Need More Evidence and red marks refuted hypotheses; nothing else is coloured. Layers (Web, API, Service, Data, Config) are labelled on each node rather than colour-coded.

- The Evidence Graph is ranked in data-flow direction and picks the orientation that fills the canvas best: long lineage chains run top-to-bottom (surface on top, SQL at the bottom, as in the design §8 wireframe), the broad architecture map runs left-to-right.
- When a trace or impact result arrives, a single sweep crosses the canvas, the camera frames the lit path (not the whole subgraph) and everything off the path steps back. SQL function internals start folded into the function node (`+n inside`); unfolding dives the camera into the RETURN expression and the base columns. Picking a hop in the inspector selects it on the canvas and glides the camera to it and its neighbours (a hop inside a folded function opens it; the traced field brings the whole path back). **Replay flow** (graph toolbar, or `&replay=1` on a field deep link; `&replay=hold` arms it and waits) plays the trace as motion: the path goes dark, the call travels down the execution path node by node with a pulse on every edge, then the value comes back up the lineage layer by layer until it lands in the field with its live value; the replay runs over whatever is unfolded at the time. Zoomed out, nodes fall back to a large name-only rendering so the map stays legible; zoom in for types, layers, live values and edge conditions.
- Explain / Investigate open with the verdict (Known, Need More Evidence, Unknown) and the confidence; every fact below it carries its evidence ids.
- Fonts (Geist, Geist Mono) and icons (Phosphor) are bundled with the app; nothing is loaded from the internet. Motion honours `prefers-reduced-motion`.

## API

All routes are under `/api/xray`; OpenAPI at `/openapi/v1.json`.

| Route | Purpose |
|---|---|
| `GET /health`, `GET /cases`, `GET /cases/{id}` | build report, case overview (key fields, gaps, traces, tool policy) |
| `GET /graph?root&depth&columns&mode=architecture` | subgraph / architecture view |
| `GET /nodes/{id}`, `GET /node?id=` | node details (+ live values when `traceId`/`scope` given) |
| `GET /search?q=` | node search |
| `GET /trace/field?field&traceId&scope` | **Trace Source**: upstream lineage tree, execution path, unknowns, evidence ids |
| `GET /impact/{id}`, `GET /impact?id=` | **Impact Analysis**: downstream closure, key paths to the surface |
| `POST /runtime/pick-order` | Live Trace through the tool gateway (`trace_pick_order` / `trace_material`) |
| `POST /runtime/tools`, `GET /runtime/tools`, `GET /runtime/audit` | whitelisted read-only tools, policy, audit log |
| `POST /ai/explain`, `POST /ai/investigate`, `POST /ai/impact-summary` | evidence-bound explanations (contract in design §7.1) |

Errors are RFC 9457 problem details (404 unknown node/trace, 400 ambiguous field or bad body, 403 denied runtime operation).

## AI provider

The default provider is `rules`: a deterministic, offline investigator that produces the same evidence-bound output shape as the LLM path (facts with evidence ids, hypotheses with status, unknowns, audit with model/version/timestamp). To use a company-approved OpenAI-compatible endpoint:

```bash
export MESXRAY_AI_API_KEY=...                # never put the key in appsettings or source
export XRay__AI__Provider=openai-compatible
export XRay__AI__BaseUrl=https://<approved-endpoint>/v1
export XRay__AI__Model=<model>
dotnet run --project src/MesXray.Api
```

The LLM receives only the evidence bundle (never a connection string, host or raw response) and must answer with a JSON schema. Every fact is re-validated by `EvidenceBindingValidator`: facts citing ids outside the bundle are downgraded to unverified hypotheses, and the verdict/confidence are capped. If the endpoint is unavailable the API falls back to the rule engine and records that in `audit.note`.

Verdict rules (rule engine and validator alike): an Unknown/Pending definition on the path, or an unknown claimed by the model, gives *Need More Evidence*; an observed value that is only explained by an unverified hypothesis (an `ISNULL` default, a constant branch such as `RETURN 1000`) also gives *Need More Evidence*; runtime details that were merely not captured (values computed inside SQL Server) are reported under `unknowns` but do not make an evidenced lineage unknown. *Known* means every hop is backed by scanned code or runtime evidence.

## Tests and CI

```bash
dotnet test MesXray.slnx              # 95 tests: scanners, graph, runtime gateway, AI, integration (AC-01 … AC-08)
cd src/MesXray.Web && npm run build   # tsc --noEmit (app + e2e) + vite build
cd src/MesXray.Web && npm run e2e     # 15 Playwright tests: the seven demo scenes in EN and ZH, plus the language switch
cd src/MesXray.Web && npm run demo:shots   # re-shoot docs/demo/*.png from the same scene definitions
```

The end-to-end suite (`src/MesXray.Web/e2e/`) drives the deep links of `docs/demo-script.md` through the real API and UI: it starts its own API (port 5090, fixtures only) and Vite (port 5199), so a running `scripts/xray.sh` stack is left alone. Numbers are compared with what the API returns for the same request; the facts the talk track quotes (`@WMS_Enabled` branches, the `ISNULL` hypothesis, `STRING_AGG`, the Pending `GetStorageBin`) are asserted literally. Locally it uses the Google Chrome that is already installed (nothing is downloaded; set `PW_CHANNEL` to use another channel); CI installs Playwright's Chromium.

`.github/workflows/ci.yml` runs four jobs on every push/PR: .NET build and tests (warnings are errors), web type-check and build, the Playwright demo scenes, and a gitleaks secret scan that keeps fixtures and docs sanitized.

## Configuration

Section `XRay` in `src/MesXray.Api/appsettings.json` (override with `XRay__Section__Key` environment variables):

| Key | Default | Meaning |
|---|---|---|
| `Fixtures:Root` | `fixtures/pick-order-details` | case folder (resolved relative to the repo root) |
| `Graph:Mode` / `Graph:ExportPath` | `ScanAndCurate` / null | build mode, optional snapshot export |
| `CorsOrigins` | Vite dev origins | allowed browser origins |
| `Runtime:AllowedEnvironments` | `FIXTURE, TEST, UAT` | runtime data from any other environment is refused |
| `Runtime:MaxRows`, `Runtime:Timeout`, `Runtime:MaxArgumentLength` | 200, 10 s, 100 | query limits |
| `AI:Provider`, `AI:BaseUrl`, `AI:Model`, `AI:ApiKeyEnvironmentVariable`, `AI:FallbackToRules` | `rules`, null, `gpt-4o-mini`, `MESXRAY_AI_API_KEY`, true | AI provider |

Copy `.env.example` for the variables the tooling reads; never commit `.env`.
