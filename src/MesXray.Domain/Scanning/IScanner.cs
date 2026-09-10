using MesXray.Domain.Graph;

namespace MesXray.Domain.Scanning;

/// <summary>A deterministic static scanner producing a graph snapshot from source files.</summary>
public interface IScanner
{
    /// <summary>Stable scanner name used in <see cref="Node.ScanVersion"/>, e.g. <c>dotnet-scanner</c>.</summary>
    string Name { get; }

    string Version { get; }

    ScanResult Scan(ScanRequest request);
}

/// <summary>Files to scan. Paths in the resulting snapshot are made relative to <see cref="RootPath"/>.</summary>
public sealed record ScanRequest(string RootPath, IReadOnlyList<string> Files)
{
    public static ScanRequest FromDirectory(string rootPath, string searchPattern)
    {
        var files = Directory.EnumerateFiles(rootPath, searchPattern, SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        return new ScanRequest(rootPath, files);
    }
}

public enum ScanDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>Something the scanner wants a human to know: unresolved symbol, unsupported syntax, parse error...</summary>
public sealed record ScanDiagnostic(ScanDiagnosticSeverity Severity, string Message, string? File = null, int? Line = null);

public sealed record ScanResult(GraphSnapshot Snapshot, IReadOnlyList<ScanDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == ScanDiagnosticSeverity.Error);
}
