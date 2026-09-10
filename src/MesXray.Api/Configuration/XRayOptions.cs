namespace MesXray.Api.Configuration;

/// <summary>How the graph is assembled at startup.</summary>
public enum GraphBuildMode
{
    /// <summary>Scan fixture sources, link, then overlay curated ground truth and manual overrides (default).</summary>
    ScanAndCurate,

    /// <summary>Scanners only - shows exactly what static analysis found.</summary>
    ScanOnly,

    /// <summary>Curated files only - no Roslyn/ScriptDom at startup.</summary>
    CuratedOnly,
}

public sealed class FixturesOptions
{
    /// <summary>Case fixture folder. Relative paths are resolved by walking up from the content root until they exist.</summary>
    public string Root { get; set; } = "fixtures/pick-order-details";
}

public sealed class GraphOptions
{
    public GraphBuildMode Mode { get; set; } = GraphBuildMode.ScanAndCurate;

    /// <summary>Optional path to write the assembled graph snapshot to (for diffing / offline inspection).</summary>
    public string? ExportPath { get; set; }
}

public sealed class XRayOptions
{
    public const string SectionName = "XRay";

    public FixturesOptions Fixtures { get; set; } = new();

    public GraphOptions Graph { get; set; } = new();

    public IList<string> CorsOrigins { get; } = ["http://localhost:5173", "http://127.0.0.1:5173"];
}
