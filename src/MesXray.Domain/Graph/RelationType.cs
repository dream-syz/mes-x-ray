namespace MesXray.Domain.Graph;

/// <summary>
/// Relationship between two nodes. Every relation has a declared <see cref="FlowDirection"/>
/// (see <see cref="RelationSemantics"/>) so that Trace Source and Impact can be computed generically.
/// </summary>
public enum RelationType
{
    // ----- Structural (container -> member); never treated as data flow -----

    /// <summary>Container -> member (Model -> Field, SP -> ResultColumn, Api -> JsonField, SP -> Intermediate).</summary>
    Contains,

    /// <summary>Field -> Model: the field's declared type is that model.</summary>
    OfType,

    // ----- DependsOn (from depends on to; "to" is upstream) -----

    /// <summary>Api -> Method that handles the request.</summary>
    HandledBy,

    /// <summary>Method -> Method, or Page -> Api.</summary>
    Calls,

    /// <summary>Method -> StoredProcedure executed via Dapper/ADO.</summary>
    ExecutesSp,

    /// <summary>Field -> Method whose result is assigned into the field (row.PickStorageBin = ...).</summary>
    EnrichedBy,

    /// <summary>Method -> Field that a branch condition reads (if (pickOrder.IsMultiPickOrder)).</summary>
    BranchesOn,

    /// <summary>StoredProcedure/Function -> SystemParameter.</summary>
    UsesParameter,

    /// <summary>StoredProcedure/Function -> Table read in a FROM/JOIN.</summary>
    Reads,

    /// <summary>StoredProcedure/Function -> Function invoked.</summary>
    CallsFunction,

    /// <summary>Expression/Field -> Function whose return value is used.</summary>
    ComputedBy,

    /// <summary>ResultColumn/IntermediateColumn -> source column when the select item is a plain column reference.</summary>
    AliasOf,

    /// <summary>Expression -> Column/IntermediateColumn it reads.</summary>
    DerivedFrom,

    /// <summary>Expression -> SystemParameter/Column that selects the branch of a CASE or filter.</summary>
    ControlledBy,

    /// <summary>Generic fallback dependency.</summary>
    DependsOn,

    // ----- FlowsTo (data flows from "from" to "to"; "from" is upstream) -----

    /// <summary>Method -> Model it returns.</summary>
    Returns,

    /// <summary>ResultColumn -> Field populated by Dapper name mapping.</summary>
    MapsTo,

    /// <summary>Field -> JsonField as emitted by the JSON serializer.</summary>
    SerializesAs,

    /// <summary>Expression -> ResultColumn/IntermediateColumn it produces.</summary>
    Produces,
}
