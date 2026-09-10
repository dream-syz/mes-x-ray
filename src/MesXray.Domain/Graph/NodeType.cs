namespace MesXray.Domain.Graph;

/// <summary>
/// Kind of thing a graph node represents. Mirrors the design document
/// (Page/API/Method/Model/Field/SP/Function/Table/Column/SystemParameter/Branch)
/// and adds the SQL-internal kinds needed for column-level lineage.
/// </summary>
public enum NodeType
{
    /// <summary>A UI page/screen (e.g. Web VP Pick Order Details).</summary>
    Page,

    /// <summary>An HTTP endpoint (method + route).</summary>
    Api,

    /// <summary>A .NET method (controller action, service or repository method).</summary>
    Method,

    /// <summary>A .NET DTO/model class.</summary>
    Model,

    /// <summary>A property on a .NET model.</summary>
    Field,

    /// <summary>A field in the serialized JSON response as seen by the UI.</summary>
    JsonField,

    /// <summary>A stored procedure.</summary>
    StoredProcedure,

    /// <summary>A user-defined SQL function.</summary>
    Function,

    /// <summary>A base table (or view).</summary>
    Table,

    /// <summary>A column of a base table.</summary>
    Column,

    /// <summary>A column in a stored procedure's final result set.</summary>
    ResultColumn,

    /// <summary>An intermediate relation inside a SQL object: CTE, temp table or derived table.</summary>
    Intermediate,

    /// <summary>A column of an intermediate relation.</summary>
    IntermediateColumn,

    /// <summary>A SQL expression (CASE, aggregate, arithmetic, function call) that produces a column.</summary>
    Expression,

    /// <summary>A system/configuration parameter (e.g. WMS_Enabled).</summary>
    SystemParameter,

    /// <summary>A control-flow branch (reserved for explicit branch nodes).</summary>
    Branch,
}
