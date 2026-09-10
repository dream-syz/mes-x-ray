namespace MesXray.Domain.Graph;

/// <summary>A vertex of the evidence graph. Identity is the stable <see cref="Id"/> (see <see cref="NodeIds"/>).</summary>
public sealed record Node
{
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata = new Dictionary<string, string>();

    /// <summary>Stable identifier, e.g. <c>sp:dbo.AP_Pick_GetPickOrderRows</c>.</summary>
    public required string Id { get; init; }

    public required NodeType Type { get; init; }

    /// <summary>Short display name.</summary>
    public required string Name { get; init; }

    /// <summary>Fully qualified name (namespace + type + member, schema + object, JSON path...).</summary>
    public string? QualifiedName { get; init; }

    public required Layer Layer { get; init; }

    public NodeStatus Status { get; init; } = NodeStatus.Known;

    /// <summary>Source/SQL location of the definition, when known.</summary>
    public SourceLocation? Source { get; init; }

    /// <summary>Type-specific details: route, parameters, expression text, data type, ...</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = EmptyMetadata;

    /// <summary>Human/business description. Curated for ground truth; may later be AI-generated and marked as such.</summary>
    public string? Description { get; init; }

    /// <summary>Scanner name and version that produced the definition (e.g. <c>sql-scanner/0.1.0</c>).</summary>
    public string? ScanVersion { get; init; }

    public bool IsResolved => Status == NodeStatus.Known;

    public string? GetMetadata(string key) => Metadata.TryGetValue(key, out var value) ? value : null;
}
