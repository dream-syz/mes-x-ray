using System.Text;
using System.Text.Json;
using MesXray.Domain.Evidence;
using MesXray.Runtime;
using MesXray.Runtime.Fixtures;
using MesXray.Runtime.Security;
using MesXray.Runtime.Store;
using MesXray.TestSupport;

namespace MesXray.Graph.Tests;

/// <summary>Design §10 / AC-07: whitelisted, read-only runtime access with redaction and audit.</summary>
public sealed class RuntimeGatewayTests
{
    private static (RuntimeToolGateway Gateway, InMemoryToolAuditLog Audit, InMemoryTraceStore Traces) CreateGateway()
    {
        var options = new RuntimeOptions { FixtureRoot = Fixtures.RuntimePath() };
        var redactor = new Redactor();
        var adapter = new FixtureRuntimeAdapter(options, redactor);
        var audit = new InMemoryToolAuditLog();
        var traces = new InMemoryTraceStore();
        return (new RuntimeToolGateway(adapter, traces, audit, redactor, options), audit, traces);
    }

    [Theory]
    [InlineData("execute_arbitrary_sql")]
    [InlineData("update_system_parameter")]
    [InlineData("update_pick_order")]
    public async Task AC07_forbidden_operations_are_rejected_with_a_policy_reason(string tool)
    {
        var (gateway, audit, _) = CreateGateway();
        var result = await gateway.InvokeAsync(new ToolCall(tool, new Dictionary<string, string> { ["orderNo"] = "PICK0843858" }), "ai");

        Assert.False(result.Allowed);
        Assert.Null(result.Result);
        Assert.Contains("forbidden", result.DenyReason, StringComparison.OrdinalIgnoreCase);
        var entry = Assert.Single(audit.Recent());
        Assert.False(entry.Allowed);
        Assert.Equal("ai", entry.Caller);
    }

