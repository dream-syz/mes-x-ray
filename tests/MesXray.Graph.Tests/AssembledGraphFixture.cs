using MesXray.Domain.Abstractions;
using MesXray.Domain.Evidence;
using MesXray.Graph.Linking;
using MesXray.Graph.Queries;
using MesXray.Graph.Store;
using MesXray.Runtime;
using MesXray.Runtime.Fixtures;
using MesXray.Runtime.Security;
using MesXray.Scanner.DotNet;
using MesXray.Scanner.Sql;
using MesXray.TestSupport;

namespace MesXray.Graph.Tests;

/// <summary>
/// Assembles the graph exactly like the API bootstrapper: scanners (FillGaps) -> linker -> curated files (Authoritative).
/// Also exposes a scan-only store so tests can measure what static analysis alone reproduces.
/// </summary>
public sealed class AssembledGraphFixture
{
    public AssembledGraphFixture()
    {
        ScannedOnly = new InMemoryGraphStore();
        var siteSettings = SiteSettings.ReadIfExists(Fixtures.SiteSettingsPath(), Fixtures.SiteSettingsSource);
        ScannedOnly.Merge(new DotNetScanner(new DotNetScannerOptions { SiteSettings = siteSettings }).ScanDirectory(Fixtures.DotNetSourcePath()).Snapshot);
        ScannedOnly.Merge(new SqlScanner().ScanDirectory(Fixtures.SqlSourcePath()).Snapshot);
        LinkReport = new GraphLinker(ScannedOnly).LinkDapperMappings();

        Store = new InMemoryGraphStore();
        Store.Merge(ScannedOnly.ToSnapshot());
        Store.Merge(GraphSnapshotFile.Read(Fixtures.GroundTruthPath()), MergePolicy.Authoritative);
        Store.Merge(GraphSnapshotFile.Read(Fixtures.ManualOverridesPath()), MergePolicy.Authoritative);

        Queries = new GraphQueryService(Store);
        Tracer = new FieldTraceService(Store, Queries);
        Impact = new ImpactService(Store);

        var options = new RuntimeOptions { FixtureRoot = Fixtures.RuntimePath() };
        Adapter = new FixtureRuntimeAdapter(options, new Redactor());
        DemoTrace = Adapter.PreloadedTraces().Single(t => t.TraceId == "trace-demo-001");
    }

    public InMemoryGraphStore ScannedOnly { get; }

    public LinkReport LinkReport { get; }

    public InMemoryGraphStore Store { get; }

    public GraphQueryService Queries { get; }

    public FieldTraceService Tracer { get; }

    public ImpactService Impact { get; }

    public FixtureRuntimeAdapter Adapter { get; }

    public RuntimeTrace DemoTrace { get; }

    public static IEnumerable<TraceHop> Flatten(TraceHop hop)
    {
        yield return hop;
        foreach (var source in hop.Sources.SelectMany(Flatten))
        {
            yield return source;
        }
    }
}
