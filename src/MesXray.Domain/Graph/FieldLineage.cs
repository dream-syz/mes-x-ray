using System.Text.Json.Serialization;

namespace MesXray.Domain.Graph;

/// <summary>How a value is transformed on one lineage hop.</summary>
public enum TransformType
{
    /// <summary>Plain rename/alias/pass-through.</summary>
    Direct,

    /// <summary>SUM/MIN/MAX/COUNT/STRING_AGG ...</summary>
    Aggregate,

    /// <summary>CASE / IIF / branch-dependent value.</summary>
    Conditional,

    /// <summary>ORM/Dapper name mapping from result column to property.</summary>
    Mapping,

    /// <summary>JSON serialization of a property.</summary>
    Serialization,

    /// <summary>Value returned by a (user-defined) function call.</summary>
    FunctionCall,

    /// <summary>Other computed expression (arithmetic, concatenation, ISNULL...).</summary>
    Expression,

    /// <summary>A constant; there is no upstream source by design (e.g. <c>'' AS PhysicalWagonId</c>).</summary>
    Literal,
}

/// <summary>
/// One hop of field-level lineage: <see cref="OutputFieldId"/> gets its value from <see cref="SourceFieldId"/>
/// through <see cref="TransformType"/>. A hop with several sources (e.g. a CASE with two branches) is stored as
/// several records sharing the same output.
/// </summary>
public sealed record FieldLineage
{
    public required string OutputFieldId { get; init; }

    /// <summary>Upstream node; null when the value is a literal or the source could not be resolved.</summary>
    public string? SourceFieldId { get; init; }

    public required TransformType TransformType { get; init; }

    /// <summary>Expression text, e.g. <c>SUM(MainResults.AvailableQuantity)</c>.</summary>
    public string? Expression { get; init; }

    /// <summary>Branch/filter condition under which this hop applies, e.g. <c>@WMS_Enabled = 1</c>.</summary>
    public string? Condition { get; init; }

    /// <summary>Edges that prove this hop.</summary>
    public IReadOnlyList<string> EvidenceEdgeIds { get; init; } = [];

    public double Confidence { get; init; } = 1.0;

    /// <summary>Deterministic identity; doubles as an evidence id for the AI layer.</summary>
    [JsonInclude]
    public string Id => $"lineage:{OutputFieldId}<-{SourceFieldId ?? "?"}{(Condition is null ? string.Empty : $"[{Condition}]")}";
}
