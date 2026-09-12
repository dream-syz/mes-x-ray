using System.Globalization;
using System.Text.RegularExpressions;
using MesXray.Domain.Abstractions;
using MesXray.Domain.Evidence;
using MesXray.Domain.Graph;
using MesXray.Graph.Queries;

namespace MesXray.AI.Evidence;

/// <summary>Builds an <see cref="EvidenceBundle"/> from a Trace Source result and the optional live trace.</summary>
public sealed partial class EvidenceBundleBuilder
{
    private readonly IGraphRepository _graph;

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Identifiers();

    public EvidenceBundleBuilder(IGraphRepository graph)
    {
        _graph = graph;
    }

    public EvidenceBundle Build(FieldTrace trace, RuntimeTrace? runtime, string? question, IReadOnlyList<string>? allowedEvidenceIds, int maxItems, string? language = null)
    {
        var hops = new List<HopSummary>();
        Flatten(trace.Root, 0, hops);

        var items = new Dictionary<string, EvidenceItem>(StringComparer.Ordinal);
        var nodesById = new Dictionary<string, Node>(StringComparer.Ordinal);
        var lineageIndex = _graph.Lineages.ToDictionary(l => l.Id, StringComparer.Ordinal);
        var literalsByOutput = _graph.Lineages
            .Where(l => l.SourceFieldId is null && l.TransformType == TransformType.Literal && l.Expression is not null)
            .GroupBy(l => l.OutputFieldId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Id, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        for (var i = 0; i < hops.Count; i++)
        {
            var hop = hops[i];
            var node = _graph.FindNode(hop.NodeId);
            if (node is null)
            {
                continue;
            }

            nodesById[node.Id] = node;
            items.TryAdd(node.Id, new EvidenceItem(node.Id, EvidenceKind.Node, DescribeNode(node), node.Id));

            // Scanner notes (local variable assignments) and constant branches travel with the hop so the investigator
            // can state them as facts; the literal lineage records become cite-able evidence.
            var notes = node.GetMetadata("variables") is { } variables
                ? variables.Split("; ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [];
            var literals = literalsByOutput.TryGetValue(hop.NodeId, out var lineages)
                ? lineages.Select(l => new LiteralBranch(l.Condition, l.Expression!, l.Id)).ToList()
                : [];
            foreach (var literal in literals)
            {
                var lineage = lineageIndex[literal.LineageId];
                items.TryAdd(lineage.Id, new EvidenceItem(lineage.Id, EvidenceKind.Lineage, DescribeLineage(lineage), lineage.OutputFieldId));
            }

            if (notes.Length > 0 || literals.Count > 0)
            {
                hops[i] = hop with { Notes = notes, LiteralBranches = literals };
            }
        }

        foreach (var node in trace.ExecutionPath)
        {
            nodesById.TryAdd(node.Id, node);
            items.TryAdd(node.Id, new EvidenceItem(node.Id, EvidenceKind.Node, DescribeNode(node), node.Id));
        }

        var edgeIndex = _graph.Edges.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var runtimeIndex = runtime?.Evidence.ToDictionary(e => e.Id, StringComparer.Ordinal) ?? new Dictionary<string, RuntimeEvidence>(StringComparer.Ordinal);

        foreach (var id in trace.EvidenceIds)
        {
            if (edgeIndex.TryGetValue(id, out var edge))
            {
                items.TryAdd(id, new EvidenceItem(id, EvidenceKind.Edge, DescribeEdge(edge), edge.FromNodeId));
            }
            else if (lineageIndex.TryGetValue(id, out var lineage))
            {
                items.TryAdd(id, new EvidenceItem(id, EvidenceKind.Lineage, DescribeLineage(lineage), lineage.OutputFieldId));
            }
            else if (runtimeIndex.TryGetValue(id, out var evidence))
            {
                items.TryAdd(id, new EvidenceItem(id, EvidenceKind.Runtime, DescribeRuntime(evidence), evidence.NodeId, evidence.Scope, evidence.Value));
            }
        }

        // Parameter values are decisive for branch selection: include them even when not on a hop.
        if (runtime is not null)
        {
            foreach (var evidence in runtime.Evidence.Where(e => e.EvidenceType == RuntimeEvidenceType.Parameter))
            {
                if (hops.Any(h => h.NodeId == evidence.NodeId))
                {
                    items.TryAdd(evidence.Id, new EvidenceItem(evidence.Id, EvidenceKind.Runtime, DescribeRuntime(evidence), evidence.NodeId, evidence.Scope, evidence.Value));
                }
            }
        }

        var ordered = items.Values
            .OrderBy(i => i.Kind)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .Take(maxItems)
            .ToList();

        var allowed = ordered.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        if (allowedEvidenceIds is { Count: > 0 })
        {
            allowed.IntersectWith(allowedEvidenceIds);
            // Node ids are structural and always cite-able.
            foreach (var n in ordered.Where(i => i.Kind == EvidenceKind.Node))
            {
                allowed.Add(n.Id);
            }
        }

        var unknowns = trace.Unknowns.Select(u => $"{u.Name} ({u.NodeId}): {u.Reason}").ToList();
        var runtimeNotes = new List<string>();
        if (runtime is not null)
        {
            // Runtime notes are trace-wide; only those naming an object on this field's path belong to this explanation.
            var leafNames = hops.Select(h => h.Name)
                .Concat(trace.Unknowns.Select(u => u.Name))
                .Concat(trace.ExecutionPath.Select(n => n.Name))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n[(n.LastIndexOf('.') + 1)..])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            runtimeNotes.AddRange(runtime.Unknowns.Where(u => Identifiers().Matches(u).Any(m => leafNames.Contains(m.Value))));
        }

        return new EvidenceBundle
        {
            Focus = trace.Field,
            Question = question,
            TraceId = runtime?.TraceId ?? trace.TraceId,
            Scope = trace.Scope,
            Environment = runtime?.Environment,
            Language = Investigators.AiLanguage.Normalize(language),
            ExecutionPath = trace.ExecutionPath,
            Hops = hops,
            Items = ordered,
            Unknowns = unknowns.Distinct(StringComparer.Ordinal).ToList(),
            RuntimeNotes = runtimeNotes.Distinct(StringComparer.Ordinal).ToList(),
            AllowedEvidenceIds = allowed,
        };
    }

    private static void Flatten(TraceHop hop, int depth, List<HopSummary> output)
    {
        output.Add(new HopSummary(
            depth,
            hop.NodeId,
            hop.Node.Name,
            hop.Node.Type,
            hop.Status,
            hop.ViaRelation,
            hop.ViaEdgeId,
            hop.TransformType,
            hop.Expression,
            hop.Condition,
            hop.ContainerNodeId,
            hop.RuntimeValues.Select(v => v.EvidenceId).ToList(),
            hop.RuntimeValues.Select(v => $"{v.Label ?? hop.Node.Name} = {Format(v.Value)}").ToList(),
            hop.IsRepeat));

        foreach (var source in hop.Sources)
        {
            Flatten(source, depth + 1, output);
        }
    }

    public static string DescribeNode(Node node)
    {
        var text = $"{node.Type} '{node.QualifiedName ?? node.Name}' [{node.Status}]";
        if (node.Description is not null)
        {
            text += $": {node.Description}";
        }

        if (node.Status != NodeStatus.Known && node.GetMetadata("reason") is { } reason)
        {
            text += $" - {reason}";
        }

        return text;
    }

    public static string DescribeEdge(Edge edge)
    {
        var text = $"{edge.FromNodeId} -{edge.RelationType}-> {edge.ToNodeId}";
        if (edge.GetMetadata("condition") is { } condition)
        {
            text += $" when {condition}";
        }

        if (edge.GetMetadata("variable") is { } variable)
        {
            text += $" via {variable}";
        }

        if (edge.EvidenceRef is not null)
        {
            text += $" ({edge.EvidenceType} {edge.EvidenceRef})";
        }

        return text;
    }

    public static string DescribeLineage(FieldLineage lineage)
    {
        var source = lineage.SourceFieldId ?? (lineage.TransformType == TransformType.Literal ? "<literal>" : "<unknown>");
        var text = $"{lineage.OutputFieldId} <- {source} [{lineage.TransformType}]";
        if (lineage.Expression is not null)
        {
            text += $" {lineage.Expression}";
        }

        if (lineage.Condition is not null)
        {
            text += $" when {lineage.Condition}";
        }

        return text;
    }

    public static string DescribeRuntime(RuntimeEvidence evidence)
        => $"{evidence.Label ?? evidence.NodeId} = {Format(evidence.Value)} ({evidence.EvidenceType}, {evidence.Environment}, {evidence.ObservedAt.ToString("u", CultureInfo.InvariantCulture)}) on {evidence.NodeId}";

    public static string Format(System.Text.Json.JsonElement value) => value.ValueKind switch
    {
        System.Text.Json.JsonValueKind.String => $"\"{value.GetString()}\"",
        System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => "null",
        _ => value.GetRawText(),
    };
}
