using System.Text.Json;

namespace MesXray.Domain.Evidence;

/// <summary>Kind of runtime observation.</summary>
public enum RuntimeEvidenceType
{
    ApiResponse,
    QueryResult,
    Parameter,
    Log,
    Timing,
}

/// <summary>
/// A single observed value bound to an architecture node during one Live Trace. Values are already redacted when
/// they reach this type; nothing here may contain hosts, tokens or personal data.
/// </summary>
public sealed record RuntimeEvidence
{
    /// <summary>Evidence id, e.g. <c>ev-001</c>. Referenced by AI conclusions.</summary>
    public required string Id { get; init; }

    public required string TraceId { get; init; }

    /// <summary>Business entity type, e.g. <c>PickOrder</c>.</summary>
    public required string EntityType { get; init; }

    /// <summary>Business entity key, e.g. <c>PICK0843858</c>.</summary>
    public required string EntityKey { get; init; }

    /// <summary>The architecture node this value belongs to (json field, result column, parameter, SP...).</summary>
    public required string NodeId { get; init; }

    public required RuntimeEvidenceType EvidenceType { get; init; }

    /// <summary>Redacted value (scalar or JSON fragment).</summary>
    public required JsonElement Value { get; init; }

    /// <summary>Sub-entity scope, e.g. material <c>T12288</c> when the value belongs to one row.</summary>
    public string? Scope { get; init; }

    /// <summary>Short human label such as <c>T12288.availableQuantity</c>.</summary>
    public string? Label { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    /// <summary>Environment the value was observed in (TEST/UAT/FIXTURE). Never production in this POC.</summary>
    public required string Environment { get; init; }
}
