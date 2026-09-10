using System.Text.Json;
using MesXray.Domain.Serialization;

namespace MesXray.Graph.Cases;

/// <summary>A known gap declared up-front so the UI and the AI never have to guess.</summary>
public sealed record KnownGap(string NodeId, string Reason, string Priority);

/// <summary>
/// Describes one X-Ray case (the first is Web Visual Picking - Pick Order Details): entry points, the fields that
/// matter, the parameters to showcase and the fixtures that back Live Trace. Loaded from <c>fixtures/{case}/case.json</c>.
/// </summary>
public sealed record CaseDefinition
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    /// <summary>Root of the architecture map, normally the page node.</summary>
    public required string RootNodeId { get; init; }

    public required string ApiNodeId { get; init; }

    public string? ResponseModelNodeId { get; init; }

    /// <summary>Fields the demo focuses on (json node ids).</summary>
    public IReadOnlyList<string> KeyFields { get; init; } = [];

    public IReadOnlyList<string> SystemParameters { get; init; } = [];

    public string? DefaultFixtureId { get; init; }

    public string? DefaultTraceId { get; init; }

    public string? DefaultScope { get; init; }

    public IReadOnlyList<KnownGap> KnownGaps { get; init; } = [];

    public static CaseDefinition Read(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<CaseDefinition>(stream, XRayJson.Options)
               ?? throw new InvalidDataException($"'{path}' does not contain a case definition.");
    }
}
