using MesXray.Domain.Graph;
using MesXray.Domain.Scanning;
using MesXray.TestSupport;

namespace MesXray.Scanner.Sql.Tests;

/// <summary>Scans the sanitised T-SQL fixture once and checks the facts required by the design document (§4.2, AC-02).</summary>
public sealed class SqlScannerTests : IClassFixture<SqlScannerTests.ScanFixture>
{
    private const string Rows = "dbo.AP_Pick_GetPickOrderRows";
    private const string Bins = "dbo.AP_Pick_GetPickStorageBin";
    private const string Wagon = "dbo.AP_Pick_GetDestinationWagon";

    private readonly ScanResult _result;

    public SqlScannerTests(ScanFixture fixture)
    {
        _result = fixture.Result;
    }

    public sealed class ScanFixture
    {
        public ScanFixture()
        {
            Result = new SqlScanner().ScanDirectory(Fixtures.SqlSourcePath());
        }

        public ScanResult Result { get; }
    }

    [Fact]
    public void Scan_has_no_errors_and_is_deterministic()
    {
        Assert.False(_result.HasErrors, string.Join("\n", _result.Diagnostics.Where(d => d.Severity == ScanDiagnosticSeverity.Error).Select(d => d.Message)));

        var again = new SqlScanner().ScanDirectory(Fixtures.SqlSourcePath());
        Assert.Equal(_result.Snapshot.Nodes.Select(n => n.Id), again.Snapshot.Nodes.Select(n => n.Id));
        Assert.Equal(_result.Snapshot.Edges.Select(e => e.Id), again.Snapshot.Edges.Select(e => e.Id));
        Assert.Equal(_result.Snapshot.Lineages.Select(l => l.Id), again.Snapshot.Lineages.Select(l => l.Id));
    }

    [Fact]
    public void Procedures_and_functions_are_defined_with_parameters_and_source()
    {
        var sp = Node($"sp:{Rows}");
        Assert.Equal(NodeStatus.Known, sp.Status);
        Assert.Equal(NodeType.StoredProcedure, sp.Type);
        Assert.Equal("dbo.AP_Pick_GetPickOrderRows", sp.QualifiedName);
        Assert.Contains("@Facility NVARCHAR(50)", sp.GetMetadata("parameters"), StringComparison.Ordinal);
        Assert.Equal("1", sp.GetMetadata("resultSets"));
        Assert.Equal("dbo.AP_Pick_GetPickOrderRows.sql", sp.Source!.Path);

        var udf = Node("udf:dbo.AF_GetTextTranslation");
        Assert.Equal(NodeStatus.Known, udf.Status);
        Assert.Equal("scalarFunction", udf.GetMetadata("objectType"));
        Assert.Equal("NVARCHAR(500)", udf.GetMetadata("returnType"));
    }

    [Fact]
    public void Function_without_definition_stays_unknown()
    {
        var udf = Node("udf:dbo.AF_Pick_GetAvailableQuantity");
        Assert.Equal(NodeStatus.Unknown, udf.Status);
        AssertEdge($"sp:{Rows}", RelationType.CallsFunction, "udf:dbo.AF_Pick_GetAvailableQuantity");
    }

    [Fact]
    public void Table_reads_are_collected_from_every_from_clause()
    {
        foreach (var table in new[] { "AT_PICK_PRINT_QUEUE", "AT_PICK_PRINT_QUEUE_DETAIL", "AT_PICK_PRODUCT", "WAREHOUSE_LOCATION", "REPLENISH_STRATEGY", "CONTENT", "PRODUCT", "PRODUCT_ALIAS" })
        {
            AssertEdge($"sp:{Rows}", RelationType.Reads, $"table:dbo.{table}");
        }

        AssertEdge("udf:dbo.AF_GetSystemParameterValue", RelationType.Reads, "table:dbo.SYSTEM_PARAMETER");
        Assert.DoesNotContain(_result.Snapshot.Edges, e => e.RelationType == RelationType.Reads && e.ToNodeId.Contains('#', StringComparison.Ordinal));
    }

