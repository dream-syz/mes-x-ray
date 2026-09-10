namespace MesXray.Domain.Graph;

/// <summary>
/// A serializable, versioned set of nodes, edges and lineage records. Ground-truth fixtures, scanner outputs and the
/// merged metadata store all use this shape, which keeps import/export/diff trivial.
/// </summary>
public sealed record GraphSnapshot
{
    public const string CurrentSchemaVersion = "1.0";

    public string SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Producer of this snapshot: <c>ground-truth</c>, <c>dotnet-scanner</c>, <c>sql-scanner</c>, <c>merged</c>...</summary>
    public string Source { get; init; } = "unknown";

    public string? ScanVersion { get; init; }

    public DateTimeOffset? GeneratedAt { get; init; }

    public IReadOnlyList<Node> Nodes { get; init; } = [];

    public IReadOnlyList<Edge> Edges { get; init; } = [];

    public IReadOnlyList<FieldLineage> Lineages { get; init; } = [];

    public static GraphSnapshot Empty(string source) => new() { Source = source };
}