    [Fact]
    public async Task Unlisted_tools_are_rejected_and_the_whitelist_is_named()
    {
        var (gateway, _, _) = CreateGateway();
        var result = await gateway.InvokeAsync(new ToolCall("drop_everything", new Dictionary<string, string>()));
        Assert.False(result.Allowed);
        Assert.Contains("trace_pick_order", result.DenyReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("WMS_Enabled; DROP TABLE X")]
    [InlineData("WMS_Enabled' OR 1=1 --")]
    [InlineData("select * from SYSTEM_PARAMETER")]
    [InlineData("../../etc/passwd")]
    public void Arguments_that_look_like_sql_or_paths_are_rejected_before_execution(string value)
    {
        var (gateway, _, _) = CreateGateway();
        var reason = gateway.Validate(new ToolCall(RuntimeToolGateway.ReadSystemParameter, new Dictionary<string, string> { ["name"] = value }));
        Assert.NotNull(reason);
    }

    [Fact]
    public void Missing_required_and_oversized_arguments_are_rejected()
    {
        var (gateway, _, _) = CreateGateway();
        Assert.Contains("required", gateway.Validate(new ToolCall(RuntimeToolGateway.TraceMaterial, new Dictionary<string, string> { ["orderNo"] = "PICK0843858" })), StringComparison.Ordinal);
        Assert.Contains("exceeds", gateway.Validate(new ToolCall(RuntimeToolGateway.ReadSystemParameter, new Dictionary<string, string> { ["name"] = new string('a', 101) })), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Whitelisted_reads_work_and_are_audited_with_redacted_arguments()
    {
        var (gateway, audit, traces) = CreateGateway();

        var parameter = await gateway.InvokeAsync(new ToolCall(RuntimeToolGateway.ReadSystemParameter, new Dictionary<string, string> { ["name"] = "WMS_Enabled" }));
        Assert.True(parameter.Allowed);
        Assert.Equal("1", parameter.Result!.Value.GetProperty("value").GetString());
        Assert.Equal("param:WMS_Enabled", parameter.Result!.Value.GetProperty("nodeId").GetString());

        var trace = await gateway.InvokeAsync(new ToolCall(RuntimeToolGateway.TracePickOrder, new Dictionary<string, string> { ["orderNo"] = "PICK0843858", ["user"] = "jane.doe" }));
        Assert.True(trace.Allowed);
        Assert.Equal("trace-demo-001", trace.Result!.Value.GetProperty("traceId").GetString());
        Assert.NotNull(traces.Find("trace-demo-001"));

        var entry = audit.Recent().First(a => a.Tool == RuntimeToolGateway.TracePickOrder);
        Assert.Equal(Redactor.Mask, entry.Arguments["user"]);
        Assert.Equal("PICK0843858", entry.Arguments["orderNo"]);
    }

    [Fact]
    public async Task Unknown_orders_are_reported_as_unknown_not_as_errors()
    {
        var (gateway, _, _) = CreateGateway();
        var result = await gateway.InvokeAsync(new ToolCall(RuntimeToolGateway.TracePickOrder, new Dictionary<string, string> { ["orderNo"] = "PICK0000000" }));
        Assert.True(result.Allowed);
        Assert.True(result.Result!.Value.TryGetProperty("unknown", out var unknown));
        Assert.Contains("PICK0000000", unknown.GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fixtures_from_disallowed_environments_are_refused()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"xray-fixtures-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "prod.json"), """
            { "fixtureId": "prod-1", "entityType": "PickOrder", "entityKey": "PICK1", "environment": "PRODUCTION", "observedAt": "2026-01-01T00:00:00Z", "traceId": "t-prod", "response": [] }
            """);
            var adapter = new FixtureRuntimeAdapter(new RuntimeOptions { FixtureRoot = dir }, new Redactor());
            var ex = await Assert.ThrowsAsync<RuntimeAccessDeniedException>(() => adapter.TracePickOrderAsync(new Domain.Abstractions.PickOrderTraceRequest("PICK1", null, null, null)));
            Assert.Contains("PRODUCTION", ex.Message, StringComparison.Ordinal);
            Assert.Empty(adapter.PreloadedTraces());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Evidence_binder_produces_deterministic_scoped_evidence()
    {
        var adapter = new FixtureRuntimeAdapter(new RuntimeOptions { FixtureRoot = Fixtures.RuntimePath() }, new Redactor());
        var trace = adapter.PreloadedTraces().Single();

        Assert.Equal("trace-demo-001", trace.TraceId);
        Assert.Equal("FIXTURE", trace.Environment);
        Assert.Equal("ev-001", trace.Evidence[0].Id);
        Assert.Equal(trace.Evidence.Select(e => e.Id).Distinct().Count(), trace.Evidence.Count);

        var parameter = Assert.Single(trace.Evidence, e => e.NodeId == "param:WMS_Enabled");
        Assert.Equal(RuntimeEvidenceType.Parameter, parameter.EvidenceType);
        Assert.Null(parameter.Scope);

        var header = Assert.Single(trace.Evidence, e => e.NodeId == "spcol:dbo.AP_Wrapper_Pick_GetPickOrderDetail.IsMultiPickOrder");
        Assert.Null(header.Scope); // keyed by the order itself -> order-level evidence

        var rows = trace.Evidence.Where(e => e.NodeId == "spcol:dbo.AP_Pick_GetPickOrderRows.AvailableQuantity").ToList();
        Assert.Equal(["T12288", "T40917", "T55102"], rows.Select(r => r.Scope).Order());

        var json = Assert.Single(trace.Evidence, e => e.NodeId == "json:pickOrderRows.availableQuantity" && e.Scope == "T12288");
        Assert.Equal("0", json.Value.GetRawText());
        Assert.Equal(RuntimeEvidenceType.ApiResponse, json.EvidenceType);

        Assert.Contains(trace.Evidence, e => e.NodeId == "sp:dbo.AP_Pick_GetPickOrderRows" && e.EvidenceType == RuntimeEvidenceType.Timing);
        Assert.DoesNotContain(trace.Evidence, e => e.NodeId.Contains("user", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Redactor_masks_credentials_hosts_and_personal_data()
    {
        var redactor = new Redactor();
        // Assemble a syntactically valid but meaningless token at runtime: no JWT literal may ever be committed (design §10).
        var fakeJwt = $"{Base64Url("""{"alg":"none","typ":"JWT"}""")}.{Base64Url("""{"sub":"fixture-user","scope":"none"}""")}.{Base64Url("not-a-real-signature")}";
        Assert.StartsWith("eyJ", fakeJwt, StringComparison.Ordinal);
        Assert.Equal("Bearer [redacted]", redactor.RedactText($"Bearer {fakeJwt}"));
        Assert.Equal("Server=[redacted];Database=MES;User Id=[redacted];Password=[redacted];", redactor.RedactText("Server=sql01.corp.local;Database=MES;User Id=svc_mes;Password=Sup3rSecret!;"));
        Assert.Equal("host [redacted] at [redacted]", redactor.RedactText("host mes-db.intranet at 10.20.30.40"));
        Assert.Equal("contact [redacted]", redactor.RedactText("contact jane.doe@example.com"));

        var element = JsonDocument.Parse("""{"user":"jane","token":"abc","rows":[{"materialNumber":"T1","operator":"john","note":"pwd=x1"}]}""").RootElement;
        var redacted = redactor.Redact(element);
        Assert.Equal(Redactor.Mask, redacted.GetProperty("user").GetString());
        Assert.Equal(Redactor.Mask, redacted.GetProperty("token").GetString());
        Assert.Equal(Redactor.Mask, redacted.GetProperty("rows")[0].GetProperty("operator").GetString());
        Assert.Equal("T1", redacted.GetProperty("rows")[0].GetProperty("materialNumber").GetString());
        Assert.Equal("pwd=[redacted]", redacted.GetProperty("rows")[0].GetProperty("note").GetString());
    }

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
