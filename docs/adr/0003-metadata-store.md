# ADR-0003: Metadata store — in-memory graph with versioned JSON snapshots

- Status: accepted (POC scope)
- Date: 2026-09-10
- Design reference: §11 (Metadata DB: "SQLite（Demo）或 SQL Server, 暂不需要 Neo4j"), §17 ("expected graph 有版本控制")

## Context

The graph for one case is small (≈180 nodes, ≈300 edges, ≈90 lineage records) and is **rebuilt from source on every start**: scanners are deterministic, curated files live in Git, and the runtime fixture is a file. The design's requirement is that the expected graph be version-controlled and that most of it be reproducible by the scanners — not that a database exists.

## Decision

`MesXray.Graph` keeps the graph in an immutable in-memory state (`InMemoryGraphStore`) behind two ports from the Domain: `IGraphRepository` (read) and `IGraphStore` (merge / load / export). Persistence is a JSON snapshot (`GraphSnapshot`, `GraphSnapshotFile`) in the shared camelCase dialect (`XRayJson`), which is also the format of the curated `expected-graph/*.json` files and of the optional `XRay:Graph:ExportPath` export.

Merge rules are encoded once (`Mergers.cs`):

| Rule | Detail |
|---|---|
| Node key | id. Status precedence Known > Pending > Unknown; `Authoritative` lets the incoming status win. |
| Placeholders | an Unknown placeholder is replaced by any incoming definition; a description written on an Unknown node (human knowledge) is kept unless the merge is `Authoritative`. |
| Curated text | descriptions written by curated sources are never overwritten by scanners (`FillGaps`). |
| Edge key | `(from, relation, to)`; confidence is the max, evidence refs are unioned. |
| Lineage key | id (`lineage:{out}<-{src}[{cond}]`); evidence edge ids are unioned. |
| Referential completeness | edges/lineage referencing undefined nodes create Unknown placeholders so a trace can always end at an explicit gap. |

The API bootstrapper applies: scanners (`FillGaps`) → linker → ground truth and manual overrides (`Authoritative`).

## Consequences

- Zero infrastructure for the demo; `dotnet run` is the whole deployment. Determinism is tested (`AC-08`).
- Multi-case or multi-user scenarios would need a persistent store. The ports are the seam: a SQLite/SQL Server implementation of `IGraphStore` can replace the in-memory one without touching scanners, queries or the API. Snapshot JSON remains the import/export and Git-diff format.
- Graph queries are simple BFS/DFS over adjacency indexes built at merge time; the node budget (`MaxNodes`) and depth limits in `GraphQueryService`, `FieldTraceService` and `ImpactService` keep responses bounded (progressive disclosure, design §15).
