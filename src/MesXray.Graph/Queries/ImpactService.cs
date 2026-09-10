using MesXray.Domain.Abstractions;
using MesXray.Domain.Graph;

namespace MesXray.Graph.Queries;

/// <summary>
/// Impact Analysis: everything downstream of a node (who consumes its value or depends on it), computed as a BFS over
/// <see cref="RelationSemantics.DownstreamNeighbor"/> plus structural propagation from a member to its container
/// (a changed result column changes the SP's output, which changes what the method returns, and so on).
/// Impact is deliberately conservative: it over-approximates rather than miss a consumer.
/// </summary>
public sealed class ImpactService
{
    public const int MaxDepth = 16;
    public const int MaxAffected = 500;

    private readonly IGraphRepository _graph;

    public ImpactService(IGraphRepository graph)
    {
        _graph = graph;
    }

    public ImpactResult Analyze(string nodeId, int maxDepth = MaxDepth)
    {
        var origin = _graph.FindNode(nodeId) ?? throw new NodeNotFoundException(nodeId);
        maxDepth = Math.Clamp(maxDepth, 1, MaxDepth);

        var previous = new Dictionary<string, (string Parent, Edge Edge, string Reason)>(StringComparer.Ordinal);
        var distance = new Dictionary<string, int>(StringComparer.Ordinal) { [origin.Id] = 0 };
        var queue = new Queue<string>();
        queue.Enqueue(origin.Id);
        var usedEdges = new Dictionary<string, Edge>(StringComparer.Ordinal);
        var truncated = false;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var d = distance[current];
            if (d >= maxDepth)
            {
                truncated = true;
                continue;
            }

            foreach (var (edge, next, reason) in DownstreamOf(current))
            {
                usedEdges.TryAdd(edge.Id, edge);
                if (distance.ContainsKey(next))
                {
                    continue;
                }

                if (distance.Count > MaxAffected)
                {
                    truncated = true;
                    break;
                }

                distance[next] = d + 1;
                previous[next] = (current, edge, reason);
                queue.Enqueue(next);
            }
        }

        var affected = distance
            .Where(kv => kv.Key != origin.Id)
            .Select(kv =>
            {
                var node = _graph.FindNode(kv.Key)!;
                return new ImpactedNode(node, kv.Value, PathTo(kv.Key, origin.Id, previous), previous[kv.Key].Reason);
            })
            .OrderBy(a => a.Distance)
            .ThenBy(a => LayerRank(a.Node.Layer))
            .ThenBy(a => a.Node.Id, StringComparer.Ordinal)
            .ToList();

        var summary = affected
            .GroupBy(a => a.Node.Type.ToString(), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var keyPaths = affected
            .Where(a => a.Node.Type is NodeType.Page or NodeType.Api or NodeType.JsonField)
            .OrderBy(a => a.Node.Type == NodeType.Page ? 0 : a.Node.Type == NodeType.Api ? 1 : 2)
            .ThenBy(a => a.Node.Id, StringComparer.Ordinal)
            .Take(12)
            .Select(a => (IReadOnlyList<string>)a.Path)
            .ToList();

        var nodes = distance.Keys.Select(id => _graph.FindNode(id)!).OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
        var edges = usedEdges.Values
            .Where(e => distance.ContainsKey(e.FromNodeId) && distance.ContainsKey(e.ToNodeId))
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ToList();

        return new ImpactResult
        {
            Origin = origin,
            Affected = affected,
            Summary = summary,
            KeyPaths = keyPaths,
            EvidenceIds = edges.Select(e => e.Id).ToList(),
            Graph = new Subgraph(origin.Id, nodes, edges, truncated),
            Truncated = truncated,
        };
    }

    private IEnumerable<(Edge Edge, string Next, string Reason)> DownstreamOf(string nodeId)
    {
        foreach (var edge in _graph.OutEdges(nodeId).Concat(_graph.InEdges(nodeId)).OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            var downstream = RelationSemantics.DownstreamNeighbor(edge, nodeId);
            if (downstream is not null)
            {
                yield return (edge, downstream, Describe(edge, nodeId));
                continue;
            }

            // Structural propagation: member -> container, model -> fields typed as that model.
            if (edge.RelationType == RelationType.Contains && edge.ToNodeId == nodeId)
            {
                yield return (edge, edge.FromNodeId, "contains the changed member");
            }
            else if (edge.RelationType == RelationType.OfType && edge.ToNodeId == nodeId)
            {
                yield return (edge, edge.FromNodeId, "is typed as the changed model");
            }
        }
    }

    private static string Describe(Edge edge, string current) => edge.FromNodeId == current
        ? $"{Humanize(edge.RelationType)} (data flows into it)"
        : $"{Humanize(edge.RelationType)} it";

    private static string Humanize(RelationType relation) => relation switch
    {
        RelationType.HandledBy => "is handled by",
        RelationType.Calls => "calls",
        RelationType.ExecutesSp => "executes",
        RelationType.EnrichedBy => "is enriched by",
        RelationType.BranchesOn => "branches on",
        RelationType.UsesParameter => "uses parameter",
        RelationType.Reads => "reads",
        RelationType.CallsFunction => "calls function",
        RelationType.ComputedBy => "is computed by",
        RelationType.AliasOf => "aliases",
        RelationType.DerivedFrom => "derives from",
        RelationType.ControlledBy => "is controlled by",
        RelationType.Returns => "returns",
        RelationType.MapsTo => "maps to",
        RelationType.SerializesAs => "serializes as",
        RelationType.Produces => "produces",
        _ => relation.ToString().ToLowerInvariant(),
    };

    private static IReadOnlyList<string> PathTo(string nodeId, string origin, Dictionary<string, (string Parent, Edge Edge, string Reason)> previous)
    {
        var path = new List<string> { nodeId };
        var current = nodeId;
        while (current != origin && previous.TryGetValue(current, out var p))
        {
            current = p.Parent;
            path.Insert(0, current);
        }

        return path;
    }

    private static int LayerRank(Layer layer) => layer switch
    {
        Layer.Web => 0,
        Layer.Api => 1,
        Layer.Service => 2,
        Layer.Data => 3,
        _ => 4,
    };
}
