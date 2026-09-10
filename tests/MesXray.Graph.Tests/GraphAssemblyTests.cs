using MesXray.Domain.Abstractions;
using MesXray.Domain.Graph;
using MesXray.Graph.Store;
using MesXray.TestSupport;

namespace MesXray.Graph.Tests;

/// <summary>Scanner output + linker must reproduce the curated expected graph; merge policies must protect curation.</summary>
public sealed class GraphAssemblyTests : IClassFixture<AssembledGraphFixture>
{
    private readonly AssembledGraphFixture _fx;

    public GraphAssemblyTests(AssembledGraphFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public void Scanners_plus_linker_reproduce_every_ground_truth_edge_except_manual_web_edges()
    {
        var expected = GroundTruth.Load();
        var missing = GroundTruth.MissingEdges(expected, _fx.ScannedOnly.ToSnapshot(), [string.Empty]);

        // The Web VP page is not scanned (no front-end scanner in the POC): its edge is curated by design.
        var unexpected = missing.Where(e => !e.FromNodeId.StartsWith("page:", StringComparison.Ordinal)).ToList();
        Assert.True(unexpected.Count == 0, "Edges only present in ground truth:\n" + GroundTruth.Describe(unexpected));
        Assert.Contains(missing, e => e.FromNodeId == "page:WebVP.PickOrderDetails" && e.RelationType == RelationType.Calls);
    }

    [Fact]
    public void Ground_truth_lineage_records_are_reproduced()
    {
        var expected = GroundTruth.Load();
        var actual = _fx.ScannedOnly.Lineages.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        var missing = expected.Lineages.Where(l => !actual.Contains(l.Id)).Select(l => l.Id).ToList();
        Assert.True(missing.Count == 0, "Lineage only in ground truth:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Linker_maps_every_result_column_to_a_model_field_by_name()
    {
        Assert.True(_fx.LinkReport.MappedColumns >= 15, $"only {_fx.LinkReport.MappedColumns} mapped");
        Assert.Empty(_fx.LinkReport.UnmappedFields);
        Assert.Empty(_fx.LinkReport.UnmappedColumns);

        var edge = _fx.Store.OutEdges("spcol:dbo.AP_Pick_GetPickOrderRows.AvailableQuantity").Single(e => e.RelationType == RelationType.MapsTo);
        Assert.Equal("field:CWPPickOrderRow.AvailableQuantity", edge.ToNodeId);
        Assert.Equal(EvidenceType.Linker, edge.EvidenceType);
    }

    [Fact]
    public void Curated_statuses_and_descriptions_survive_the_merge()
    {
        var udf = _fx.Store.FindNode("udf:dbo.AF_Pick_GetAvailableQuantity")!;
        Assert.Equal(NodeStatus.Unknown, udf.Status);
        Assert.False(string.IsNullOrEmpty(udf.Description));

        var pending = _fx.Store.FindNode("method:PickOrderService.GetStorageBin")!;
        Assert.Equal(NodeStatus.Pending, pending.Status);

        var parameter = _fx.Store.FindNode("param:WMS_Enabled")!;
        Assert.Equal(NodeStatus.Known, parameter.Status);
        Assert.Contains("AvailableQuantity", parameter.Description, StringComparison.Ordinal);
        Assert.Equal("@WMS_Enabled", _fx.Store.OutEdges("sp:dbo.AP_Pick_GetPickOrderRows").Single(e => e.ToNodeId == "param:WMS_Enabled").GetMetadata("variable"));
    }

    [Fact]
    public void FillGaps_upgrades_placeholders_but_never_overrides_curated_text()
    {
        var store = new InMemoryGraphStore();
        store.Load(new GraphSnapshot
        {
            Source = "curated",
            ScanVersion = "1",
            Nodes =
            [
                new Node { Id = "sp:dbo.X", Type = NodeType.StoredProcedure, Name = "X", Layer = Layer.Data, Status = NodeStatus.Unknown, Description = "curated description" },
            ],
        });

        store.Merge(new GraphSnapshot
        {
            Source = "scanner",
            ScanVersion = "2",
            Nodes =
            [
                new Node { Id = "sp:dbo.X", Type = NodeType.StoredProcedure, Name = "X", Layer = Layer.Data, Status = NodeStatus.Known, Description = "scanner description", Metadata = new Dictionary<string, string> { ["parameters"] = "@a" } },
            ],
        });

        var node = store.FindNode("sp:dbo.X")!;
        Assert.Equal(NodeStatus.Known, node.Status);
        Assert.Equal("curated description", node.Description);
        Assert.Equal("@a", node.GetMetadata("parameters"));

        store.Merge(new GraphSnapshot
        {
            Source = "override",
            ScanVersion = "3",
            Nodes = [new Node { Id = "sp:dbo.X", Type = NodeType.StoredProcedure, Name = "X", Layer = Layer.Data, Status = NodeStatus.Pending, Description = "override" }],
        }, MergePolicy.Authoritative);

        node = store.FindNode("sp:dbo.X")!;
        Assert.Equal(NodeStatus.Pending, node.Status);
        Assert.Equal("override", node.Description);
    }

    [Fact]
    public void Referenced_but_undefined_nodes_become_unknown_placeholders()
    {
        var store = new InMemoryGraphStore();
        store.Merge(new GraphSnapshot
        {
            Source = "scanner",
            ScanVersion = "1",
            Edges = [new Edge { FromNodeId = "method:A.B", ToNodeId = "sp:dbo.Missing", RelationType = RelationType.ExecutesSp }],
        });

        var placeholder = store.FindNode("sp:dbo.Missing")!;
        Assert.Equal(NodeStatus.Unknown, placeholder.Status);
        Assert.Equal(NodeType.StoredProcedure, placeholder.Type);
        Assert.Equal(Layer.Data, placeholder.Layer);
    }

    [Fact]
    public void Snapshot_round_trips_through_json()
    {
        var snapshot = _fx.Store.ToSnapshot();
        var json = GraphSnapshotFile.Serialize(snapshot);
        var path = Path.Combine(Path.GetTempPath(), $"xray-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            var back = GraphSnapshotFile.Read(path);
            Assert.Equal(snapshot.Nodes.Count, back.Nodes.Count);
            Assert.Equal(snapshot.Edges.Select(e => e.Id), back.Edges.Select(e => e.Id));
            Assert.Equal(snapshot.Lineages.Select(l => l.Id), back.Lineages.Select(l => l.Id));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
