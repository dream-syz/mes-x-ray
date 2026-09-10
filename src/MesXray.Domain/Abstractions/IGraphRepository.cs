using MesXray.Domain.Graph;

namespace MesXray.Domain.Abstractions;

/// <summary>Read side of the metadata store. Implementations must be safe for concurrent readers.</summary>
public interface IGraphRepository
{
    IReadOnlyCollection<Node> Nodes { get; }

    IReadOnlyCollection<Edge> Edges { get; }

    IReadOnlyCollection<FieldLineage> Lineages { get; }

    Node? FindNode(string nodeId);

    /// <summary>Edges whose <see cref="Edge.FromNodeId"/> is <paramref name="nodeId"/>.</summary>
    IReadOnlyList<Edge> OutEdges(string nodeId);

    /// <summary>Edges whose <see cref="Edge.ToNodeId"/> is <paramref name="nodeId"/>.</summary>
    IReadOnlyList<Edge> InEdges(string nodeId);

    IReadOnlyList<FieldLineage> LineagesFor(string outputFieldId);

    /// <summary>Full copy of the current store as a snapshot (for export / diffing against expected graphs).</summary>
    GraphSnapshot ToSnapshot();
}

/// <summary>How conflicting node attributes are resolved when merging a snapshot into the store.</summary>
public enum MergePolicy
{
    /// <summary>
    /// Scanner output: fills gaps and upgrades Unknown placeholders to Known, but never overrides curated
    /// descriptions or statuses.
    /// </summary>
    FillGaps,

    /// <summary>
    /// Curated ground truth / manual overrides: incoming status, description and metadata win on conflict.
    /// </summary>
    Authoritative,
}

/// <summary>Write side of the metadata store.</summary>
public interface IGraphWriter
{
    /// <summary>Replaces the whole store.</summary>
    void Load(GraphSnapshot snapshot);

    /// <summary>Merges a snapshot into the store using the merge rules documented on the implementation.</summary>
    void Merge(GraphSnapshot snapshot, MergePolicy policy = MergePolicy.FillGaps);
}

/// <summary>Convenience contract for components that both read and write the store (bootstrapping, tests).</summary>
public interface IGraphStore : IGraphRepository, IGraphWriter
{
}
