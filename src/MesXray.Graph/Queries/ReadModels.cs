using System.Text.Json;
using MesXray.Domain.Graph;

namespace MesXray.Graph.Queries;

/// <summary>A renderable slice of the graph.</summary>
public sealed record Subgraph(
    string? RootNodeId,
    IReadOnlyList<Node> Nodes,
    IReadOnlyList<Edge> Edges,
    bool Truncated);

/// <summary>Node plus its immediate neighbourhood and lineage records.</summary>
public sealed record NodeDetails(
    Node Node,
    IReadOnlyList<Edge> Incoming,
    IReadOnlyList<Edge> Outgoing,
    IReadOnlyList<Node> Neighbors,
    IReadOnlyList<FieldLineage> LineageAsOutput,
    IReadOnlyList<FieldLineage> LineageAsSource,
    Node? Container,
    IReadOnlyList<string> EvidenceIds);

/// <summary>A runtime value attached to a trace/impact node.</summary>
public sealed record RuntimeValue(string EvidenceId, string? Scope, string? Label, JsonElement Value, string EvidenceType);

/// <summary>An explicit gap: a node we reached but whose definition we do not have.</summary>
public sealed record Unknown(string NodeId, string Name, NodeType Type, string Reason);

/// <summary>One hop in a Trace Source tree. Children are the upstream sources of this node.</summary>
public sealed record TraceHop
{
    public required string NodeId { get; init; }

    public required Node Node { get; init; }

    /// <summary>Relation of the edge that led here from the downstream node (null for the root).</summary>
    public RelationType? ViaRelation { get; init; }

    public string? ViaEdgeId { get; init; }

    public TransformType? TransformType { get; init; }

    public string? Expression { get; init; }

    public string? Condition { get; init; }

    public double Confidence { get; init; } = 1.0;

    public NodeStatus Status { get; init; }

    /// <summary>Owning container (SP for result columns, model for fields, API for JSON fields).</summary>
    public string? ContainerNodeId { get; init; }

    public IReadOnlyList<RuntimeValue> RuntimeValues { get; init; } = [];

    /// <summary>True when this node already appeared elsewhere in the tree and was not expanded again.</summary>
    public bool IsRepeat { get; init; }

    public IReadOnlyList<TraceHop> Sources { get; init; } = [];
}

/// <summary>Result of Trace Source for one field.</summary>
public sealed record FieldTrace
{
    public required Node Field { get; init; }

    public required TraceHop Root { get; init; }

    /// <summary>Static path Page -> API -> methods -> SPs that produce the traced field.</summary>
    public IReadOnlyList<Node> ExecutionPath { get; init; } = [];

    public IReadOnlyList<Unknown> Unknowns { get; init; } = [];

    public IReadOnlyList<string> EvidenceIds { get; init; } = [];

    public required Subgraph Graph { get; init; }

    public string? TraceId { get; init; }

    public string? Scope { get; init; }
}

/// <summary>One affected node with the path that connects it to the changed node.</summary>
public sealed record ImpactedNode(Node Node, int Distance, IReadOnlyList<string> Path, string Reason);

/// <summary>Result of Impact Analysis for one node.</summary>
public sealed record ImpactResult
{
    public required Node Origin { get; init; }

    public IReadOnlyList<ImpactedNode> Affected { get; init; } = [];

    /// <summary>Affected node counts per type, for the summary chips.</summary>
    public IReadOnlyDictionary<string, int> Summary { get; init; } = new Dictionary<string, int>();

    /// <summary>Parameter -> SP -> Field -> API -> UI style narratives for the most relevant targets.</summary>
    public IReadOnlyList<IReadOnlyList<string>> KeyPaths { get; init; } = [];

    public IReadOnlyList<string> EvidenceIds { get; init; } = [];

    public required Subgraph Graph { get; init; }

    public bool Truncated { get; init; }
}

/// <summary>Raised when a short field name matches several nodes.</summary>
public sealed class AmbiguousFieldException : Exception
{
    public AmbiguousFieldException(string field, IReadOnlyList<string> candidates)
        : base($"Field '{field}' is ambiguous; specify one of: {string.Join(", ", candidates)}")
    {
        Field = field;
        Candidates = candidates;
    }

    public string Field { get; }

    public IReadOnlyList<string> Candidates { get; }
}

/// <summary>Raised when a node id cannot be found in the store.</summary>
public sealed class NodeNotFoundException : Exception
{
    public NodeNotFoundException(string nodeId)
        : base($"Node '{nodeId}' was not found.")
    {
        NodeId = nodeId;
    }

    public string NodeId { get; }
}
