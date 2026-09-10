using MesXray.Domain.Abstractions;
using MesXray.Domain.Evidence;
using MesXray.Domain.Graph;

namespace MesXray.Graph.Queries;

/// <summary>
/// Trace Source: starting from an output field, walk upstream along lineage relations until we reach base columns,
/// system parameters, functions or an Unknown node. Deterministic, evidence-only; no inference.
/// </summary>
public sealed class FieldTraceService
{
    public const int MaxDepth = 24;
    public const int MaxHops = 300;

    private readonly IGraphRepository _graph;
    private readonly GraphQueryService _queries;

    public FieldTraceService(IGraphRepository graph, GraphQueryService queries)
    {
        _graph = graph;
        _queries = queries;
    }

    /// <summary>
    /// Resolves <paramref name="field"/> (node id or short name such as <c>availableQuantity</c>) and builds the trace.
    /// When <paramref name="runtime"/> is supplied its evidence is overlaid on the hops (Live Trace).
    /// </summary>
    public FieldTrace Trace(string field, RuntimeTrace? runtime = null, string? scope = null)
    {
        var node = ResolveField(field);
        var evidence = new SortedSet<string>(StringComparer.Ordinal);
        var unknowns = new Dictionary<string, Unknown>(StringComparer.Ordinal);
        var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        var edges = new Dictionary<string, Edge>(StringComparer.Ordinal);
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        var hopBudget = MaxHops;

        var root = BuildHop(node, via: null, lineage: null, depth: 0, path: [], runtime, scope, evidence, unknowns, nodes, edges, expanded, ref hopBudget);

        var executionPath = ResolveExecutionPath(node, nodes.Values.ToList(), nodes, edges);
        AddContainers(nodes, edges);

        // Lineage summaries whose both ends are inside the trace are evidence for the AI layer as well.
        foreach (var lineage in _graph.Lineages)
        {
            if (nodes.ContainsKey(lineage.OutputFieldId) && (lineage.SourceFieldId is null || nodes.ContainsKey(lineage.SourceFieldId)))
            {
                evidence.Add(lineage.Id);
            }
        }

        var graph = new Subgraph(
            node.Id,
            nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal).ToList(),
            edges.Values.OrderBy(e => e.Id, StringComparer.Ordinal).ToList(),
            Truncated: hopBudget <= 0);

