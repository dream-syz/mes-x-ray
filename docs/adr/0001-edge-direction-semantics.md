# ADR-0001: Edge direction semantics

- Status: accepted
- Date: 2026-09-10
- Design reference: §5.2 Edge, §6 Field-Level Lineage

## Context

The design lists relation types that point in two different "directions" of meaning. `spcol -MapsTo-> field -SerializesAs-> json` follows the **data flow** (source to consumer), while `expr -DerivedFrom-> column`, `method -ExecutesSp-> sp` and `sp -UsesParameter-> param` follow the **dependency** (consumer to source). Both readings are natural for their layer, and the ground truth in appendix A uses both. Trace Source and Impact Analysis, however, must walk a single consistent notion of "upstream" and "downstream" across all layers, otherwise a trace would stop at the first edge written the other way round.

## Decision

Every `RelationType` declares a `FlowDirection` in `RelationSemantics`:

| FlowDirection | Meaning of `from -> to` | Relations |
|---|---|---|
| `DependsOn` | `from` consumes / depends on `to`; **data flows to → from** | `HandledBy`, `Calls`, `ExecutesSp`, `EnrichedBy`, `BranchesOn`, `UsesParameter`, `Reads`, `CallsFunction`, `ComputedBy`, `AliasOf`, `DerivedFrom`, `ControlledBy`, `DependsOn` |
| `FlowsTo` | value of `from` becomes `to`; **data flows from → to** | `Returns`, `MapsTo`, `SerializesAs`, `Produces` |
| `Structural` | ownership / typing, no data flow | `Contains`, `OfType` |

Edges are stored exactly as authored (so they read naturally in the graph and in the curated JSON) and the semantics are resolved at query time:

- `RelationSemantics.UpstreamNeighbor(edge, node)` returns the producer side; Trace Source walks upstream over lineage relations only.
- `RelationSemantics.DownstreamNeighbor(edge, node)` returns the consumer side; Impact Analysis walks downstream and propagates through structural edges (a column impacts its table's readers, a JSON field impacts its API and page).
- The UI lays out every edge in flow direction (`DependsOn` edges are reversed for layout) so the Evidence Graph always reads Data → Web left to right.

Edge ids are `edge:{from}|{Relation}|{to}` so the same fact from two scanners merges into one edge with the union of its evidence refs.

## Consequences

- Scanners and curators author edges in the form that is most natural for them; they never have to think about traversal.
- Adding a relation type requires classifying it in `RelationSemantics` (the switch has no default so the compiler flags the omission).
- Lineage records (`FieldLineage`) stay keyed by the **produced** column; an expression node's lineage is found through its `Produces` edge, and a source feeding several CASE branches yields one edge whose `condition` metadata merges the branch predicates while each branch keeps its own lineage record.
