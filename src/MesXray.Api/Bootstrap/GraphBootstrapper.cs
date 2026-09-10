using System.Diagnostics;
using MesXray.Api.Configuration;
using MesXray.Domain.Abstractions;
using MesXray.Domain.Graph;
using MesXray.Domain.Scanning;
using MesXray.Graph.Cases;
using MesXray.Graph.Linking;
using MesXray.Graph.Store;
using MesXray.Scanner.DotNet;
using MesXray.Scanner.Sql;

namespace MesXray.Api.Bootstrap;

/// <summary>One step of the graph build, for the health/case endpoints.</summary>
public sealed record BuildStep(string Name, int Nodes, int Edges, int Lineages, long ElapsedMs, IReadOnlyList<string> Diagnostics);

/// <summary>What the graph was built from and how it went.</summary>
public sealed record GraphBuildReport(
    GraphBuildMode Mode,
    string FixtureRoot,
    DateTimeOffset BuiltAt,
    IReadOnlyList<BuildStep> Steps,
    int Nodes,
    int Edges,
    int Lineages,
    int UnknownNodes,
    int PendingNodes,
    LinkReport? Linker);

/// <summary>
/// Assembles the evidence graph at startup: scanners fill gaps, the linker joins .NET and SQL, curated files are
/// authoritative. Deterministic for a given fixture folder.
/// </summary>
public sealed class GraphBootstrapper
{
    private readonly IGraphStore _store;
    private readonly XRayOptions _options;
    private readonly ILogger<GraphBootstrapper> _logger;

    public GraphBootstrapper(IGraphStore store, XRayOptions options, ILogger<GraphBootstrapper> logger)
    {
        _store = store;
        _options = options;
        _logger = logger;
    }

    public GraphBuildReport? Report { get; private set; }

    public CaseDefinition? Case { get; private set; }

    public string FixtureRoot { get; private set; } = string.Empty;

    public GraphBuildReport Build()
    {
        FixtureRoot = FixturePaths.Resolve(_options.Fixtures.Root);
        var caseFile = Path.Combine(FixtureRoot, "case.json");
        Case = File.Exists(caseFile) ? CaseDefinition.Read(caseFile) : null;

        var steps = new List<BuildStep>();
        LinkReport? linkReport = null;
        _store.Load(new GraphSnapshot { Source = "bootstrap", ScanVersion = "0" });

        if (_options.Graph.Mode != GraphBuildMode.CuratedOnly)
        {
            var dotnetRoot = Path.Combine(FixtureRoot, "source", "dotnet");
            if (Directory.Exists(dotnetRoot))
            {
                steps.Add(Time("dotnet-scanner", () => Merge(new DotNetScanner().ScanDirectory(dotnetRoot))));
            }

            var sqlRoot = Path.Combine(FixtureRoot, "source", "sql");
            if (Directory.Exists(sqlRoot))
            {
                steps.Add(Time("sql-scanner", () => Merge(new SqlScanner().ScanDirectory(sqlRoot))));
            }

            steps.Add(Time("linker", () =>
            {
                linkReport = new GraphLinker(_store).LinkDapperMappings();
                return [$"mapped {linkReport.MappedColumns} column(s); {linkReport.UnmappedFields.Count} unmapped field(s); {linkReport.UnmappedColumns.Count} unmapped column(s)"];
            }));
        }

        if (_options.Graph.Mode != GraphBuildMode.ScanOnly)
        {
            var groundTruth = Path.Combine(FixtureRoot, "expected-graph", "ground-truth.json");
            if (File.Exists(groundTruth))
            {
                steps.Add(Time("ground-truth", () =>
                {
                    _store.Merge(GraphSnapshotFile.Read(groundTruth), MergePolicy.Authoritative);
                    return [];
                }));
            }

            var overrides = Path.Combine(FixtureRoot, "expected-graph", "manual-overrides.json");
            if (File.Exists(overrides))
            {
                steps.Add(Time("manual-overrides", () =>
                {
                    _store.Merge(GraphSnapshotFile.Read(overrides), MergePolicy.Authoritative);
                    return [];
                }));
            }
        }

        if (!string.IsNullOrWhiteSpace(_options.Graph.ExportPath))
        {
            GraphSnapshotFile.Write(_store.ToSnapshot(), _options.Graph.ExportPath);
        }

        Report = new GraphBuildReport(
            _options.Graph.Mode,
            FixtureRoot,
            DateTimeOffset.UtcNow,
            steps,
            _store.Nodes.Count,
            _store.Edges.Count,
            _store.Lineages.Count,
            _store.Nodes.Count(n => n.Status == NodeStatus.Unknown),
            _store.Nodes.Count(n => n.Status == NodeStatus.Pending),
            linkReport);

        _logger.LogInformation("Graph built ({Mode}): {Nodes} nodes, {Edges} edges, {Lineages} lineage records, {Unknown} unknown, {Pending} pending.",
            Report.Mode, Report.Nodes, Report.Edges, Report.Lineages, Report.UnknownNodes, Report.PendingNodes);

        return Report;
    }

    private IReadOnlyList<string> Merge(ScanResult result)
    {
        _store.Merge(result.Snapshot, MergePolicy.FillGaps);
        return result.Diagnostics
            .Where(d => d.Severity != ScanDiagnosticSeverity.Info)
            .Select(d => $"[{d.Severity}] {d.File}:{d.Line} {d.Message}")
            .ToList();
    }

    private BuildStep Time(string name, Func<IReadOnlyList<string>> action)
    {
        var watch = Stopwatch.StartNew();
        var diagnostics = action();
        watch.Stop();
        return new BuildStep(name, _store.Nodes.Count, _store.Edges.Count, _store.Lineages.Count, watch.ElapsedMilliseconds, diagnostics);
    }
}

/// <summary>Resolves the fixture folder relative to the current directory or any ancestor (repo root during development).</summary>
public static class FixturePaths
{
    public static string Resolve(string configured)
    {
        if (Path.IsPathRooted(configured))
        {
            return configured;
        }

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, configured);
                if (Directory.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }

                dir = dir.Parent;
            }
        }

        throw new DirectoryNotFoundException($"Fixture folder '{configured}' was not found relative to '{Directory.GetCurrentDirectory()}' or its parents. Set XRay:Fixtures:Root.");
    }
}