        return new FieldTrace
        {
            Field = node,
            Root = root,
            ExecutionPath = executionPath,
            Unknowns = unknowns.Values.OrderBy(u => u.NodeId, StringComparer.Ordinal).ToList(),
            EvidenceIds = evidence.ToList(),
            Graph = graph,
            TraceId = runtime?.TraceId,
            Scope = scope,
        };
    }

    /// <summary>Node id, exact JSON path, or short name (last segment) matched case-insensitively against field-like nodes.</summary>
    public Node ResolveField(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            throw new NodeNotFoundException(field);
        }

        var direct = _graph.FindNode(field);
        if (direct is not null)
        {
            return direct;
        }

        var json = _graph.FindNode(NodeIds.Json(field));
        if (json is not null)
        {
            return json;
        }

        foreach (var type in new[] { NodeType.JsonField, NodeType.Field, NodeType.ResultColumn, NodeType.IntermediateColumn, NodeType.Column })
        {
            var candidates = _graph.Nodes
                .Where(n => n.Type == type && string.Equals(NodeIds.LeafName(n.Id), field, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n.Id, StringComparer.Ordinal)
                .ToList();
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            if (candidates.Count > 1)
            {
                throw new AmbiguousFieldException(field, candidates.Select(c => c.Id).ToList());
            }
        }

        throw new NodeNotFoundException(field);
    }

    private TraceHop BuildHop(
        Node node,
        Edge? via,
        FieldLineage? lineage,
        int depth,
        IReadOnlyList<string> path,
        RuntimeTrace? runtime,
        string? scope,
        SortedSet<string> evidence,
        Dictionary<string, Unknown> unknowns,
        Dictionary<string, Node> nodes,
        Dictionary<string, Edge> edges,
        HashSet<string> expanded,
        ref int hopBudget)
    {
        nodes[node.Id] = node;
        if (via is not null)
        {
            edges[via.Id] = via;
            evidence.Add(via.Id);
        }

        if (lineage is not null)
        {
            evidence.Add(lineage.Id);
            foreach (var edgeId in lineage.EvidenceEdgeIds)
            {
                evidence.Add(edgeId);
            }
        }

        var runtimeValues = CollectRuntimeValues(node.Id, runtime, scope, evidence);

        if (node.Status != NodeStatus.Known)
        {
            unknowns.TryAdd(node.Id, new Unknown(node.Id, node.Name, node.Type, node.GetMetadata("reason") ?? DefaultReason(node)));
        }

        var isRepeat = !expanded.Add(node.Id);
        var sources = new List<TraceHop>();

        if (!isRepeat && node.Status == NodeStatus.Known && depth < MaxDepth && hopBudget > 0)
        {
            var newPath = path.Append(node.Id).ToList();
            foreach (var (edge, upstreamId) in UpstreamLineageEdges(node.Id))
            {
                if (newPath.Contains(upstreamId, StringComparer.Ordinal))
                {
                    continue; // cycle
                }

                if (hopBudget-- <= 0)
                {
                    break;
                }

                var upstream = _graph.FindNode(upstreamId) ?? NodePlaceholderFor(upstreamId);
                var hopLineage = FindLineage(node, upstreamId, edge, evidence);
                sources.Add(BuildHop(upstream, edge, hopLineage, depth + 1, newPath, runtime, scope, evidence, unknowns, nodes, edges, expanded, ref hopBudget));
            }

            // A model field with no lineage of its own is populated by whatever fills its model: the SP that Dapper maps
            // to the model, or the method that enriches the parent field. Following those makes an unscanned SP or a
            // Pending method visible instead of silently ending the trace at a Known field.
            if (sources.Count == 0 && node.Type == NodeType.Field)
            {
                foreach (var (edge, upstreamId) in StructuralFallbackEdges(node.Id))
                {
                    if (newPath.Contains(upstreamId, StringComparer.Ordinal) || hopBudget-- <= 0)
                    {
                        continue;
                    }

                    var upstream = _graph.FindNode(upstreamId) ?? NodePlaceholderFor(upstreamId);
                    sources.Add(BuildHop(upstream, edge, null, depth + 1, newPath, runtime, scope, evidence, unknowns, nodes, edges, expanded, ref hopBudget));
                }

                if (sources.Count == 0)
                {
                    unknowns.TryAdd($"{node.Id}#no-source", new Unknown(node.Id, node.Name, node.Type,
                        "No lineage source was found for this field: the code that assigns it was not scanned."));
                }
            }

            // Lineage records whose source could not be resolved are gaps too (literals are not: they have no source by design).
            foreach (var dangling in _graph.LineagesFor(node.Id).Where(l => l.SourceFieldId is null))
            {
                evidence.Add(dangling.Id);
                if (dangling.TransformType != TransformType.Literal)
                {
                    unknowns.TryAdd($"{node.Id}#unresolved", new Unknown(node.Id, node.Name, node.Type,
                        $"Source of expression '{dangling.Expression}' could not be resolved statically."));
                }
            }
        }

        return new TraceHop
        {
            NodeId = node.Id,
            Node = node,
            ViaRelation = via?.RelationType,
            ViaEdgeId = via?.Id,
            TransformType = lineage?.TransformType ?? InferTransform(via),
            Expression = lineage?.Expression ?? node.GetMetadata("expression"),
            // The edge carries the merged condition when one source feeds several CASE branches.
            Condition = via?.GetMetadata("condition") ?? lineage?.Condition,
            Confidence = Math.Min(via?.Confidence ?? 1.0, lineage?.Confidence ?? 1.0),
            Status = node.Status,
            ContainerNodeId = _queries.FindContainer(node.Id)?.Id,
            RuntimeValues = runtimeValues,
            IsRepeat = isRepeat,
            Sources = sources.OrderBy(s => s.Condition ?? string.Empty, StringComparer.Ordinal).ThenBy(s => s.NodeId, StringComparer.Ordinal).ToList(),
        };
    }

    private IEnumerable<(Edge Edge, string UpstreamId)> UpstreamLineageEdges(string nodeId)
    {
        foreach (var edge in _graph.OutEdges(nodeId).Concat(_graph.InEdges(nodeId)))
        {
            if (!RelationSemantics.IsLineage(edge.RelationType))
            {
                continue;
            }

            var upstream = RelationSemantics.UpstreamNeighbor(edge, nodeId);
            if (upstream is not null)
            {
                yield return (edge, upstream);
            }
        }
    }

    /// <summary>
    /// Structural producers of a model field that has no lineage of its own: the SPs Dapper maps onto its model and the
    /// enrichment methods attached to the fields typed as that model (e.g. <c>StorageBin -EnrichedBy-> GetStorageBin</c>).
    /// </summary>
    private IEnumerable<(Edge Edge, string UpstreamId)> StructuralFallbackEdges(string fieldId)
    {
        var model = _queries.FindContainer(fieldId);
        if (model is not { Type: NodeType.Model })
        {
            yield break;
        }

        foreach (var edge in _graph.InEdges(model.Id).OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            switch (edge.RelationType)
            {
                case RelationType.MapsTo:
                    yield return (edge, edge.FromNodeId);
                    break;
                case RelationType.OfType:
                    foreach (var enrichment in _graph.OutEdges(edge.FromNodeId).Where(e => e.RelationType == RelationType.EnrichedBy).OrderBy(e => e.Id, StringComparer.Ordinal))
                    {
                        yield return (enrichment, enrichment.ToNodeId);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Lineage record describing the hop <paramref name="node"/> -> <paramref name="sourceId"/>. Lineage is keyed by the
    /// produced column, so for an expression node the column it produces is used; when the same source feeds several
    /// CASE branches the conditions are merged.
    /// </summary>
    private FieldLineage? FindLineage(Node node, string sourceId, Edge via, SortedSet<string> evidence)
    {
        var outputId = node.Type == NodeType.Expression ? ProducedColumnOf(node) ?? node.Id : node.Id;
        var candidates = _graph.LineagesFor(outputId).Where(l => l.SourceFieldId == sourceId).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        // Every branch record of this hop is evidence, even though the hop displays one of them.
        foreach (var candidate in candidates)
        {
            evidence.Add(candidate.Id);
            foreach (var edgeId in candidate.EvidenceEdgeIds)
            {
                evidence.Add(edgeId);
            }
        }

        var condition = via.GetMetadata("condition");
        return candidates.FirstOrDefault(l => l.Condition == condition) ?? candidates[0];
    }

    private string? ProducedColumnOf(Node expression)
        => expression.GetMetadata("produces")
           ?? _graph.OutEdges(expression.Id).FirstOrDefault(e => e.RelationType == RelationType.Produces)?.ToNodeId;

    private static IReadOnlyList<RuntimeValue> CollectRuntimeValues(string nodeId, RuntimeTrace? runtime, string? scope, SortedSet<string> evidence)
    {
        if (runtime is null)
        {
            return [];
        }

        var values = runtime.Evidence
            .Where(e => e.NodeId == nodeId && (scope is null || e.Scope is null || string.Equals(e.Scope, scope, StringComparison.OrdinalIgnoreCase)))
            .Select(e => new RuntimeValue(e.Id, e.Scope, e.Label, e.Value, e.EvidenceType.ToString()))
            .ToList();
        foreach (var v in values)
        {
            evidence.Add(v.EvidenceId);
        }

        return values;
    }

    /// <summary>
    /// Finds the SPs that own result columns in the trace and returns the shortest static path from the page/API to
    /// each of them, as a single ordered list (Web -> API -> Service -> SP).
    /// </summary>
    private List<Node> ResolveExecutionPath(Node root, IReadOnlyList<Node> traceNodes, Dictionary<string, Node> nodes, Dictionary<string, Edge> edges)
    {
        // Result columns lead to their SP; SPs, UDFs and (pending) methods reached structurally are targets themselves.
        var sqlObjects = traceNodes
            .Where(n => n.Type is NodeType.ResultColumn or NodeType.Function or NodeType.StoredProcedure or NodeType.Method)
            .Select(n => n.Type == NodeType.ResultColumn ? _queries.FindContainer(n.Id) : n)
            .Where(n => n is not null)
            .Select(n => n!.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var entry = FindEntryPoint(root);
        if (entry is null || sqlObjects.Count == 0)
        {
            return [];
        }

        var ordered = new List<Node>();
        foreach (var target in sqlObjects)
        {
            var path = ShortestExecutionPath(entry.Id, target);
            if (path is null)
            {
                continue;
            }

            for (var i = 0; i < path.Count; i++)
            {
                var n = _graph.FindNode(path[i])!;
                nodes[n.Id] = n;
                if (!ordered.Any(o => o.Id == n.Id))
                {
                    ordered.Add(n);
                }

                if (i > 0)
                {
                    var edge = _graph.OutEdges(path[i - 1]).First(e => e.ToNodeId == path[i] && RelationSemantics.IsExecutionPath(e.RelationType));
                    edges[edge.Id] = edge;
                }
            }
        }

        return ordered;
    }

    /// <summary>
    /// The page that calls the API owning the traced field; falls back to the API itself, then to any page in the store.
    /// </summary>
    private Node? FindEntryPoint(Node root)
    {
        var container = root.Type == NodeType.JsonField ? _queries.FindContainer(root.Id) : null;
        if (container is { Type: NodeType.Api })
        {
            var page = _graph.InEdges(container.Id)
                .Where(e => e.RelationType == RelationType.Calls)
                .Select(e => _graph.FindNode(e.FromNodeId))
                .FirstOrDefault(n => n is { Type: NodeType.Page });
            return page ?? container;
        }

        return _graph.Nodes.Where(n => n.Type == NodeType.Page).OrderBy(n => n.Id, StringComparer.Ordinal).FirstOrDefault()
               ?? _graph.Nodes.Where(n => n.Type == NodeType.Api).OrderBy(n => n.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    private List<string>? ShortestExecutionPath(string from, string to)
    {
        var previous = new Dictionary<string, string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { from };
        var queue = new Queue<string>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == to)
            {
                var path = new List<string> { to };
                while (previous.TryGetValue(path[0], out var prev))
                {
                    path.Insert(0, prev);
                }

                return path;
            }

            foreach (var edge in _graph.OutEdges(current).Where(e => RelationSemantics.IsExecutionPath(e.RelationType)).OrderBy(e => e.Id, StringComparer.Ordinal))
            {
                if (visited.Add(edge.ToNodeId))
                {
                    previous[edge.ToNodeId] = current;
                    queue.Enqueue(edge.ToNodeId);
                }
            }
        }

        return null;
    }

    private void AddContainers(Dictionary<string, Node> nodes, Dictionary<string, Edge> edges)
    {
        foreach (var node in nodes.Values.ToList())
        {
            var containsEdge = _graph.InEdges(node.Id).FirstOrDefault(e => e.RelationType == RelationType.Contains);
            if (containsEdge is null)
            {
                continue;
            }

            var container = _graph.FindNode(containsEdge.FromNodeId);
            if (container is null)
            {
                continue;
            }

            nodes.TryAdd(container.Id, container);
            edges.TryAdd(containsEdge.Id, containsEdge);
        }
    }

    private static Node NodePlaceholderFor(string nodeId) => Store.NodePlaceholders.For(nodeId);

    private static TransformType? InferTransform(Edge? via) => via?.RelationType switch
    {
        RelationType.SerializesAs => TransformType.Serialization,
        RelationType.MapsTo => TransformType.Mapping,
        RelationType.AliasOf => TransformType.Direct,
        RelationType.ComputedBy => TransformType.FunctionCall,
        RelationType.ControlledBy => TransformType.Conditional,
        _ => null,
    };

    private static string DefaultReason(Node node) => node.Status switch
    {
        NodeStatus.Pending => "Deliberately not scanned in this POC iteration.",
        _ => "Definition not scanned; need more evidence.",
    };
}
