using System.Text.Json;

namespace MesXray.Runtime.Fixtures;

/// <summary>Result rows of one stored procedure call captured in a fixture.</summary>
public sealed class ProcedureResultFixture
{
    public int? DurationMs { get; set; }

    /// <summary>Column whose value identifies the row (e.g. <c>MaterialNumber</c>). Used as the evidence scope.</summary>
    public string? RowKey { get; set; }

    public List<Dictionary<string, JsonElement>> Rows { get; set; } = [];
}

/// <summary>
/// Shape of <c>fixtures/&lt;case&gt;/runtime/*.json</c>: one sanitised, offline observation of a pick order request.
/// Fixtures are committed to Git and therefore may never contain real hosts, credentials or personal data.
/// </summary>
public sealed class RuntimeFixture
{
    public required string FixtureId { get; set; }

    public required string EntityType { get; set; }

    public required string EntityKey { get; set; }

    public required string Environment { get; set; }

    public DateTimeOffset ObservedAt { get; set; }

    public string? TraceId { get; set; }

    public JsonElement? Request { get; set; }

    public Dictionary<string, string> SystemParameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public JsonElement? Response { get; set; }

    public Dictionary<string, ProcedureResultFixture> ProcedureResults { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Unknowns { get; set; } = [];
}
