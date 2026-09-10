using System.Text.Json;
using MesXray.Domain.Graph;
using MesXray.Domain.Serialization;

namespace MesXray.TestSupport;

/// <summary>Loads the hand-curated expected graph and compares scanner output against it.</summary>
public static class GroundTruth
{
    public static GraphSnapshot Load(string caseId = "pick-order-details")
        => Read(Fixtures.GroundTruthPath(caseId));

    public static GraphSnapshot LoadManualOverrides(string caseId = "pick-order-details")
        => Read(Fixtures.ManualOverridesPath(caseId));

    /// <summary>
    /// Edges of <paramref name="expected"/> whose source node id starts with one of <paramref name="fromPrefixes"/>
    /// and whose relation is in <paramref name="relations"/> (or any relation when null) that are missing from <paramref name="actual"/>.
    /// </summary>
    public static IReadOnlyList<Edge> MissingEdges(
        GraphSnapshot expected,
        GraphSnapshot actual,
        IReadOnlyCollection<string> fromPrefixes,
        IReadOnlyCollection<RelationType>? relations = null)
    {
        var actualIds = actual.Edges.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        return expected.Edges
            .Where(e => fromPrefixes.Any(p => e.FromNodeId.StartsWith(p, StringComparison.Ordinal)))
            .Where(e => relations is null || relations.Contains(e.RelationType))
            .Where(e => !actualIds.Contains(e.Id))
            .ToList();
    }

    public static string Describe(IEnumerable<Edge> edges)
        => string.Join("\n", edges.Select(e => $"  {e.FromNodeId} -{e.RelationType}-> {e.ToNodeId}"));

    private static GraphSnapshot Read(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<GraphSnapshot>(stream, XRayJson.Options)
               ?? throw new InvalidDataException($"Empty snapshot: {path}");
    }
}