    [Fact]
    public void System_parameters_are_bound_to_their_local_variables()
    {
        var edge = AssertEdge($"sp:{Rows}", RelationType.UsesParameter, "param:WMS_Enabled");
        Assert.Equal("@WMS_Enabled", edge.GetMetadata("variable"));
        Assert.Contains("declare", edge.GetMetadata("usage"), StringComparison.Ordinal);
        Assert.Contains("branch", edge.GetMetadata("usage"), StringComparison.Ordinal);

        var filter = AssertEdge($"sp:{Rows}", RelationType.UsesParameter, "param:PickOrder_Type");
        Assert.Equal("@PickOrder_Type", filter.GetMetadata("variable"));
        Assert.Contains("filter", filter.GetMetadata("usage"), StringComparison.Ordinal);

        AssertEdge($"sp:{Rows}", RelationType.UsesParameter, "param:PickDocumentClassForProductImage");
        Assert.Equal(Layer.Config, Node("param:WMS_Enabled").Layer);
    }

    [Fact]
    public void AC02_case_on_WMS_Enabled_yields_two_conditional_branches()
    {
        var expr = $"expr:{Rows}.MainResults.AvailableQuantity";
        Assert.Equal("case", Node(expr).GetMetadata("kind"));
        AssertEdge(expr, RelationType.Produces, $"ctecol:{Rows}.MainResults.AvailableQuantity");

        var control = AssertEdge(expr, RelationType.ControlledBy, "param:WMS_Enabled");
        Assert.Equal("@WMS_Enabled", control.GetMetadata("variable"));

        var sumBranch = AssertEdge(expr, RelationType.DerivedFrom, "column:dbo.AT_PICK_PRINT_QUEUE_DETAIL.Quantity");
        Assert.Equal("@WMS_Enabled = 0", sumBranch.GetMetadata("condition"));

        var udfBranch = AssertEdge(expr, RelationType.ComputedBy, "udf:dbo.AF_Pick_GetAvailableQuantity");
        Assert.Equal("@WMS_Enabled = 1", udfBranch.GetMetadata("condition"));
        Assert.Equal("@WMS_Enabled = 1", AssertEdge(expr, RelationType.DerivedFrom, "column:dbo.AT_PICK_PRODUCT.WarehouseLocationID").GetMetadata("condition"));

        var lineages = _result.Snapshot.Lineages.Where(l => l.OutputFieldId == $"ctecol:{Rows}.MainResults.AvailableQuantity").ToList();
        var conditions = lineages.Select(l => l.Condition).Where(c => c is not null).Distinct().OrderBy(c => c).ToList();
        Assert.Equal(["@WMS_Enabled = 0", "@WMS_Enabled = 1"], conditions);
        Assert.Contains(lineages, l => l.SourceFieldId == "column:dbo.AT_PICK_PRINT_QUEUE_DETAIL.Quantity" && l.TransformType == TransformType.Conditional && l.Expression!.Contains("* 10", StringComparison.Ordinal));
        Assert.Contains(lineages, l => l.SourceFieldId == "param:WMS_Enabled" && l.Condition is null);
    }

    [Fact]
    public void Aliases_and_aggregates_hop_through_ctes_to_the_result_set()
    {
        AssertEdge($"sp:{Rows}", RelationType.Contains, $"cte:{Rows}.MainResults");
        Assert.Equal("cte", Node($"cte:{Rows}.MaterialTotals").GetMetadata("kind"));

        AssertEdge($"spcol:{Rows}.AvailableQuantity", RelationType.AliasOf, $"ctecol:{Rows}.MaterialTotals.TotalAvailableQuantity");
        AssertEdge($"expr:{Rows}.MaterialTotals.TotalAvailableQuantity", RelationType.DerivedFrom, $"ctecol:{Rows}.MainResults.AvailableQuantity");
        AssertEdge($"spcol:{Rows}.MaterialNumber", RelationType.AliasOf, "column:dbo.PRODUCT.ProductNo");
        AssertEdge($"ctecol:{Rows}.MainResults.ProductID", RelationType.AliasOf, "column:dbo.AT_PICK_PRODUCT.ProductID");

        var aggregate = Assert.Single(_result.Snapshot.Lineages, l => l.OutputFieldId == $"ctecol:{Rows}.MaterialTotals.TotalAvailableQuantity");
        Assert.Equal(TransformType.Aggregate, aggregate.TransformType);
        Assert.Equal("SUM(MR.AvailableQuantity)", aggregate.Expression);
    }

