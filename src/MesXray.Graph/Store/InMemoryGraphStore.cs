using MesXray.Domain.Abstractions;
using MesXray.Domain.Graph;

namespace MesXray.Graph.Store;

/// <summary>
/// Thread-safe in-memory metadata store. The whole graph for a case is small (hundreds of nodes), so an immutable
/// snapshot swapped atomically on every write is simpler and safer than fine-grained locking.
///
/// <para>Merge rules (see docs/adr/0003-metadata-store.md):</para>
/// <list type="bullet">
/// <item>Nodes are keyed by id. Status precedence is Known > Pending > Unknown; <see cref="MergePolicy.Authoritative"/> lets the incoming status win.</item>
/// <item>An Unknown placeholder is fully replaced by any incoming definition.</item>
/// <item>Descriptions written by curated sources are never overwritten by scanners.</item>
/// <item>Edges are keyed by (from, relation, to); confidence is the max, evidence refs are unioned.</item>
/// <item>Lineage records are keyed by id; evidence edge ids are unioned.</item>
/// <item>Edges referencing undefined nodes create Unknown placeholders so the graph is always referentially complete.</item>
/// </list>
/// </summary>
public sealed class InMemoryGraphStore : IGraphStore
{
    private readonly Lock _writeLock = new();
    private volatile State _state = State.Empty;

    public IReadOnlyCollection<Node> Nodes => _state.Nodes.Values;

    public IReadOnlyCollection<Edge> Edges => _state.Edges.Values;

    public IReadOnlyCollection<FieldLineage> Lineages => _state.Lineages.Values;

    public Node? FindNode(string nodeId) => _state.Nodes.GetValueOrDefault(nodeId);

    public IReadOnlyList<Edge> OutEdges(string nodeId) => _state.Outgoing.GetValueOrDefault(nodeId, []);

    public IReadOnlyList<Edge> InEdges(string nodeId) => _state.Incoming.GetValueOrDefault(nodeId, []);

    public IReadOnlyList<FieldLineage> LineagesFor(string outputFieldId) => _state.LineageByOutput.GetValueOrDefault(outputFieldId, []);

    public GraphSnapshot ToSnapshot()
    {
        var state = _state;
        return new GraphSnapshot
        {
            Source = "merged",
            GeneratedAt = DateTimeOffset.UtcNow,
            Nodes = state.Nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal).ToList(),
            Edges = state.Edges.Values.OrderBy(e => e.Id, StringComparer.Ordinal).ToList(),
            Lineages = state.Lineages.Values.OrderBy(l => l.Id, StringComparer.Ordinal).ToList(),
        };
    }

    public void Load(GraphSnapshot snapshot)
    {
        lock (_writeLock)
        {
            _state = State.Empty;
            MergeCore(snapshot, MergePolicy.Authoritative);
        }
    }

    public void Merge(GraphSnapshot snapshot, MergePolicy policy = MergePolicy.FillGaps)
    {
        lock (_writeLock)
        {
            MergeCore(snapshot, policy);
        }
    }

    private void MergeCore(GraphSnapshot snapshot, MergePolicy policy)
    {
        var nodes = new Dictionary<string, Node>(_state.Nodes, StringComparer.Ordinal);
        var edges = new Dictionary<string, Edge>(_state.Edges, StringComparer.Ordinal);
        var lineages = new Dictionary<string, FieldLineage>(_state.Lineages, StringComparer.Ordinal);

        foreach (var incoming in snapshot.Nodes)
        {
            nodes[incoming.Id] = nodes.TryGetValue(incoming.Id, out var existing)
                ? NodeMerger.Merge(existing, incoming, policy)
                : incoming;
        }

        foreach (var incoming in snapshot.Edges)
        {
            EnsureNode(nodes, incoming.FromNodeId);
            EnsureNode(nodes, incoming.ToNodeId);
            edges[incoming.Id] = edges.TryGetValue(incoming.Id, out var existing)
                ? EdgeMerger.Merge(existing, incoming)
                : incoming;
        }

        foreach (var incoming in snapshot.Lineages)
        {
            EnsureNode(nodes, incoming.OutputFieldId);
            if (incoming.SourceFieldId is not null)
            {
                EnsureNode(nodes, incoming.SourceFieldId);
            }

            lineages[incoming.Id] = lineages.TryGetValue(incoming.Id, out var existing)
                ? LineageMerger.Merge(existing, incoming)
                : incoming;
        }

        _state = State.Build(nodes, edges, lineages);
    }

    private static void EnsureNode(Dictionary<string, Node> nodes, string nodeId)
    {
        if (!nodes.ContainsKey(nodeId))
        {
            nodes[nodeId] = NodePlaceholders.For(nodeId);
        }
    }

    private sealed class State
    {
        public static readonly State Empty = Build([], [], []);

        private State(
            Dictionary<string, Node> nodes,
            Dictionary<string, Edge> edges,
            Dictionary<string, FieldLineage> lineages,
            Dictionary<string, IReadOnlyList<Edge>> outgoing,
            Dictionary<string, IReadOnlyList<Edge>> incoming,
            Dictionary<string, IReadOnlyList<FieldLineage>> lineageByOutput)
        {
            Nodes = nodes;
            Edges = edges;
            Lineages = lineages;
            Outgoing = outgoing;
            Incoming = incoming;
            LineageByOutput = lineageByOutput;
        }

        public Dictionary<string, Node> Nodes { get; }

        public Dictionary<string, Edge> Edges { get; }

        public Dictionary<string, FieldLineage> Lineages { get; }

        public Dictionary<string, IReadOnlyList<Edge>> Outgoing { get; }

        public Dictionary<string, IReadOnlyList<Edge>> Incoming { get; }

        public Dictionary<string, IReadOnlyList<FieldLineage>> LineageByOutput { get; }

        public static State Build(
            Dictionary<string, Node> nodes,
            Dictionary<string, Edge> edges,
            Dictionary<string, FieldLineage> lineages)
        {
            var ordered = edges.Values.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            var outgoing = ordered.GroupBy(e => e.FromNodeId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Edge>)g.ToList(), StringComparer.Ordinal);
            var incoming = ordered.GroupBy(e => e.ToNodeId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<Edge>)g.ToList(), StringComparer.Ordinal);
            var byOutput = lineages.Values.OrderBy(l => l.Id, StringComparer.Ordinal)
                .GroupBy(l => l.OutputFieldId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<FieldLineage>)g.ToList(), StringComparer.Ordinal);
            return new State(nodes, edges, lineages, outgoing, incoming, byOutput);
        }
    }
}
