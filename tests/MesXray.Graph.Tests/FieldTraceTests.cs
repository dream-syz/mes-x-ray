using MesXray.Domain.Graph;
using MesXray.Graph.Queries;
using MesXray.Graph.Store;

namespace MesXray.Graph.Tests;

/// <summary>Trace Source (design §6, AC-02, AC-04, AC-06) on the assembled graph.</summary>
public sealed class FieldTraceTests : IClassFixture<AssembledGraphFixture>
{
    private readonly AssembledGraphFixture _fx;

    public FieldTraceTests(AssembledGraphFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public void AC02_availableQuantity_shows_both_WMS_Enabled_branches()
    {
        var trace = _fx.Tracer.Trace("availableQuantity");
        Assert.Equal("json:pickOrderRows.availableQuantity", trace.Field.Id);

        var hops = AssembledGraphFixture.Flatten(trace.Root).ToList();
        var caseExpr = Assert.Single(hops, h => h.NodeId == "expr:dbo.AP_Pick_GetPickOrderRows.MainResults.AvailableQuantity");
        Assert.Contains(caseExpr.Sources, s => s.NodeId == "param:WMS_Enabled" && s.ViaRelation == RelationType.ControlledBy);
        Assert.Contains(caseExpr.Sources, s => s.Condition == "@WMS_Enabled = 0" && s.NodeId == "column:dbo.AT_PICK_PRINT_QUEUE_DETAIL.Quantity");
        var udf = Assert.Single(caseExpr.Sources, s => s.NodeId == "udf:dbo.AF_Pick_GetAvailableQuantity");
        Assert.Equal("@WMS_Enabled = 1", udf.Condition);
        Assert.Equal(NodeStatus.Known, udf.Status);

        var conditions = caseExpr.Sources.Select(s => s.Condition).Where(c => c is not null).Distinct().Order().ToList();
        Assert.Equal(["@WMS_Enabled = 0", "@WMS_Enabled = 1"], conditions);

        // With the function definition scanned the trace continues into its RETURN expression down to INVENTORY2.
        var ret = Assert.Single(udf.Sources);
        Assert.Equal("expr:dbo.AF_Pick_GetAvailableQuantity.$.RETURN", ret.NodeId);
        Assert.Contains(ret.Sources, s => s.NodeId == "ctecol:dbo.AF_Pick_GetAvailableQuantity.InventoryData.QuantityOnHand" && s.Condition == "ELSE");
        Assert.Contains(ret.Sources, s => s.NodeId == "column:dbo.DET2_ILG_ProductDeliveryMethod.DeliveryMethod" && s.ViaRelation == RelationType.ControlledBy);
        Assert.Contains(hops, h => h.NodeId == "column:dbo.INVENTORY2.QuantityOnHand" && h.Condition == "ELSE");
        Assert.Contains(hops, h => h.NodeId == "column:dbo.INVENTORY2.QuantityAllocated" && h.Condition == "@UseQuantityAllocated = 1 | PG.Group_ = 'ECU'");

        Assert.Empty(trace.Unknowns);
        Assert.Equal(["Web VP - Pick Order Details", "GET /cwp/v1/picking/pickOrder", "GetPickOrder", "GetPickOrderDetails", "GetPickOrderRows", "GetPickOrderRows", "AP_Pick_GetPickOrderRows", "AF_Pick_GetAvailableQuantity"],
            trace.ExecutionPath.Select(n => n.Name));
    }

    [Fact]
    public void Trace_walks_serialization_mapping_alias_and_expression_hops_in_order()
    {
        var trace = _fx.Tracer.Trace("json:pickOrderRows.availableQuantity");
        var chain = new List<string>();
        var hop = trace.Root;
        while (hop is not null)
        {
            chain.Add(hop.NodeId);
            hop = hop.Sources.FirstOrDefault(s => s.Type() is not (NodeType.SystemParameter or NodeType.Column or NodeType.Function));
        }

        Assert.Equal(
        [
            "json:pickOrderRows.availableQuantity",
            "field:CWPPickOrderRow.AvailableQuantity",
            "spcol:dbo.AP_Pick_GetPickOrderRows.AvailableQuantity",
            "ctecol:dbo.AP_Pick_GetPickOrderRows.MaterialTotals.TotalAvailableQuantity",
            "expr:dbo.AP_Pick_GetPickOrderRows.MaterialTotals.TotalAvailableQuantity",
            "ctecol:dbo.AP_Pick_GetPickOrderRows.MainResults.AvailableQuantity",
            "expr:dbo.AP_Pick_GetPickOrderRows.MainResults.AvailableQuantity",
        ], chain);
    }

    [Fact]
    public void AC04_pick_storage_bin_quantities_show_their_sql_expressions()
    {
        var onHand = _fx.Tracer.Trace("json:pickOrderRows.pickStorageBin.onHandQuantity", _fx.DemoTrace, "T12288");
        var onHandExpr = Assert.Single(AssembledGraphFixture.Flatten(onHand.Root), h => h.NodeId.StartsWith("expr:", StringComparison.Ordinal));
        Assert.Equal("SUM(APPQD.PickedQuantity)", onHandExpr.Expression);
        Assert.Equal("0", onHand.Root.RuntimeValues.Single().Value.GetRawText());

        var allocated = _fx.Tracer.Trace("json:pickOrderRows.pickStorageBin.allocatedQuantity", _fx.DemoTrace, "T12288");
        var allocatedExpr = Assert.Single(AssembledGraphFixture.Flatten(allocated.Root), h => h.NodeId.StartsWith("expr:", StringComparison.Ordinal));
        Assert.Equal("SUM(APPQD.Quantity - ISNULL(APPQD.PickedQuantity, 0))", allocatedExpr.Expression);
        Assert.Equal("18", allocated.Root.RuntimeValues.Single().Value.GetRawText());
        Assert.Contains(AssembledGraphFixture.Flatten(allocated.Root), h => h.NodeId == "column:dbo.AT_PICK_PRINT_QUEUE_DETAIL.PickedQuantity");
        Assert.Empty(allocated.Unknowns);

        var storageBin = _fx.Tracer.Trace("json:pickOrderRows.pickStorageBin.storageBin", _fx.DemoTrace, "T12288");
        var hops = AssembledGraphFixture.Flatten(storageBin.Root).ToList();
        Assert.Contains(hops, h => h.NodeId == "expr:dbo.AP_Pick_GetPickStorageBin.Bins.StorageBin" && h.Expression!.Contains("STRING_AGG", StringComparison.Ordinal));
        Assert.Contains(hops, h => h.NodeId == "tmpcol:dbo.AP_Pick_GetPickStorageBin.#LocationList.StorageBin");
        Assert.Contains(hops, h => h.NodeId == "param:WL_Description_Replacement" && h.ViaRelation == RelationType.ControlledBy);
        Assert.Contains(hops, h => h.Condition == "ELSE" && h.NodeId == "column:dbo.WAREHOUSE_LOCATION.Location");
        Assert.Equal("\"A-01-02,A-01-05\"", storageBin.Root.RuntimeValues.Single().Value.GetRawText());
    }

    [Fact]
    public void Live_trace_overlays_values_on_the_hops_that_have_evidence()
    {
        var trace = _fx.Tracer.Trace("availableQuantity", _fx.DemoTrace, "T12288");
        Assert.Equal("trace-demo-001", trace.TraceId);
        Assert.Equal("0", trace.Root.RuntimeValues.Single().Value.GetRawText());

        var hops = AssembledGraphFixture.Flatten(trace.Root).ToList();
        var spcol = Assert.Single(hops, h => h.NodeId == "spcol:dbo.AP_Pick_GetPickOrderRows.AvailableQuantity");
        Assert.Equal("T12288", spcol.RuntimeValues.Single().Scope);

        var parameter = Assert.Single(hops, h => h.NodeId == "param:WMS_Enabled");
        Assert.Equal("\"1\"", parameter.RuntimeValues.Single().Value.GetRawText());
        Assert.Contains(trace.EvidenceIds, id => id.StartsWith("ev-", StringComparison.Ordinal));
    }

    [Fact]
    public void Scope_filters_row_level_values_but_keeps_order_level_ones()
    {
        var other = _fx.Tracer.Trace("availableQuantity", _fx.DemoTrace, "T55102");
        Assert.Equal("1000", other.Root.RuntimeValues.Single().Value.GetRawText());
        Assert.Contains(AssembledGraphFixture.Flatten(other.Root), h => h.NodeId == "param:WMS_Enabled" && h.RuntimeValues.Count == 1);
    }

    [Fact]
    public void AC06_destination_wagon_storage_bin_ends_in_a_pending_method()
    {
        // CWPStorageBin.Location has no lineage of its own: the trace must follow the model's producers and end
        // explicitly at the Pending enrichment method and the runtime-computed SP, never silently at a Known field.
        var trace = _fx.Tracer.Trace("json:pickOrderRows.destinationWagon.storageBin.location");
        var pending = Assert.Single(trace.Unknowns, u => u.NodeId == "method:PickOrderService.GetStorageBin");
        Assert.Contains("not been scanned", pending.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(trace.Unknowns, u => u.NodeId.StartsWith("sp:dbo.<", StringComparison.Ordinal) && u.Type == NodeType.StoredProcedure);
        Assert.Contains(AssembledGraphFixture.Flatten(trace.Root), h => h.NodeId == "method:PickOrderService.GetStorageBin" && h.Status == NodeStatus.Pending && h.ViaRelation == RelationType.EnrichedBy && h.Sources.Count == 0);
        Assert.Contains(trace.ExecutionPath, n => n.Id == "method:PickOrderService.GetStorageBin");
        Assert.Equal("page:WebVP.PickOrderDetails", trace.ExecutionPath[0].Id);
    }

    [Fact]
    public void Literal_columns_are_explained_without_being_unknown()
    {
        var trace = _fx.Tracer.Trace("json:pickOrderRows.destinationWagon.physicalWagonId");
        Assert.Empty(trace.Unknowns);
        var expr = Assert.Single(AssembledGraphFixture.Flatten(trace.Root), h => h.NodeId.StartsWith("expr:", StringComparison.Ordinal));
        Assert.Equal("''", expr.Expression);
        Assert.Empty(expr.Sources);
    }

    [Fact]
    public void Short_names_resolve_when_unique_and_fail_loudly_when_ambiguous()
    {
        Assert.Equal("json:pickOrderRows.materialImageUrl", _fx.Tracer.ResolveField("materialImageUrl").Id);
        Assert.Equal("json:pickOrderRows.pickStorageBin.storageBin", _fx.Tracer.ResolveField("StorageBin").Id); // json fields win over model fields
        Assert.Equal("json:pickOrderRows.destinationWagon.storageBin.quantity", _fx.Tracer.ResolveField("pickOrderRows.destinationWagon.storageBin.quantity").Id);
        Assert.Throws<NodeNotFoundException>(() => _fx.Tracer.ResolveField("noSuchField"));

        // The demo response has no duplicated leaf names, so ambiguity is exercised on a purpose-built store.
        var store = new InMemoryGraphStore();
        store.Merge(new GraphSnapshot
        {
            Source = "test",
            ScanVersion = "1",
            Nodes =
            [
                new Node { Id = "json:header.status", Type = NodeType.JsonField, Name = "status", Layer = Layer.Web, Status = NodeStatus.Known },
                new Node { Id = "json:rows.status", Type = NodeType.JsonField, Name = "status", Layer = Layer.Web, Status = NodeStatus.Known },
            ],
        });
        var ex = Assert.Throws<AmbiguousFieldException>(() => new FieldTraceService(store, new GraphQueryService(store)).ResolveField("status"));
        Assert.Equal(["json:header.status", "json:rows.status"], ex.Candidates);
    }

    [Fact]
    public void Unknown_focus_produces_an_empty_tree_with_the_gap_stated()
    {
        var trace = _fx.Tracer.Trace("udf:dbo.AF_GetSystemParameterValueint");
        Assert.NotEqual(NodeStatus.Known, trace.Root.Status);
        Assert.Empty(trace.Root.Sources);
        Assert.Single(trace.Unknowns);
    }

    [Fact]
    public void Scalar_function_focus_traces_its_return_value_to_the_base_columns()
    {
        var trace = _fx.Tracer.Trace("udf:dbo.AF_Pick_GetAvailableQuantity", _fx.DemoTrace, "T55102");
        Assert.Equal(NodeStatus.Known, trace.Root.Status);
        Assert.Empty(trace.Unknowns);

        var hops = AssembledGraphFixture.Flatten(trace.Root).ToList();
        Assert.Contains(hops, h => h.NodeId == "expr:dbo.AF_Pick_GetAvailableQuantity.$.RETURN");
        Assert.Contains(hops, h => h.NodeId == "column:dbo.INVENTORY2.QuantityOnHand");
        Assert.Contains(hops, h => h.NodeId == "column:dbo.PRODUCT_GROUP.Group_" && h.ViaRelation == RelationType.ControlledBy);
        Assert.DoesNotContain(hops, h => h.Status != NodeStatus.Known);
    }
}

file static class HopExtensions
{
    public static NodeType Type(this TraceHop hop) => hop.Node.Type;
}