    [Fact]
    public void Scalar_udf_calls_in_the_result_set_become_computed_by_edges()
    {
        var expr = $"expr:{Rows}.$.MaterialImageUrl";
        AssertEdge(expr, RelationType.Produces, $"spcol:{Rows}.MaterialImageUrl");
        AssertEdge(expr, RelationType.ComputedBy, "udf:dbo.AF_Pick_GetProductImageURL");
        AssertEdge(expr, RelationType.DerivedFrom, "column:dbo.PRODUCT.ProductID");
        Assert.Equal("@ImageDocClass", AssertEdge(expr, RelationType.DerivedFrom, "param:PickDocumentClassForProductImage").GetMetadata("variable"));
        AssertEdge($"expr:{Rows}.$.MaterialDescription", RelationType.ComputedBy, "udf:dbo.AF_GetTextTranslation");
    }

    [Fact]
    public void Temp_tables_created_inserted_and_selected_into_are_tracked_column_by_column()
    {
        Assert.Equal("tempTable", Node($"tmp:{Bins}.#LocationDistinct").GetMetadata("kind"));
        AssertEdge($"sp:{Bins}", RelationType.Contains, $"tmp:{Bins}.#LocationDistinct");
        AssertEdge($"tmp:{Bins}.#LocationDistinct", RelationType.Contains, $"tmpcol:{Bins}.#LocationDistinct.StorageBin");

        // INSERT INTO #LocationDistinct (...) SELECT CASE WHEN @WL_Description_Replacement = 1 THEN udf(...) ELSE WL.Location END
        var expr = $"expr:{Bins}.#LocationDistinct.StorageBin";
        AssertEdge(expr, RelationType.Produces, $"tmpcol:{Bins}.#LocationDistinct.StorageBin");
        AssertEdge(expr, RelationType.ControlledBy, "param:WL_Description_Replacement");
        Assert.Equal("@WL_Description_Replacement = 1", AssertEdge(expr, RelationType.ComputedBy, "udf:dbo.AF_GetTextTranslation").GetMetadata("condition"));
        Assert.Equal("@WL_Description_Replacement = 1", AssertEdge(expr, RelationType.DerivedFrom, "column:dbo.WAREHOUSE_LOCATION.TextID").GetMetadata("condition"));
        Assert.Equal("ELSE", AssertEdge(expr, RelationType.DerivedFrom, "column:dbo.WAREHOUSE_LOCATION.Location").GetMetadata("condition"));
        AssertEdge($"expr:{Bins}.#LocationDistinct.FirstInventoryOn", RelationType.DerivedFrom, "column:dbo.INVENTORY2.CreatedOn");

        // SELECT ... INTO #LocationList FROM #LocationDistinct LD
        AssertEdge($"tmpcol:{Bins}.#LocationList.StorageBin", RelationType.AliasOf, $"tmpcol:{Bins}.#LocationDistinct.StorageBin");
        var window = AssertEdge($"expr:{Bins}.#LocationList.FifoRank", RelationType.DerivedFrom, $"tmpcol:{Bins}.#LocationDistinct.FirstInventoryOn");
        Assert.Equal("order", window.GetMetadata("role"));
        Assert.Equal("window", Node($"expr:{Bins}.#LocationList.FifoRank").GetMetadata("kind"));
    }

