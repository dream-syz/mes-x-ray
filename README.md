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
scripts/xray.sh start          # add --open to launch the demo scene in the browser
scripts/xray.sh status         # pids, ports, API health
scripts/xray.sh logs           # follow artifacts/run/logs/{api,web}.log
scripts/xray.sh restart        # stop + rebuild + start   (--api-only / --web-only to limit)
scripts/xray.sh stop           # --force also frees the ports from processes it did not start
```

Options: `--release` (Release build), `--no-build` (reuse last build), `XRAY_API_PORT` / `XRAY_WEB_PORT` to move the ports; a repo-root `.env` (copy of `.env.example`) is loaded automatically. Pid files and logs live in `artifacts/run/`.

Or run the two processes by hand:

```bash
# 1. API (scans the fixture, builds the graph, preloads the runtime fixture) — http://localhost:5080
dotnet run --project src/MesXray.Api

# 2. Web UI (Vite dev server with /api proxied to :5080) — http://localhost:5173
cd src/MesXray.Web && npm install && npm run dev
```

Everything runs offline from `fixtures/pick-order-details`; no database, no network, no credentials are needed. Open <http://localhost:5173/?order=PICK0843858&field=availableQuantity&scope=T12288&investigate=1> for the headline demo scene in one click.

To serve the UI from the API instead of Vite: `npm run build` in `src/MesXray.Web` (output goes to `artifacts/web/`), copy `artifacts/web/*` to `src/MesXray.Api/wwwroot/`, and browse to <http://localhost:5080>.

## What you get

| Capability | Where | Design ref |
|---|---|---|
| Evidence Graph model: `Node`, `Edge`, `FieldLineage`, `RuntimeEvidence`, stable node ids, relation direction semantics | `src/MesXray.Domain` | §5, ADR-0001 |
| In-memory graph store with JSON snapshots, FillGaps/Authoritative merge, Dapper by-name linker, Trace Source, Impact Analysis, architecture view | `src/MesXray.Graph` | §3, §6, ADR-0003 |
| .NET scanner (Roslyn): endpoints, call chains, Dapper SP calls, models/properties, enrichment methods, branches, JSON serialization | `src/MesXray.Scanner.DotNet` | §4.1 |
| SQL scanner (ScriptDom): procedures/functions, tables, aliases, CTE/temp tables, CASE branch conditions, aggregates, UDF calls, system-parameter reads | `src/MesXray.Scanner.Sql` | §4.2 |
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
├─ .github/workflows/ci.yml           CI: dotnet build/test, web typecheck/build, secret scan
├─ scripts/xray.sh                    one-click start / stop / restart / status / logs
├─ src/
│  ├─ MesXray.Domain/          graph model, node-id conventions, ports (IRuntimeAdapter, IGraphRepository…)
│  ├─ MesXray.Graph/           store, snapshot IO, mergers, linker, queries (trace / impact / details)
│  ├─ MesXray.Scanner.DotNet/  Roslyn scanner
│  ├─ MesXray.Scanner.Sql/     ScriptDom scanner
│  ├─ MesXray.Runtime/         fixture adapter, evidence binder, tool gateway, redactor
│  ├─ MesXray.AI/              evidence bundle, investigators, validator, prompts (embedded)
│  ├─ MesXray.Api/             ASP.NET Core host, endpoints, bootstrapper
│  └─ MesXray.Web/             React + TypeScript + Vite UI
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
│  ├─ demo-script.md · security.md
└─ artifacts/                         generated, git-ignored: bin/ obj/ (.NET), web/ (Vite), test-results/, run/ (pids, logs)
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

`GET /api/xray/health` returns the build report (nodes/edges/lineage per step, unknown/pending counts, linker result). With the shipped fixture the scanners reproduce every ground-truth edge except the manually curated `page → API` call, and the linker maps all 18 result columns.

Modes (`XRay:Graph:Mode`): `ScanAndCurate` (default), `ScanOnly` (measure the scanners alone), `CuratedOnly` (no scanning). Set `XRay:Graph:ExportPath` to write the merged snapshot as JSON — commit it next to the fixture to version the expected graph.

## Running the demo

Follow [`docs/demo-script.md`](docs/demo-script.md) (design §14). The UI also supports deep links so every scene is one URL:

| Scene | URL |
|---|---|
| Trace `availableQuantity` for T12288 with live values and AI investigation | `/?order=PICK0843858&field=availableQuantity&scope=T12288&investigate=1` |
| PickStorageBin expressions (`SUM(APPQD.PickedQuantity)`, allocated) | `/?order=PICK0843858&field=json:pickOrderRows.pickStorageBin.allocatedQuantity&scope=T12288` |
| `WMS_Enabled` impact back to the page | `/?impact=param:WMS_Enabled` |
| FIFO / `STRING_AGG` storage-bin lineage | `/?order=PICK0843858&field=json:pickOrderRows.pickStorageBin.storageBin&scope=T12288` |
| Unknown by design: destination wagon storage bin | `/?field=json:pickOrderRows.destinationWagon.storageBin.location&explain=1` |

### How the UI reads

The UI is styled as an X-ray film: one dark theme, one accent. Everything the graph knows sits in grey; the accent is reserved for what the X-ray reveals, i.e. the traced path, the selected node and live evidence. Amber marks Unknown / Pending / Need More Evidence and red marks refuted hypotheses; nothing else is coloured. Layers (Web, API, Service, Data, Config) are labelled on each node rather than colour-coded.

- The Evidence Graph is ranked in data-flow direction and picks the orientation that fills the canvas best: long lineage chains run top-to-bottom (surface on top, SQL at the bottom, as in the design §8 wireframe), the broad architecture map runs left-to-right.
- When a trace or impact result arrives, a single sweep crosses the canvas, the lit path is drawn in flow order and everything off the path steps back. Zoomed out, nodes fall back to a large name-only rendering so the map stays legible; zoom in for types, layers, live values and edge conditions.
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

## Tests and CI

```bash
dotnet test MesXray.slnx          # 85 tests: scanners, graph, runtime gateway, AI, integration (AC-01 … AC-08)
cd src/MesXray.Web && npm run build   # tsc --noEmit + vite build
```

`.github/workflows/ci.yml` runs both on every push/PR.

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
