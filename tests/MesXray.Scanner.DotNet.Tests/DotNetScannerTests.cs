using MesXray.Domain.Graph;
using MesXray.Domain.Scanning;
using MesXray.Scanner.DotNet;
using MesXray.TestSupport;

namespace MesXray.Scanner.DotNet.Tests;

/// <summary>Scans the sanitised EBBA fixture once and checks the facts required by the design document (§4.1, AC-01).</summary>
public sealed class DotNetScannerTests : IClassFixture<DotNetScannerTests.ScanFixture>
{
    private readonly ScanResult _result;

    public DotNetScannerTests(ScanFixture fixture)
    {
        _result = fixture.Result;
        _withoutSiteSettings = fixture.WithoutSiteSettings;
    }

    public sealed class ScanFixture
    {
        public ScanFixture()
        {
            Result = Scan(WithSiteSettings);
            WithoutSiteSettings = Scan(DotNetScannerOptions.Default);
        }

        /// <summary>The case as the API builds it: code plus the site's configuration values.</summary>
        public ScanResult Result { get; }

        /// <summary>The same code scanned without configuration input, to check that nothing is guessed.</summary>
        public ScanResult WithoutSiteSettings { get; }

        public static DotNetScannerOptions WithSiteSettings { get; } = new()
        {
            SiteSettings = SiteSettings.ReadIfExists(Fixtures.SiteSettingsPath(), Fixtures.SiteSettingsSource),
        };

        public static ScanResult Scan(DotNetScannerOptions options) => new DotNetScanner(options).ScanDirectory(Fixtures.DotNetSourcePath());
    }

    private readonly ScanResult _withoutSiteSettings;

    [Fact]
    public void Scan_has_no_errors_and_is_deterministic()
    {
        Assert.False(_result.HasErrors, string.Join("\n", _result.Diagnostics.Select(d => d.Message)));

        var again = ScanFixture.Scan(ScanFixture.WithSiteSettings);
        Assert.Equal(_result.Snapshot.Nodes.Select(n => n.Id), again.Snapshot.Nodes.Select(n => n.Id));
        Assert.Equal(_result.Snapshot.Edges.Select(e => e.Id), again.Snapshot.Edges.Select(e => e.Id));
    }

    [Fact]
    public void HttpGet_plus_Route_produces_api_endpoint_handled_by_action()
    {
        var api = Assert.Single(_result.Snapshot.Nodes, n => n.Type == NodeType.Api);
        Assert.Equal("api:GET /cwp/v1/picking/pickOrder", api.Id);
        Assert.Equal("/cwp/v1/picking/pickOrder", api.GetMetadata("route"));
        AssertEdge("api:GET /cwp/v1/picking/pickOrder", RelationType.HandledBy, "method:PickingController.GetPickOrder");
        AssertEdge("api:GET /cwp/v1/picking/pickOrder", RelationType.Returns, "model:CWPPickOrderModel");
    }

    [Fact]
    public void AC01_GetPickOrderRows_executes_AP_Pick_GetPickOrderRows()
    {
        var edge = AssertEdge("method:PickOrderQuery.GetPickOrderRows", RelationType.ExecutesSp, "sp:dbo.AP_Pick_GetPickOrderRows");
        Assert.Equal(1.0, edge.Confidence);
        Assert.Equal(EvidenceType.Roslyn, edge.EvidenceType);
        Assert.Contains("PickOrderQuery.cs:L", edge.EvidenceRef, StringComparison.Ordinal);
        Assert.Equal("StoredProcedure", edge.GetMetadata("commandType"));
    }

    [Fact]
    public void Dapper_generic_argument_maps_sp_to_model()
    {
        AssertEdge("sp:dbo.AP_Pick_GetPickOrderRows", RelationType.MapsTo, "model:CWPPickOrderRow");
        AssertEdge("sp:dbo.AP_Pick_GetPickStorageBin", RelationType.MapsTo, "model:CWPPickStorageBin");
        AssertEdge("sp:dbo.AP_Pick_GetDestinationWagon", RelationType.MapsTo, "model:CWPDestinationWagon");
        AssertEdge("sp:dbo.AP_Wrapper_Pick_GetPickOrderDetail", RelationType.MapsTo, "model:PickOrderHeader");
    }

    [Fact]
    public void Interface_calls_resolve_to_the_single_implementation()
    {
        var edge = AssertEdge("method:PickingController.GetPickOrder", RelationType.Calls, "method:PickOrderService.GetPickOrderDetails");
        Assert.Equal("IPickOrderService", edge.GetMetadata("via"));
        Assert.True(edge.Confidence >= 0.9);

        AssertEdge("method:PickOrderService.GetPickOrderRows", RelationType.Calls, "method:PickOrderQuery.GetPickOrderRows");
        AssertEdge("method:PickOrderService.GetPickOrderRows", RelationType.Calls, "method:PickOrderService.GetPickStorageBin");
        AssertEdge("method:PickOrderService.GetPickOrderRows", RelationType.Calls, "method:PickOrderService.GetDestinationWagon");
    }

