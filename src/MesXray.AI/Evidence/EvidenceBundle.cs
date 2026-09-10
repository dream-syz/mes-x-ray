using System.Text.Json;
using MesXray.Domain.Graph;

namespace MesXray.AI.Evidence;

/// <summary>Kind of a cite-able evidence item.</summary>
public enum EvidenceKind
{
    Node,
    Edge,
    Lineage,
    Runtime,
}

/// <summary>One thing the AI may cite. <see cref="Id"/> is the only handle it is allowed to use.</summary>
public sealed record EvidenceItem(string Id, EvidenceKind Kind, string Text, string? NodeId = null, string? Scope = null, JsonElement? Value = null);

/// <summary>Flattened Trace Source hop for the model and the rule engine.</summary>
public sealed record HopSummary(
    int Depth,
    string NodeId,
    string Name,
    NodeType Type,
    NodeStatus Status,
    RelationType? ViaRelation,
    string? ViaEdgeId,
    TransformType? Transform,
    string? Expression,
    string? Condition,
    string? ContainerNodeId,
    IReadOnlyList<string> RuntimeEvidenceIds,
    IReadOnlyList<string> RuntimeValueTexts,
    bool IsRepeat);

/// <summary>
/// Everything the AI Investigator is allowed to reason from: the focus node, the upstream hops, cite-able evidence
/// items and explicit unknowns. The LLM never sees the graph or the database directly.
/// </summary>
public sealed record EvidenceBundle
{
    public required Node Focus { get; init; }

    public string? Question { get; init; }

    public string? TraceId { get; init; }

    public string? Scope { get; init; }

    public string? Environment { get; init; }

    /// <summary>Page -> API -> methods -> SPs, as node names.</summary>
    public IReadOnlyList<Node> ExecutionPath { get; init; } = [];

    public IReadOnlyList<HopSummary> Hops { get; init; } = [];

    public IReadOnlyList<EvidenceItem> Items { get; init; } = [];

    /// <summary>Unknown/Pending nodes reached while tracing, with the reason.</summary>
    public IReadOnlyList<string> Unknowns { get; init; } = [];

    /// <summary>Ids that may be cited (equal to <see cref="Items"/> ids, optionally narrowed by the caller).</summary>
    public required IReadOnlySet<string> AllowedEvidenceIds { get; init; }

    public EvidenceItem? Item(string id) => Items.FirstOrDefault(i => i.Id == id);

    public IEnumerable<HopSummary> HopsOfType(NodeType type) => Hops.Where(h => h.Type == type && !h.IsRepeat);
}
