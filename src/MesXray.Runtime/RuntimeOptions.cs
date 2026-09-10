namespace MesXray.Runtime;

/// <summary>
/// Guard rails for the Runtime Adapter (design §10): allowed environments, row and time limits. Connection strings
/// never live here - a real adapter reads them from a secret manager / environment variables.
/// </summary>
public sealed class RuntimeOptions
{
    public const string SectionName = "XRay:Runtime";

    /// <summary>Directory containing sanitised runtime fixtures (<c>*.json</c>).</summary>
    public string FixtureRoot { get; set; } = string.Empty;

    /// <summary>Environments the adapter may serve data from. Production is never allowed in the POC.</summary>
    public IList<string> AllowedEnvironments { get; } = ["FIXTURE", "TEST", "UAT"];

    /// <summary>Maximum rows returned per procedure result; extra rows are dropped and reported as an unknown.</summary>
    public int MaxRows { get; set; } = 200;

    /// <summary>Per-operation timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Maximum length of a single tool argument.</summary>
    public int MaxArgumentLength { get; set; } = 100;
}