    [Fact]
    public void Branch_on_IsMultiPickOrder_is_recorded_with_guarded_calls()
    {
        var branch = AssertEdge("method:PickOrderService.GetPickOrderDetails", RelationType.BranchesOn, "field:PickOrderHeader.IsMultiPickOrder");
        Assert.Equal("pickOrder.IsMultiPickOrder", branch.GetMetadata("condition"));

        var thenCall = AssertEdge("method:PickOrderService.GetPickOrderDetails", RelationType.Calls, "method:PickOrderService.GetMultiPickOrderRows");
        Assert.Equal("then", thenCall.GetMetadata("branch"));

        var elseCall = AssertEdge("method:PickOrderService.GetPickOrderDetails", RelationType.Calls, "method:PickOrderService.GetPickOrderRows");
        Assert.Equal("else", elseCall.GetMetadata("branch"));
        Assert.Equal("pickOrder.IsMultiPickOrder", elseCall.GetMetadata("condition"));
    }

    [Fact]
    public void Property_assignment_from_method_result_is_enrichment()
    {
        AssertEdge("field:CWPPickOrderRow.PickStorageBin", RelationType.EnrichedBy, "method:PickOrderService.GetPickStorageBin");
        AssertEdge("field:CWPPickOrderRow.DestinationWagon", RelationType.EnrichedBy, "method:PickOrderService.GetDestinationWagon");
        AssertEdge("field:CWPDestinationWagon.StorageBin", RelationType.EnrichedBy, "method:PickOrderService.GetStorageBin");
        AssertEdge("field:CWPPickOrderModel.PickOrderRows", RelationType.EnrichedBy, "method:PickOrderService.GetPickOrderRows");
    }

    [Fact]
    public void Property_copy_between_models_is_a_direct_mapping_with_lineage()
    {
        var edge = AssertEdge("field:PickOrderHeader.PickOrderNo", RelationType.MapsTo, "field:CWPPickOrderModel.PickOrderNo");
        Assert.Equal("Assignment", edge.GetMetadata("strategy"));
        var lineage = Assert.Single(_result.Snapshot.Lineages, l => l.OutputFieldId == "field:CWPPickOrderModel.PickOrderNo");
        Assert.Equal(TransformType.Direct, lineage.TransformType);
        Assert.Contains(edge.Id, lineage.EvidenceEdgeIds);
    }

    [Fact]
    public void Models_and_fields_are_declared_with_kind_metadata()
    {
        var row = Assert.Single(_result.Snapshot.Nodes, n => n.Id == "model:CWPPickOrderRow");
        Assert.Equal(NodeStatus.Known, row.Status);
        Assert.NotNull(row.Source);

        var scalar = Assert.Single(_result.Snapshot.Nodes, n => n.Id == "field:CWPPickOrderRow.AvailableQuantity");
        Assert.Equal("scalar", scalar.GetMetadata("kind"));
        var nested = Assert.Single(_result.Snapshot.Nodes, n => n.Id == "field:CWPPickOrderRow.PickStorageBin");
        Assert.Equal("model", nested.GetMetadata("kind"));
        var collection = Assert.Single(_result.Snapshot.Nodes, n => n.Id == "field:CWPPickOrderRow.DestinationWagon");
        Assert.Equal("collection", collection.GetMetadata("kind"));

        AssertEdge("model:CWPPickOrderRow", RelationType.Contains, "field:CWPPickOrderRow.AvailableQuantity");
        AssertEdge("field:CWPPickOrderRow.PickStorageBin", RelationType.OfType, "model:CWPPickStorageBin");
    }

    [Fact]
    public void Response_model_is_flattened_into_camelCase_json_fields()
    {
        var json = Assert.Single(_result.Snapshot.Nodes, n => n.Id == "json:pickOrderRows.availableQuantity");
        Assert.Equal("$[].pickOrderRows[].availableQuantity", json.QualifiedName);
        AssertEdge("api:GET /cwp/v1/picking/pickOrder", RelationType.Contains, "json:pickOrderRows.availableQuantity");
        AssertEdge("field:CWPPickOrderRow.AvailableQuantity", RelationType.SerializesAs, "json:pickOrderRows.availableQuantity");
        AssertEdge("field:CWPPickStorageBin.StorageBin", RelationType.SerializesAs, "json:pickOrderRows.pickStorageBin.storageBin");
        AssertEdge("field:CWPDestinationWagon.NumberOfBoxes", RelationType.SerializesAs, "json:pickOrderRows.destinationWagon.numberOfBoxes");
        Assert.Contains(_result.Snapshot.Lineages, l => l.OutputFieldId == "json:pickOrderRows.availableQuantity" && l.TransformType == TransformType.Serialization);
    }

