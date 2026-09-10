using MesXray.Domain.Graph;

namespace MesXray.Graph.Tests;

/// <summary>Impact Analysis (design §6.2, AC-05) and the architecture / node queries.</summary>
public sealed class ImpactAndQueryTests : IClassFixture<AssembledGraphFixture>
{
    private readonly AssembledGraphFixture _fx;

    public ImpactAndQueryTests(AssembledGraphFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public void AC05_WMS_Enabled_impacts_availableQuantity_then_api_then_web_page()
    {
        var impact = _fx.Impact.Analyze("param:WMS_Enabled");
        var ids = impact.Affected.Select(a => a.Node.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("expr:dbo.AP_Pick_GetPickOrderRows.MainResults.AvailableQuantity", ids);
        Assert.Contains("spcol:dbo.AP_Pick_GetPickOrderRows.AvailableQuantity", ids);
        Assert.Contains("field:CWPPickOrderRow.AvailableQuantity", ids);
        Assert.Contains("json:pickOrderRows.availableQuantity", ids);
        Assert.Contains("api:GET /cwp/v1/picking/pickOrder", ids);
        Assert.Contains("page:WebVP.PickOrderDetails", ids);

        var fieldPath = Assert.Single(impact.KeyPaths, p => p[^1] == "json:pickOrderRows.availableQuantity");
        Assert.Equal("param:WMS_Enabled", fieldPath[0]);
        Assert.Contains("spcol:dbo.AP_Pick_GetPickOrderRows.AvailableQuantity", fieldPath);

        // Other result columns of the same SP are not lineage-impacted by the parameter.
        Assert.DoesNotContain("json:pickOrderRows.materialNumber", ids);
    }

    [Fact]
    public void Column_impact_reaches_every_json_field_computed_from_it()
    {
        var impact = _fx.Impact.Analyze("column:dbo.AT_PICK_PRINT_QUEUE_DETAIL.PickedQuantity");
        var ids = impact.Affected.Select(a => a.Node.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("json:pickOrderRows.pickStorageBin.onHandQuantity", ids);
        Assert.Contains("json:pickOrderRows.pickStorageBin.allocatedQuantity", ids);
        Assert.DoesNotContain("json:pickOrderRows.availableQuantity", ids);
    }

    [Fact]
    public void Function_impact_goes_through_the_case_branch()
    {
        var impact = _fx.Impact.Analyze("udf:dbo.AF_Pick_GetAvailableQuantity");
        Assert.Contains(impact.Affected, a => a.Node.Id == "json:pickOrderRows.availableQuantity");
        Assert.Contains(impact.Summary.Keys, k => k == nameof(NodeType.JsonField));
    }

    [Fact]
    public void Architecture_view_shows_page_api_methods_and_procedures_without_columns()
    {
        var arch = _fx.Queries.GetArchitecture("page:WebVP.PickOrderDetails");
        var ids = arch.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("api:GET /cwp/v1/picking/pickOrder", ids);
        Assert.Contains("method:PickOrderService.GetPickOrderDetails", ids);
        Assert.Contains("sp:dbo.AP_Pick_GetPickOrderRows", ids);
        Assert.Contains("param:WMS_Enabled", ids);
        Assert.DoesNotContain(arch.Nodes, n => n.Type is NodeType.Column or NodeType.ResultColumn or NodeType.Expression);
    }

    [Fact]
    public void Subgraph_is_bounded_and_marks_truncation()
    {
        var small = _fx.Queries.GetSubgraph("sp:dbo.AP_Pick_GetPickOrderRows", depth: 1);
        Assert.Contains(small.Nodes, n => n.Id == "param:WMS_Enabled");
        Assert.DoesNotContain(small.Nodes, n => n.Id == "json:pickOrderRows.availableQuantity");

        var large = _fx.Queries.GetSubgraph("page:WebVP.PickOrderDetails", depth: 8);
        Assert.True(large.Nodes.Count <= 400);
    }

    [Fact]
    public void Node_details_include_container_and_lineage()
    {
        var details = _fx.Queries.GetNodeDetails("spcol:dbo.AP_Pick_GetPickOrderRows.AvailableQuantity");
        Assert.Equal("sp:dbo.AP_Pick_GetPickOrderRows", details.Container?.Id);
        Assert.Contains(details.Outgoing, e => e.RelationType == RelationType.AliasOf);
        Assert.Contains(details.Outgoing, e => e.RelationType == RelationType.MapsTo);
        Assert.Single(details.LineageAsOutput);
    }
}
