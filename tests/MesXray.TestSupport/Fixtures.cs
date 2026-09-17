namespace MesXray.TestSupport;

/// <summary>Locates the repository's <c>fixtures/</c> folder from any test working directory.</summary>
public static class Fixtures
{
    private static readonly Lazy<string> Root = new(Locate);

    public static string RootPath => Root.Value;

    public static string CasePath(string caseId = "pick-order-details") => Path.Combine(RootPath, caseId);

    public static string DotNetSourcePath(string caseId = "pick-order-details") => Path.Combine(CasePath(caseId), "source", "dotnet");

    public static string SqlSourcePath(string caseId = "pick-order-details") => Path.Combine(CasePath(caseId), "source", "sql");

    /// <summary>Sanitised site configuration read by the .NET scanner (optional per case).</summary>
    public static string SiteSettingsPath(string caseId = "pick-order-details") => Path.Combine(CasePath(caseId), "source", "config", "site-settings.json");

    /// <summary>How the site settings file is cited in evidence references, matching the API bootstrapper.</summary>
    public const string SiteSettingsSource = "config/site-settings.json";

    public static string GroundTruthPath(string caseId = "pick-order-details") => Path.Combine(CasePath(caseId), "expected-graph", "ground-truth.json");

    public static string ManualOverridesPath(string caseId = "pick-order-details") => Path.Combine(CasePath(caseId), "expected-graph", "manual-overrides.json");

    public static string RuntimePath(string caseId = "pick-order-details") => Path.Combine(CasePath(caseId), "runtime");

    public static string CaseDefinitionPath(string caseId = "pick-order-details") => Path.Combine(CasePath(caseId), "case.json");

    private static string Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "MesXray.slnx")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository 'fixtures' folder from " + AppContext.BaseDirectory);
    }
}