    [Fact]
    public void Computed_procedure_name_is_marked_unknown_not_guessed_without_site_settings()
    {
        var id = EdgeIds.Of("method:PickOrderQuery.GetStorageBin", RelationType.ExecutesSp, "sp:dbo.<_options.StorageBinProcedure>");
        var edge = Assert.Single(_withoutSiteSettings.Snapshot.Edges, e => e.Id == id);
        Assert.True(edge.Confidence < 0.6);
        Assert.Equal("_options.StorageBinProcedure", edge.GetMetadata("unresolvedExpression"));
        var sp = Assert.Single(_withoutSiteSettings.Snapshot.Nodes, n => n.Id == "sp:dbo.<_options.StorageBinProcedure>");
        Assert.Equal(NodeStatus.Unknown, sp.Status);
        Assert.Contains(_withoutSiteSettings.Diagnostics, d => d.Severity == ScanDiagnosticSeverity.Warning && d.Message.Contains("StorageBinProcedure", StringComparison.Ordinal));
        Assert.DoesNotContain(_withoutSiteSettings.Snapshot.Nodes, n => n.Id == "sp:dbo.AP_Pick_GetPutStorageBin");
    }

    [Fact]
    public void Configured_procedure_name_is_resolved_from_the_site_settings_with_configuration_evidence()
    {
        // PickingOptions.StorageBinProcedure = dbo.AP_Pick_GetPutStorageBin at the demo site (P1 input, 2026-09-17).
        var edge = AssertEdge("method:PickOrderQuery.GetStorageBin", RelationType.ExecutesSp, "sp:dbo.AP_Pick_GetPutStorageBin");
        Assert.Equal(EvidenceType.Configuration, edge.EvidenceType);
        Assert.Equal(0.9, edge.Confidence);
        Assert.Equal("config/site-settings.json#PickingOptions.StorageBinProcedure", edge.EvidenceRef);
        Assert.Equal("PickingOptions.StorageBinProcedure", edge.GetMetadata("resolvedFrom"));
        Assert.Equal("dbo.AP_Pick_GetPutStorageBin", edge.GetMetadata("configuredValue"));
        Assert.StartsWith("Queries/PickOrderQuery.cs:", edge.GetMetadata("codeRef"), StringComparison.Ordinal);
        Assert.Contains(edge.EvidenceRef!, edge.GetMetadata("evidenceRefs")!, StringComparison.Ordinal);

        // The procedure itself is still only referenced: its definition has to come from the SQL scanner (or curation).
        var sp = Assert.Single(_result.Snapshot.Nodes, n => n.Id == "sp:dbo.AP_Pick_GetPutStorageBin");
        Assert.Equal(NodeStatus.Unknown, sp.Status);
        Assert.Equal("dbo.AP_Pick_GetPutStorageBin", sp.QualifiedName);
        AssertEdge("sp:dbo.AP_Pick_GetPutStorageBin", RelationType.MapsTo, "model:CWPStorageBin");

        Assert.DoesNotContain(_result.Snapshot.Nodes, n => n.Id.StartsWith("sp:dbo.<", StringComparison.Ordinal));
        Assert.DoesNotContain(_result.Diagnostics, d => d.Severity == ScanDiagnosticSeverity.Warning && d.Message.Contains("StorageBinProcedure", StringComparison.Ordinal));
    }

    [Fact]
    public void Referenced_stored_procedures_are_placeholders_until_the_sql_scanner_defines_them()
    {
        var sp = Assert.Single(_result.Snapshot.Nodes, n => n.Id == "sp:dbo.AP_Pick_GetPickOrderRows");
        Assert.Equal(NodeStatus.Unknown, sp.Status);
        Assert.Equal("dbo.AP_Pick_GetPickOrderRows", sp.QualifiedName);
    }

    private Edge AssertEdge(string from, RelationType relation, string to)
    {
        var id = EdgeIds.Of(from, relation, to);
        var edge = _result.Snapshot.Edges.FirstOrDefault(e => e.Id == id);
        Assert.True(edge is not null, $"Missing edge {id}. Edges from '{from}':\n" + string.Join("\n", _result.Snapshot.Edges.Where(e => e.FromNodeId == from).Select(e => "  " + e.Id)));
        return edge!;
    }
}
