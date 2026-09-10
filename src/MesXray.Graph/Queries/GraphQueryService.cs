using MesXray.Domain.Abstractions;
using MesXray.Domain.Graph;

namespace MesXray.Graph.Queries;

/// <summary>Neighbourhood and architecture queries. Pure functions over the repository; no caching needed at POC scale.</summary>
public sealed class GraphQueryService
{
    public const int MaxDepth = 8;
    public const int MaxNodes = 400;

    private readonly IGraphRepository _graph;

    public GraphQueryService(IGraphRepository graph)
    {
        _graph = graph;
    }

    /// <summary>Undirected BFS neighbourhood of <paramref name="rootNodeId"/> up to <paramref name="depth"/> hops.</summary>
    public Subgraph GetSubgraph(string rootNodeId, int depth, bool includeColumnLevel = true)
    {
        var root = _graph.FindNode(rootNodeId) ?? throw new NodeNotFoundException(rootNodeId);
        depth = Math.Clamp(depth, 0, MaxDepth);

        var visited = new Dictionary<string, int>(StringComparer.Ordinal) { [root.Id] = 0 };
        var queue = new Queue<string>();
        queue.Enqueue(root.Id);
        var edges = new Dictionary<string, Edge>(StringComparer.Ordinal);
        var truncated = false;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentDepth = visited[current];
            if (currentDepth >= depth)
            {
                continue;
            }

            foreach (var edge in _graph.OutEdges(current).Concat(_graph.InEdges(current)))
            {
                var other = edge.FromNodeId == current ? edge.ToNodeId : edge.FromNodeId;
                if (!includeColumnLevel && IsColumnLevel(other))
                {
                    continue;
                }

                edges[edge.Id] = edge;
                if (visited.ContainsKey(other))
                {
                    continue;
                }

                if (visited.Count >= MaxNodes)
                {
                    truncated = true;
                    continue;
                }

                visited[other] = currentDepth + 1;
                queue.Enqueue(other);
            }
        }

        var nodes = visited.Keys.Select(id => _graph.FindNode(id)!).OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
        var edgeList = edges.Values
            .Where(e => visited.ContainsKey(e.FromNodeId) && visited.ContainsKey(e.ToNodeId))
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
        return new Subgraph(root.Id, nodes, edgeList, truncated);
    }

    /// <summary>
    /// The business-level architecture map: from a page/api root, follow execution-path relations plus the SP-level
    /// dependencies (parameters, tables, functions). Column-level nodes are excluded; they belong to field traces.
    /// </summary>
    public Subgraph GetArchitecture(string rootNodeId)
    {
        var root = _graph.FindNode(rootNodeId) ?? throw new NodeNotFoundException(rootNodeId);
        var visited = new HashSet<string>(StringComparer.Ordinal) { root.Id };
        var queue = new Queue<string>();
        queue.Enqueue(root.Id);
        var edges = new List<Edge>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var edge in _graph.OutEdges(current))
            {
                if (!IsArchitectureRelation(edge.RelationType))
                {
                    continue;
                }

                edges.Add(edge);
                if (visited.Add(edge.ToNodeId))
                {
                    queue.Enqueue(edge.ToNodeId);
                }
            }
        }

        var nodes = visited.Select(id => _graph.FindNode(id)!).OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
        return new Subgraph(root.Id, nodes, edges.OrderBy(e => e.Id, StringComparer.Ordinal).ToList(), Truncated: false);
    }

    public NodeDetails GetNodeDetails(string nodeId)
    {
        var node = _graph.FindNode(nodeId) ?? throw new NodeNotFoundException(nodeId);
        var incoming = _graph.InEdges(nodeId);
        var outgoing = _graph.OutEdges(nodeId);
        var neighborIds = incoming.Select(e => e.FromNodeId).Concat(outgoing.Select(e => e.ToNodeId))
            .Distinct(StringComparer.Ordinal);
        var neighbors = neighborIds.Select(id => _graph.FindNode(id)!).OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
        var asOutput = _graph.LineagesFor(nodeId);
        var asSource = _graph.Lineages.Where(l => l.SourceFieldId == nodeId).OrderBy(l => l.Id, StringComparer.Ordinal).ToList();
        var container = FindContainer(nodeId);
        var evidence = incoming.Select(e => e.Id).Concat(outgoing.Select(e => e.Id))
            .Concat(asOutput.Select(l => l.Id)).Concat(asSource.Select(l => l.Id))
            .Distinct(StringComparer.Ordinal).ToList();
        return new NodeDetails(node, incoming, outgoing, neighbors, asOutput, asSource, container, evidence);
    }

    /// <summary>The node that structurally contains <paramref name="nodeId"/> (SP for a result column, model for a field...).</summary>
    public Node? FindContainer(string nodeId)
    {
        var containsEdge = _graph.InEdges(nodeId).FirstOrDefault(e => e.RelationType == RelationType.Contains);
        return containsEdge is null ? null : _graph.FindNode(containsEdge.FromNodeId);
    }

    internal static bool IsColumnLevel(string nodeId) => NodeIds.Prefix(nodeId) is
        "spcol" or "ctecol" or "tmpcol" or "expr" or "column" or "field" or "json" or "cte" or "tmp";

    internal static bool IsArchitectureRelation(RelationType relation) => relation is
        RelationType.Calls or
        RelationType.HandledBy or
        RelationType.ExecutesSp or
        RelationType.CallsFunction or
        RelationType.UsesParameter or
        RelationType.Reads or
        RelationType.Returns or
        RelationType.BranchesOn;
}
