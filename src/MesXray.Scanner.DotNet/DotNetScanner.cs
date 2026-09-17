using MesXray.Domain.Scanning;
using MesXray.Scanner.DotNet.Analysis;
using MesXray.Scanner.DotNet.Rules;
using Microsoft.CodeAnalysis;

namespace MesXray.Scanner.DotNet;

/// <summary>
/// Roslyn-based static scanner for the .NET side of a case. Deterministic: same input, same snapshot.
/// Facts are emitted with confidence 1.0 when the semantic model binds the symbol, lower when a syntax heuristic
/// was needed (interface dispatch, Dapper without CommandType, computed procedure names).
/// </summary>
public sealed class DotNetScanner : IScanner
{
    public const string ScannerName = "dotnet-scanner";
    public const string ScannerVersion = "0.1.0";

    private readonly DotNetScannerOptions _options;

    public DotNetScanner()
        : this(DotNetScannerOptions.Default)
    {
    }

    public DotNetScanner(DotNetScannerOptions options)
    {
        _options = options;
    }

    public string Name => ScannerName;

    public string Version => ScannerVersion;

    public ScanResult Scan(ScanRequest request)
    {
        var builder = new SnapshotBuilder(ScannerName, $"{ScannerName}/{ScannerVersion}");
        var trees = new List<SyntaxTree>();

        if (_options.ImplicitUsings.Count > 0)
        {
            var globalUsings = string.Join('\n', _options.ImplicitUsings.Select(ns => $"global using global::{ns};"));
            trees.Add(CompilationFactory.Parse(globalUsings, "<implicit-usings>"));
        }

        foreach (var file in request.Files)
        {
            try
            {
                trees.Add(CompilationFactory.Parse(File.ReadAllText(file), file));
            }
            catch (Exception ex)
            {
                builder.Report(ScanDiagnosticSeverity.Error, $"Could not read '{file}': {ex.Message}", file);
            }
        }

        if (!trees.Any(t => t.FilePath != "<implicit-usings>"))
        {
            builder.Report(ScanDiagnosticSeverity.Warning, "No C# files to scan.");
            return builder.Build();
        }

        var compilation = CompilationFactory.Create("MesXray.Scan", trees);
        foreach (var diagnostic in compilation.GetParseDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            var span = diagnostic.Location.GetLineSpan();
            builder.Report(ScanDiagnosticSeverity.Error, diagnostic.GetMessage(), span.Path, span.StartLinePosition.Line + 1);
        }

        var context = new ScanContext(compilation, TypeIndex.Build(compilation), builder, request.RootPath, _options.SiteSettings);

        // Declarations first so that body rules can link to fully defined nodes.
        new ModelRule(context).Apply();
        new MethodRule(context).Apply();
        new EndpointRule(context).Apply();
        new MethodBodyRule(context).Apply();

        builder.Report(ScanDiagnosticSeverity.Info, $"Scanned {request.Files.Count} C# file(s).");
        return builder.Build();
    }

    /// <summary>Convenience for scanning a directory of .cs files.</summary>
    public ScanResult ScanDirectory(string rootPath) => Scan(ScanRequest.FromDirectory(rootPath, "*.cs"));
}