    [Fact]
    public void Outer_apply_with_string_agg_within_group_is_a_derived_table_hop()
    {
        Assert.Equal("derivedTable", Node($"cte:{Bins}.Bins").GetMetadata("kind"));
        AssertEdge($"spcol:{Bins}.StorageBin", RelationType.AliasOf, $"ctecol:{Bins}.Bins.StorageBin");

        var expr = $"expr:{Bins}.Bins.StorageBin";
        Assert.Equal("aggregate", Node(expr).GetMetadata("kind"));
        Assert.Equal("value", AssertEdge(expr, RelationType.DerivedFrom, $"tmpcol:{Bins}.#LocationList.StorageBin").GetMetadata("role"));
        Assert.Equal("order", AssertEdge(expr, RelationType.DerivedFrom, $"tmpcol:{Bins}.#LocationList.FifoRank").GetMetadata("role"));

        AssertEdge($"expr:{Bins}.$.AllocatedQuantity", RelationType.DerivedFrom, "column:dbo.AT_PICK_PRINT_QUEUE_DETAIL.Quantity");
        AssertEdge($"expr:{Bins}.$.AllocatedQuantity", RelationType.DerivedFrom, "column:dbo.AT_PICK_PRINT_QUEUE_DETAIL.PickedQuantity");
    }

    [Fact]
    public void Literal_columns_have_no_source_but_are_not_unknown()
    {
        AssertEdge($"expr:{Wagon}.$.PhysicalWagonId", RelationType.Produces, $"spcol:{Wagon}.PhysicalWagonId");
        var lineage = Assert.Single(_result.Snapshot.Lineages, l => l.OutputFieldId == $"spcol:{Wagon}.PhysicalWagonId");
        Assert.Null(lineage.SourceFieldId);
        Assert.Equal(TransformType.Literal, lineage.TransformType);
        Assert.Equal("''", lineage.Expression);
        AssertEdge($"spcol:{Wagon}.NumberOfBoxes", RelationType.AliasOf, "column:dbo.AT_PICK_GROUP.NoOfBins");
    }

    [Fact]
    public void Every_sql_layer_edge_of_the_ground_truth_is_reproduced()
    {
        var expected = GroundTruth.Load();
        var missing = GroundTruth.MissingEdges(
            expected,
            _result.Snapshot,
            ["sp:", "udf:", "expr:", "spcol:", "ctecol:", "tmpcol:", "cte:", "tmp:"],
            Enum.GetValues<RelationType>().Where(r => r != RelationType.MapsTo).ToList());

        Assert.True(missing.Count == 0, "Missing edges:\n" + GroundTruth.Describe(missing));
    }

    [Fact]
    public void Ground_truth_edge_metadata_matches()
    {
        var expected = GroundTruth.Load();
        var actual = _result.Snapshot.Edges.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var mismatches = new List<string>();
        foreach (var edge in expected.Edges.Where(e => e.FromNodeId.StartsWith("expr:", StringComparison.Ordinal) || e.RelationType == RelationType.UsesParameter))
        {
            if (!actual.TryGetValue(edge.Id, out var scanned))
            {
                continue; // covered by the coverage test
            }

            foreach (var (key, value) in edge.Metadata)
            {
                if (scanned.GetMetadata(key) != value)
                {
                    mismatches.Add($"{edge.Id} [{key}] expected '{value}' got '{scanned.GetMetadata(key)}'");
                }
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }

    private Node Node(string id)
    {
        var node = _result.Snapshot.Nodes.FirstOrDefault(n => n.Id == id);
        Assert.True(node is not null, $"Missing node {id}. Nodes with same prefix:\n" + string.Join("\n", _result.Snapshot.Nodes.Where(n => NodeIds.Prefix(n.Id) == NodeIds.Prefix(id)).Select(n => "  " + n.Id)));
        return node!;
    }

    private Edge AssertEdge(string from, RelationType relation, string to)
    {
        var id = EdgeIds.Of(from, relation, to);
        var edge = _result.Snapshot.Edges.FirstOrDefault(e => e.Id == id);
        Assert.True(edge is not null, $"Missing edge {id}. Edges from '{from}':\n" + string.Join("\n", _result.Snapshot.Edges.Where(e => e.FromNodeId == from).Select(e => "  " + e.Id)));
        return edge!;
    }
}
