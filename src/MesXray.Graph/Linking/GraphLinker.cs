using MesXray.Domain.Abstractions;
using MesXray.Domain.Graph;

namespace MesXray.Graph.Linking;

/// <summary>Outcome of a linking pass, for logging and for the case overview.</summary>
public sealed record LinkReport(int MappedColumns, IReadOnlyList<string> UnmappedFields, IReadOnlyList<string> UnmappedColumns);

/// <summary>
/// Deterministic cross-scanner linking. The .NET scanner knows "method X executes SP Y and materialises type Z";
/// the SQL scanner knows the result columns of Y. Neither knows the other, so column -> property edges are created
/// here using Dapper's documented rule: case-insensitive name match.
/// </summary>
public sealed class GraphLinker
{
    public const string Version = "0.1.0";

    private readonly IGraphStore _store;

    public GraphLinker(IGraphStore store)
    {
        _store = store;
    }

    public LinkReport LinkDapperMappings()
    {
        var edges = new List<Edge>();
        var lineages = new List<FieldLineage>();
        var unmappedFields = new SortedSet<string>(StringComparer.Ordinal);
        var unmappedColumns = new SortedSet<string>(StringComparer.Ordinal);

        var spToModel = _store.Edges
            .Where(e => e.RelationType == RelationType.MapsTo && NodeIds.Prefix(e.FromNodeId) == "sp" && NodeIds.Prefix(e.ToNodeId) == "model")
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ToList();

        foreach (var mapping in spToModel)
        {
            var columns = _store.OutEdges(mapping.FromNodeId)
                .Where(e => e.RelationType == RelationType.Contains && NodeIds.Prefix(e.ToNodeId) == "spcol")
                .Select(e => _store.FindNode(e.ToNodeId)!)
                .ToList();
            var fields = _store.OutEdges(mapping.ToNodeId)
                .Where(e => e.RelationType == RelationType.Contains && NodeIds.Prefix(e.ToNodeId) == "field")
                .Select(e => _store.FindNode(e.ToNodeId)!)
                .ToList();

            if (columns.Count == 0)
            {
                // The SP was not scanned: nothing to link; the SP node stays Unknown and the trace shows it.
                continue;
            }

            var columnsByName = columns
                .GroupBy(c => NodeIds.LeafName(c.Id), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var matchedColumns = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                var fieldName = NodeIds.LeafName(field.Id);
                if (!columnsByName.TryGetValue(fieldName, out var column))
                {
                    // Nested/complex properties (PickStorageBin, DestinationWagon) are populated by code, not by Dapper.
                    if (!IsScalar(field))
                    {
                        continue;
                    }

                    unmappedFields.Add(field.Id);
                    continue;
                }

                matchedColumns.Add(column.Id);
                var edge = new Edge
                {
                    FromNodeId = column.Id,
                    ToNodeId = field.Id,
                    RelationType = RelationType.MapsTo,
                    Confidence = 0.95,
                    EvidenceType = EvidenceType.Linker,
                    EvidenceRef = $"{mapping.EvidenceRef ?? mapping.Id}; dapper name match",
                    Metadata = new Dictionary<string, string>
                    {
                        ["strategy"] = "DapperByName",
                        ["linker"] = $"graph-linker/{Version}",
                    },
                };
                edges.Add(edge);
                lineages.Add(new FieldLineage
                {
                    OutputFieldId = field.Id,
                    SourceFieldId = column.Id,
                    TransformType = TransformType.Mapping,
                    Expression = $"{NodeIds.LeafName(column.Id)} -> {fieldName}",
                    EvidenceEdgeIds = [edge.Id, mapping.Id],
                    Confidence = 0.95,
                });
            }

            foreach (var column in columns.Where(c => !matchedColumns.Contains(c.Id)))
            {
                unmappedColumns.Add(column.Id);
            }
        }

        _store.Merge(new GraphSnapshot
        {
            Source = "graph-linker",
            ScanVersion = $"graph-linker/{Version}",
            GeneratedAt = DateTimeOffset.UtcNow,
            Edges = edges,
            Lineages = lineages,
        });

        return new LinkReport(edges.Count, unmappedFields.ToList(), unmappedColumns.ToList());
    }

    /// <summary>The .NET scanner tags every property with <c>kind = scalar | model | collection</c>.</summary>
    private static bool IsScalar(Node field)
        => !string.Equals(field.GetMetadata("kind"), "model", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(field.GetMetadata("kind"), "collection", StringComparison.OrdinalIgnoreCase);
}
