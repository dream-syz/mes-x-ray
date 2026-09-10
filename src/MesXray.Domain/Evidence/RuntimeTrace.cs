using System.Text.Json;

namespace MesXray.Domain.Evidence;

/// <summary>The result of one Live Trace: the (redacted) response plus every evidence item bound to graph nodes.</summary>
public sealed record RuntimeTrace
{
    public required string TraceId { get; init; }

    public required string EntityType { get; init; }

    public required string EntityKey { get; init; }

    public required string Environment { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    /// <summary>Redacted API response as returned to the UI.</summary>
    public JsonElement? Response { get; init; }

    public IReadOnlyList<RuntimeEvidence> Evidence { get; init; } = [];

    /// <summary>Things the trace could not observe (e.g. UDF internals), stated explicitly.</summary>
    public IReadOnlyList<string> Unknowns { get; init; } = [];

    /// <summary>Fixture id the trace was served from, when applicable.</summary>
    public string? FixtureId { get; init; }
}
