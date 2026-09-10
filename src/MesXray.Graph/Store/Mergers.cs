using MesXray.Domain.Abstractions;
using MesXray.Domain.Graph;

namespace MesXray.Graph.Store;

internal static class NodeMerger
{
    public static Node Merge(Node existing, Node incoming, MergePolicy policy)
    {
        // A placeholder carries no knowledge; whatever arrives replaces it (keeping the placeholder's reason for audit).
        // A description on an Unknown node is human knowledge ("name computed at runtime"), so a FillGaps scan keeps it.
        if (existing.Status == NodeStatus.Unknown && incoming.Status != NodeStatus.Unknown)
        {
            return incoming with
            {
                Description = policy == MergePolicy.Authoritative
                    ? incoming.Description ?? existing.Description
                    : existing.Description ?? incoming.Description,
                Metadata = MergeMetadata(existing.Metadata, incoming.Metadata, incomingWins: true),
            };
        }

        var incomingWins = policy == MergePolicy.Authoritative;

        var status = incomingWins
            ? incoming.Status
            : Strongest(existing.Status, incoming.Status);

        var definitionArrived = existing.Status != NodeStatus.Known && incoming.Status == NodeStatus.Known;

        return existing with
        {
            Type = existing.Type,
            Name = incomingWins ? incoming.Name : existing.Name,
            QualifiedName = incomingWins ? incoming.QualifiedName ?? existing.QualifiedName : existing.QualifiedName ?? incoming.QualifiedName,
            Layer = incomingWins ? incoming.Layer : existing.Layer,
            Status = status,
            Source = existing.Source ?? incoming.Source,
            Description = incomingWins ? incoming.Description ?? existing.Description : existing.Description ?? incoming.Description,
            Metadata = MergeMetadata(existing.Metadata, incoming.Metadata, incomingWins || definitionArrived),
            ScanVersion = definitionArrived || existing.ScanVersion is null ? incoming.ScanVersion ?? existing.ScanVersion : existing.ScanVersion,
        };
    }

    private static NodeStatus Strongest(NodeStatus a, NodeStatus b)
    {
        static int Rank(NodeStatus s) => s switch
        {
            NodeStatus.Known => 2,
            NodeStatus.Pending => 1,
            _ => 0,
        };

        return Rank(a) >= Rank(b) ? a : b;
    }

    internal static IReadOnlyDictionary<string, string> MergeMetadata(
        IReadOnlyDictionary<string, string> existing,
        IReadOnlyDictionary<string, string> incoming,
        bool incomingWins)
    {
        if (incoming.Count == 0)
        {
            return existing;
        }

        var result = new Dictionary<string, string>(existing, StringComparer.Ordinal);
        foreach (var (key, value) in incoming)
        {
            if (incomingWins || !result.ContainsKey(key))
            {
                result[key] = value;
            }
        }

        return result;
    }
}

internal static class EdgeMerger
{
    public static Edge Merge(Edge existing, Edge incoming)
    {
        var strongest = incoming.Confidence > existing.Confidence ? incoming : existing;
        var refs = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var r in new[] { existing.EvidenceRef, incoming.EvidenceRef }
                     .Concat(SplitRefs(existing.GetMetadata("evidenceRefs")))
                     .Concat(SplitRefs(incoming.GetMetadata("evidenceRefs"))))
        {
            if (!string.IsNullOrWhiteSpace(r))
            {
                refs.Add(r);
            }
        }

        var metadata = new Dictionary<string, string>(
            NodeMerger.MergeMetadata(existing.Metadata, incoming.Metadata, incomingWins: incoming.Confidence > existing.Confidence),
            StringComparer.Ordinal);
        if (refs.Count > 1)
        {
            metadata["evidenceRefs"] = string.Join(";", refs);
        }

        return existing with
        {
            Confidence = Math.Max(existing.Confidence, incoming.Confidence),
            EvidenceType = strongest.EvidenceType,
            EvidenceRef = strongest.EvidenceRef ?? existing.EvidenceRef ?? incoming.EvidenceRef,
            Metadata = metadata,
        };
    }

    private static IEnumerable<string> SplitRefs(string? joined)
        => string.IsNullOrEmpty(joined) ? [] : joined.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal static class LineageMerger
{
    public static FieldLineage Merge(FieldLineage existing, FieldLineage incoming)
    {
        var evidence = new SortedSet<string>(existing.EvidenceEdgeIds.Concat(incoming.EvidenceEdgeIds), StringComparer.Ordinal);
        return existing with
        {
            Expression = existing.Expression ?? incoming.Expression,
            Confidence = Math.Max(existing.Confidence, incoming.Confidence),
            EvidenceEdgeIds = evidence.ToList(),
        };
    }
}
