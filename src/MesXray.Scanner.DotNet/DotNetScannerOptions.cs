namespace MesXray.Scanner.DotNet;

/// <summary>Tunables for the .NET scanner. Defaults reproduce a standard SDK-style project.</summary>
public sealed class DotNetScannerOptions
{
    /// <summary>
    /// Namespaces imported as <c>global using</c> before analysis. Mirrors the SDK's ImplicitUsings so that
    /// <c>Task&lt;T&gt;</c>, <c>List&lt;T&gt;</c> and friends bind even though the scanned files never import them.
    /// </summary>
    public IReadOnlyList<string> ImplicitUsings { get; init; } =
    [
        "System",
        "System.Collections.Generic",
        "System.IO",
        "System.Linq",
        "System.Net.Http",
        "System.Threading",
        "System.Threading.Tasks",
    ];

    /// <summary>
    /// Configuration values of the site under study. A Dapper call whose procedure name is an options property
    /// (<c>_options.StorageBinProcedure</c>) resolves to the configured procedure with Configuration evidence;
    /// without a setting the procedure stays an Unknown placeholder.
    /// </summary>
    public SiteSettings SiteSettings { get; init; } = SiteSettings.Empty;

    public static DotNetScannerOptions Default { get; } = new();
}
