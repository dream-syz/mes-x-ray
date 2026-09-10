using System.Text.Json.Serialization;

namespace MesXray.Domain.Graph;

/// <summary>
/// A directed, typed relationship. An edge is identified by (from, relation, to); the same fact discovered twice
/// is merged rather than duplicated (see graph merge rules).
/// </summary>
public sealed record Edge
{
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata = new Dictionary<string, string>();

    public required string FromNodeId { get; init; }

    public required string ToNodeId { get; init; }

    public required RelationType RelationType { get; init; }

    /// <summary>1.0 = statically certain; lower values indicate heuristic derivation.</summary>
    public double Confidence { get; init; } = 1.0;

    public EvidenceType EvidenceType { get; init; } = EvidenceType.Manual;

    /// <summary>Pointer to the proof: <c>file.cs:L42-L45</c>, <c>dbo.AP_X:L120</c>, a trace id, ...</summary>
    public string? EvidenceRef { get; init; }

    /// <summary>Relation-specific details: guard condition, branch value, mapping strategy, usage kind, ...</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = EmptyMetadata;

    /// <summary>Deterministic identity derived from the (from, relation, to) triple.</summary>
    [JsonInclude]
    public string Id => EdgeIds.Of(FromNodeId, RelationType, ToNodeId);

    public FlowDirection Direction => RelationSemantics.DirectionOf(RelationType);

    public string? GetMetadata(string key) => Metadata.TryGetValue(key, out var value) ? value : null;
}

/// <summary>Edge identity helper. Edge ids double as evidence ids for the AI layer.</summary>
public static class EdgeIds
{
    public static string Of(string fromNodeId, RelationType relation, string toNodeId)
        => $"edge:{fromNodeId}|{relation}|{toNodeId}";
}
