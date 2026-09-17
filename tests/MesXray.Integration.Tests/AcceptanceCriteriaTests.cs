using System.Net;
using System.Text.Json.Nodes;

namespace MesXray.Integration.Tests;

/// <summary>Design §13 acceptance criteria AC-01..AC-08, exercised through the HTTP surface of design §7.</summary>
public sealed class AcceptanceCriteriaTests : IClassFixture<XRayApiFactory>
{
    private const string Api = "/api/xray";
    private readonly HttpClient _client;

    public AcceptanceCriteriaTests(XRayApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_reports_a_built_graph_with_the_expected_pipeline()
    {
        var health = await GetJson($"{Api}/health");
        Assert.Equal("ok", health["status"]!.GetValue<string>());
        var report = health["graph"]!;
        Assert.Equal("scanAndCurate", report["mode"]!.GetValue<string>());
        Assert.True(report["nodes"]!.GetValue<int>() > 150);
        var steps = report["steps"]!.AsArray().Select(s => s!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(["dotnet-scanner", "sql-scanner", "linker", "ground-truth", "manual-overrides"], steps);
        Assert.True(report["linker"]!["mappedColumns"]!.GetValue<int>() >= 15);
        Assert.Empty(report["linker"]!["unmappedColumns"]!.AsArray());
    }

    [Fact]
    public async Task Case_overview_exposes_key_fields_gaps_and_tool_policy()
    {
        var cases = await GetJson($"{Api}/cases");
        Assert.Equal("pick-order-details", cases[0]!["id"]!.GetValue<string>());

        var overview = await GetJson($"{Api}/cases/pick-order-details");
        Assert.Equal(8, overview["keyFields"]!.AsArray().Count);
        // The P0 gap (AF_Pick_GetAvailableQuantity definition) was delivered and scanned: it is no longer a known gap.
        Assert.DoesNotContain(overview["knownGaps"]!.AsArray(), g => g!["nodeId"]!.GetValue<string>() == "udf:dbo.AF_Pick_GetAvailableQuantity");
        Assert.Equal(2, overview["knownGaps"]!.AsArray().Count);
        // P1: the site configuration named the procedure (2026-09-17), so the gap is now its definition, not the method.
        Assert.Contains(overview["knownGaps"]!.AsArray(), g => g!["nodeId"]!.GetValue<string>() == "sp:dbo.AP_Pick_GetPutStorageBin" && g["status"]!.GetValue<string>() == "pending");
        Assert.DoesNotContain(overview["knownGaps"]!.AsArray(), g => g!["nodeId"]!.GetValue<string>() == "method:PickOrderService.GetStorageBin");
        Assert.Contains(overview["systemParameters"]!.AsArray(), p => p!["id"]!.GetValue<string>() == "param:Pick_UseParentLocation");
        Assert.Contains("trace_pick_order", overview["allowedTools"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.Contains("execute_arbitrary_sql", overview["forbiddenTools"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.Contains(overview["traces"]!.AsArray(), t => t!["traceId"]!.GetValue<string>() == "trace-demo-001");

        var missing = await _client.GetAsync($"{Api}/cases/nope");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task AC01_method_to_stored_procedure_edge_is_visible_on_the_node()
    {
        var details = (await GetJson($"{Api}/nodes/method:PickOrderQuery.GetPickOrderRows"))["details"]!;
        Assert.Equal("known", details["node"]!["status"]!.GetValue<string>());
        var outgoing = details["outgoing"]!.AsArray();
        Assert.Contains(outgoing, e => e!["relationType"]!.GetValue<string>() == "executesSp" && e["toNodeId"]!.GetValue<string>() == "sp:dbo.AP_Pick_GetPickOrderRows");

        var sp = (await GetJson($"{Api}/node?id=sp:dbo.AP_Pick_GetPickOrderRows"))["details"]!;
        Assert.Contains(sp["outgoing"]!.AsArray(), e => e!["relationType"]!.GetValue<string>() == "usesParameter" && e["toNodeId"]!.GetValue<string>() == "param:WMS_Enabled");
        Assert.Contains(sp["outgoing"]!.AsArray(), e => e!["relationType"]!.GetValue<string>() == "reads" && e["toNodeId"]!.GetValue<string>() == "table:dbo.AT_PICK_PRINT_QUEUE_DETAIL");
    }

    [Fact]
    public async Task AC02_availableQuantity_trace_shows_both_WMS_branches_and_the_execution_path()
    {
        var trace = await GetJson($"{Api}/trace/field?field=availableQuantity");
        Assert.Equal("json:pickOrderRows.availableQuantity", trace["field"]!["id"]!.GetValue<string>());

        var hops = Flatten(trace["root"]!).ToList();
        var udf = Assert.Single(hops, h => h["nodeId"]!.GetValue<string>() == "udf:dbo.AF_Pick_GetAvailableQuantity");
        Assert.Equal("@WMS_Enabled = 1", udf["condition"]!.GetValue<string>());
        Assert.Equal("known", udf["status"]!.GetValue<string>());
        var tableColumn = Assert.Single(hops, h => h["nodeId"]!.GetValue<string>() == "column:dbo.AT_PICK_PRINT_QUEUE_DETAIL.Quantity" && h["condition"] is not null);
        Assert.Equal("@WMS_Enabled = 0", tableColumn["condition"]!.GetValue<string>());
        Assert.Contains(hops, h => h["nodeId"]!.GetValue<string>() == "param:WMS_Enabled" && h["viaRelation"]!.GetValue<string>() == "controlledBy");

        // The WMS branch continues through the function's RETURN expression down to INVENTORY2.
        Assert.Contains(hops, h => h["nodeId"]!.GetValue<string>() == "expr:dbo.AF_Pick_GetAvailableQuantity.$.RETURN");
        Assert.Contains(hops, h => h["nodeId"]!.GetValue<string>() == "column:dbo.INVENTORY2.QuantityOnHand");
        Assert.Empty(trace["unknowns"]!.AsArray());

        var path = trace["executionPath"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).ToList();
        Assert.Equal("page:WebVP.PickOrderDetails", path[0]);
        Assert.Equal("api:GET /cwp/v1/picking/pickOrder", path[1]);
        Assert.Contains("method:PickOrderQuery.GetPickOrderRows", path);
        Assert.Contains("sp:dbo.AP_Pick_GetPickOrderRows", path);
        Assert.Equal("udf:dbo.AF_Pick_GetAvailableQuantity", path[^1]); // the SP calls the UDF at runtime
    }

    [Fact]
    public async Task AC03_explain_binds_facts_to_evidence_and_follows_the_udf_to_its_base_columns()
    {
        var response = await _client.PostAsync($"{Api}/ai/explain", XRayApiFactory.Json(new
        {
            focusNodeId = "json:pickOrderRows.availableQuantity",
            traceId = "trace-demo-001",
            scope = "T12288",
            question = "Why is Available Quantity 0?",
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await XRayApiFactory.ReadJsonAsync(response);
        var explanation = body["explanation"]!;

        // The whole static lineage is evidenced; only a runtime detail (values computed inside SQL Server) is missing.
        Assert.Equal("known", explanation["verdict"]!.GetValue<string>());
        var evidenceIds = body["evidence"]!.AsArray().Select(e => e!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var facts = explanation["knownFacts"]!.AsArray();
        Assert.NotEmpty(facts);
        foreach (var fact in facts)
        {
            var ids = fact!["evidenceIds"]!.AsArray().Select(i => i!.GetValue<string>()).ToList();
            Assert.NotEmpty(ids);
            Assert.All(ids, id => Assert.Contains(id, evidenceIds));
        }

        Assert.Contains(facts, f => f!["text"]!.GetValue<string>().Contains("WMS_Enabled = 1", StringComparison.Ordinal));
        Assert.Contains(facts, f => f!["text"]!.GetValue<string>().Contains("AF_Pick_GetAvailableQuantity = IF EXISTS", StringComparison.Ordinal));
        Assert.Contains(facts, f => f!["text"]!.GetValue<string>().Contains("returns the constant 1000", StringComparison.Ordinal));
        Assert.Contains(facts, f => f!["text"]!.GetValue<string>().Contains("dbo.INVENTORY2.QuantityOnHand", StringComparison.Ordinal));
        Assert.Empty(explanation["hypotheses"]!.AsArray());
        Assert.Contains(explanation["unknowns"]!.AsArray(), u => u!.GetValue<string>().Contains("evaluated inside SQL Server", StringComparison.Ordinal));
        Assert.InRange(explanation["confidence"]!.GetValue<double>(), 0.85, 0.95);

        var audit = explanation["audit"]!;
        Assert.Equal("rules", audit["provider"]!.GetValue<string>());
        Assert.NotNull(audit["model"]);
        Assert.NotNull(audit["timestamp"]);
        Assert.NotEmpty(audit["evidenceIds"]!.AsArray());
    }

    [Fact]
    public async Task AC04_storage_bin_quantities_show_their_sql_expressions_with_live_values()
    {
        var onHand = await GetJson($"{Api}/trace/field?field=json:pickOrderRows.pickStorageBin.onHandQuantity&traceId=trace-demo-001&scope=T12288");
        var onHandExpr = Assert.Single(Flatten(onHand["root"]!), h => h["nodeId"]!.GetValue<string>().StartsWith("expr:", StringComparison.Ordinal));
        Assert.Equal("SUM(APPQD.PickedQuantity)", onHandExpr["expression"]!.GetValue<string>());
        Assert.Equal("0", onHand["root"]!["runtimeValues"]![0]!["value"]!.ToJsonString());

        var allocated = await GetJson($"{Api}/trace/field?field=json:pickOrderRows.pickStorageBin.allocatedQuantity&traceId=trace-demo-001&scope=T12288");
        var allocatedExpr = Assert.Single(Flatten(allocated["root"]!), h => h["nodeId"]!.GetValue<string>().StartsWith("expr:", StringComparison.Ordinal));
        Assert.Equal("SUM(APPQD.Quantity - ISNULL(APPQD.PickedQuantity, 0))", allocatedExpr["expression"]!.GetValue<string>());
        Assert.Equal("18", allocated["root"]!["runtimeValues"]![0]!["value"]!.ToJsonString());
        Assert.Empty(allocated["unknowns"]!.AsArray());
    }

    [Fact]
    public async Task AC05_impact_of_WMS_Enabled_reaches_field_api_and_page()
    {
        var impact = await GetJson($"{Api}/impact/param:WMS_Enabled");
        var affected = impact["affected"]!.AsArray().Select(a => a!["node"]!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("json:pickOrderRows.availableQuantity", affected);
        Assert.Contains("api:GET /cwp/v1/picking/pickOrder", affected);
        Assert.Contains("page:WebVP.PickOrderDetails", affected);
        var keyPaths = impact["keyPaths"]!.AsArray().Select(p => p!.AsArray().Select(n => n!.GetValue<string>()).ToList()).ToList();
        Assert.All(keyPaths, p => Assert.Equal("param:WMS_Enabled", p[0]));
        Assert.Contains(keyPaths, p => p[^1] == "page:WebVP.PickOrderDetails" && p.Contains("api:GET /cwp/v1/picking/pickOrder"));
        Assert.Contains(keyPaths, p => p[^1] == "json:pickOrderRows.availableQuantity" && p.Contains("spcol:dbo.AP_Pick_GetPickOrderRows.AvailableQuantity"));

        var summary = await _client.PostAsync($"{Api}/ai/impact-summary", XRayApiFactory.Json(new { nodeId = "param:WMS_Enabled" }));
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        var body = await XRayApiFactory.ReadJsonAsync(summary);
        Assert.Contains("WMS_Enabled", body["explanation"]!["summary"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AC03_investigate_explains_zero_with_a_hypothesis_bound_to_the_function_return_expression()
    {
        var response = await _client.PostAsync($"{Api}/ai/investigate", XRayApiFactory.Json(new
        {
            traceId = "trace-demo-001",
            focusNodeId = "json:pickOrderRows.availableQuantity",
            scope = "T12288",
            question = "Why is Available Quantity 0?",
            language = "zh-CN",
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await XRayApiFactory.ReadJsonAsync(response);
        var explanation = body["explanation"]!;

        Assert.Equal("needMoreEvidence", explanation["verdict"]!.GetValue<string>());
        Assert.Contains("需要更多证据", explanation["summary"]!.GetValue<string>(), StringComparison.Ordinal);
        var hypothesis = Assert.Single(explanation["hypotheses"]!.AsArray());
        Assert.Contains("ISNULL(SUM(QuantityOnHand), 0)", hypothesis!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("unverified", hypothesis["status"]!.GetValue<string>());
        Assert.Contains("TEST", hypothesis["suggestedCheck"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains(explanation["nextSteps"]!.AsArray(), s => s!.GetValue<string>().Contains("dbo.DET2_ILG_ProductDeliveryMethod", StringComparison.Ordinal));
        Assert.InRange(explanation["confidence"]!.GetValue<double>(), 0.7, 0.85);
    }

    [Fact]
    public async Task AC06_destination_wagon_storage_bin_is_reported_as_unknown_not_guessed()
    {
        var response = await _client.PostAsync($"{Api}/ai/investigate", XRayApiFactory.Json(new
        {
            traceId = "trace-demo-001",
            focusNodeId = "json:pickOrderRows.destinationWagon.storageBin.location",
            question = "Where does the destination wagon storage bin come from?",
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var explanation = (await XRayApiFactory.ReadJsonAsync(response))["explanation"]!;

        Assert.Equal("needMoreEvidence", explanation["verdict"]!.GetValue<string>());
        Assert.Contains(explanation["unknowns"]!.AsArray(), u => u!.GetValue<string>().Contains("AP_Pick_GetPutStorageBin", StringComparison.Ordinal));
        Assert.Contains(explanation["nextSteps"]!.AsArray(), s => s!.GetValue<string>().Contains("AP_Pick_GetPutStorageBin", StringComparison.Ordinal));
        Assert.DoesNotContain(explanation["knownFacts"]!.AsArray(), f => f!["text"]!.GetValue<string>().Contains("StorageBin returns", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AC07_forbidden_tools_and_hostile_arguments_are_denied_with_403_and_audited()
    {
        var denied = await _client.PostAsync($"{Api}/runtime/tools", XRayApiFactory.Json(new { tool = "update_system_parameter", arguments = new { name = "WMS_Enabled", value = "0" } }));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var result = await XRayApiFactory.ReadJsonAsync(denied);
        Assert.False(result["allowed"]!.GetValue<bool>());
        Assert.Contains("forbidden", result["denyReason"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        var auditId = result["auditId"]!.GetValue<string>();

        var hostile = await _client.PostAsync($"{Api}/runtime/pick-order", XRayApiFactory.Json(new { orderNo = "PICK0843858' OR 1=1 --" }));
        Assert.Equal(HttpStatusCode.Forbidden, hostile.StatusCode);
        var problem = await XRayApiFactory.ReadJsonAsync(hostile);
        Assert.Equal("Runtime operation denied", problem["title"]!.GetValue<string>());

        var audit = await GetJson($"{Api}/runtime/audit?count=50");
        Assert.Contains(audit.AsArray(), a => a!["auditId"]!.GetValue<string>() == auditId && !a["allowed"]!.GetValue<bool>());

        var tools = await GetJson($"{Api}/runtime/tools");
        Assert.DoesNotContain("execute_arbitrary_sql", tools["allowed"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.Contains("execute_arbitrary_sql", tools["forbidden"]!.AsArray().Select(t => t!.GetValue<string>()));
    }

    [Fact]
    public async Task Whitelisted_runtime_reads_return_redacted_camelCase_results()
    {
        var response = await _client.PostAsync($"{Api}/runtime/tools", XRayApiFactory.Json(new { tool = "read_system_parameter", arguments = new { name = "WMS_Enabled", traceId = "trace-demo-001" } }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await XRayApiFactory.ReadJsonAsync(response);
        Assert.True(result["allowed"]!.GetValue<bool>());
        Assert.Equal("param:WMS_Enabled", result["result"]!["nodeId"]!.GetValue<string>());
        Assert.Equal("\"1\"", result["result"]!["value"]!.ToJsonString());

        var live = await _client.PostAsync($"{Api}/runtime/pick-order", XRayApiFactory.Json(new { orderNo = "PICK0843858" }));
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        var trace = await XRayApiFactory.ReadJsonAsync(live);
        Assert.Equal("trace-demo-001", trace["traceId"]!.GetValue<string>());
        var text = trace.ToJsonString();
        Assert.DoesNotContain("eyJ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", text, StringComparison.OrdinalIgnoreCase);

        var unknownOrder = await _client.PostAsync($"{Api}/runtime/pick-order", XRayApiFactory.Json(new { orderNo = "PICK0000000" }));
        Assert.Equal(HttpStatusCode.NotFound, unknownOrder.StatusCode);
    }

    [Fact]
    public async Task AC08_the_demo_sequence_is_deterministic_and_offline()
    {
        var first = await RunDemoSequence();
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(first, await RunDemoSequence());
        }
    }

    [Fact]
    public async Task Errors_are_problem_details()
    {
        var notFound = await _client.GetAsync($"{Api}/node?id=sp:dbo.DoesNotExist");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        var problem = await XRayApiFactory.ReadJsonAsync(notFound);
        Assert.Equal(404, problem["status"]!.GetValue<int>());
        Assert.Contains("DoesNotExist", problem["detail"]!.GetValue<string>(), StringComparison.Ordinal);

        var staleTrace = await _client.GetAsync($"{Api}/trace/field?field=availableQuantity&traceId=trace-does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, staleTrace.StatusCode);

        // A body missing a required member is a 400 problem, not a 500.
        var malformed = await _client.PostAsync($"{Api}/ai/investigate", XRayApiFactory.Json(new { question = "no trace id" }));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        var malformedProblem = await XRayApiFactory.ReadJsonAsync(malformed);
        Assert.Contains("traceId", malformedProblem["detail"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);

        // The question is optional for Investigate (the UI sends none by default).
        var noQuestion = await _client.PostAsync($"{Api}/ai/investigate", XRayApiFactory.Json(new { traceId = "trace-demo-001", focusNodeId = "json:pickOrderRows.availableQuantity", scope = "T12288" }));
        Assert.Equal(HttpStatusCode.OK, noQuestion.StatusCode);

        var openApi = await _client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        var document = await XRayApiFactory.ReadJsonAsync(openApi);
        Assert.NotNull(document["paths"]!["/api/xray/ai/explain"]);
    }

    private async Task<string> RunDemoSequence()
    {
        var architecture = await GetJson($"{Api}/graph?mode=architecture");
        var live = await XRayApiFactory.ReadJsonAsync(await _client.PostAsync($"{Api}/runtime/pick-order", XRayApiFactory.Json(new { orderNo = "PICK0843858" })));
        var trace = await GetJson($"{Api}/trace/field?field=json:pickOrderRows.availableQuantity&traceId=trace-demo-001&scope=T12288");
        var explain = await XRayApiFactory.ReadJsonAsync(await _client.PostAsync($"{Api}/ai/explain", XRayApiFactory.Json(new { focusNodeId = "json:pickOrderRows.availableQuantity", traceId = "trace-demo-001", scope = "T12288" })));
        var impact = await GetJson($"{Api}/impact/param:WMS_Enabled");

        explain["explanation"]!["audit"]!.AsObject().Remove("timestamp");
        return string.Join("\n", architecture.ToJsonString(), live.ToJsonString(), trace.ToJsonString(), explain.ToJsonString(), impact.ToJsonString());
    }

    private async Task<JsonNode> GetJson(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, $"{url} -> {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await XRayApiFactory.ReadJsonAsync(response);
    }

    private static IEnumerable<JsonNode> Flatten(JsonNode hop)
    {
        yield return hop;
        foreach (var source in hop["sources"]?.AsArray() ?? [])
        {
            foreach (var nested in Flatten(source!))
            {
                yield return nested;
            }
        }
    }
}
