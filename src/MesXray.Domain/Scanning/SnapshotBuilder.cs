using MesXray.Domain.Graph;

namespace MesXray.Domain.Scanning;

/// <summary>
/// Accumulates nodes, edges and lineage while scanning, de-duplicating by id. A node that is first referenced
/// (placeholder) and later defined is upgraded; an edge seen twice keeps the highest confidence.
/// </summary>
public sealed class SnapshotBuilder
{
    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Edge> _edges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FieldLineage> _lineages = new(StringComparer.Ordinal);
    private readonly List<ScanDiagnostic> _diagnostics = [];
    private readonly string _source;
    private readonly string _scanVersion;

    public SnapshotBuilder(string source, string scanVersion)
    {
        _source = source;
        _scanVersion = scanVersion;
    }

    public string ScanVersion => _scanVersion;

    public IReadOnlyList<ScanDiagnostic> Diagnostics => _diagnostics;

    public bool HasNode(string id) => _nodes.ContainsKey(id);

    public Node? Find(string id) => _nodes.GetValueOrDefault(id);

    /// <summary>Adds or upgrades a node definition.</summary>
    public Node Define(Node node)
    {
        var stamped = node.ScanVersion is null ? node with { ScanVersion = _scanVersion } : node;
        if (_nodes.TryGetValue(node.Id, out var existing))
        {
            // Never downgrade: a Known definition beats a placeholder; same status -> merge metadata (new wins).
            if (existing.Status == NodeStatus.Known && stamped.Status != NodeStatus.Known)
            {
                return existing;
            }

            var metadata = new Dictionary<string, string>(existing.Metadata, StringComparer.Ordinal);
            foreach (var (k, v) in stamped.Metadata)
            {
                metadata[k] = v;
            }

            stamped = stamped with
            {
                Source = stamped.Source ?? existing.Source,
                Description = stamped.Description ?? existing.Description,
                QualifiedName = stamped.QualifiedName ?? existing.QualifiedName,
                Metadata = metadata,
            };
        }

        _nodes[node.Id] = stamped;
        return stamped;
    }

    /// <summary>Ensures a node exists; creates an Unknown placeholder when it does not.</summary>
    public Node Reference(string id, NodeType type, string name, Layer layer, string? reason = null, string? qualifiedName = null)
    {
        if (_nodes.TryGetValue(id, out var existing))
        {
            return existing;
        }

        var node = new Node
        {
            Id = id,
            Type = type,
            Name = name,
            QualifiedName = qualifiedName,
            Layer = layer,
            Status = NodeStatus.Unknown,
            Metadata = new Dictionary<string, string>
            {
                ["reason"] = reason ?? "Referenced but its definition was not scanned.",
            },
            ScanVersion = _scanVersion,
        };
        _nodes[id] = node;
        return node;
    }

    public Edge Link(
        string fromNodeId,
        RelationType relation,
        string toNodeId,
        EvidenceType evidenceType,
        string? evidenceRef,
        double confidence = 1.0,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var edge = new Edge
        {
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            RelationType = relation,
            Confidence = confidence,
            EvidenceType = evidenceType,
            EvidenceRef = evidenceRef,
            Metadata = metadata ?? new Dictionary<string, string>(),
        };

        if (_edges.TryGetValue(edge.Id, out var existing))
        {
            if (existing.Confidence >= edge.Confidence)
            {
                // Keep the stronger edge but remember the extra evidence location.
                if (evidenceRef is not null && existing.EvidenceRef != evidenceRef)
                {
                    var merged = new Dictionary<string, string>(existing.Metadata, StringComparer.Ordinal);
                    var refs = new SortedSet<string>(StringComparer.Ordinal) { existing.EvidenceRef ?? string.Empty, evidenceRef };
                    if (merged.TryGetValue("evidenceRefs", out var previous))
                    {
                        foreach (var r in previous.Split(';', StringSplitOptions.RemoveEmptyEntries))
                        {
                            refs.Add(r);
                        }
                    }

                    refs.Remove(string.Empty);
                    merged["evidenceRefs"] = string.Join(";", refs);
                    _edges[edge.Id] = existing with { Metadata = merged };
                }

                return _edges[edge.Id];
            }
        }

        _edges[edge.Id] = edge;
        return edge;
    }

    public FieldLineage AddLineage(FieldLineage lineage)
    {
        if (_lineages.TryGetValue(lineage.Id, out var existing))
        {
            var evidence = new SortedSet<string>(existing.EvidenceEdgeIds.Concat(lineage.EvidenceEdgeIds), StringComparer.Ordinal);
            var merged = existing with
            {
                Expression = existing.Expression ?? lineage.Expression,
                EvidenceEdgeIds = evidence.ToList(),
                Confidence = Math.Max(existing.Confidence, lineage.Confidence),
            };
            _lineages[lineage.Id] = merged;
            return merged;
        }

        _lineages[lineage.Id] = lineage;
        return lineage;
    }

    public void Report(ScanDiagnosticSeverity severity, string message, string? file = null, int? line = null)
        => _diagnostics.Add(new ScanDiagnostic(severity, message, file, line));

    public ScanResult Build()
    {
        var snapshot = new GraphSnapshot
        {
            Source = _source,
            ScanVersion = _scanVersion,
            GeneratedAt = DateTimeOffset.UtcNow,
            Nodes = _nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal).ToList(),
            Edges = _edges.Values.OrderBy(e => e.Id, StringComparer.Ordinal).ToList(),
            Lineages = _lineages.Values.OrderBy(l => l.Id, StringComparer.Ordinal).ToList(),
        };
        return new ScanResult(snapshot, _diagnostics.ToList());
    }
}
