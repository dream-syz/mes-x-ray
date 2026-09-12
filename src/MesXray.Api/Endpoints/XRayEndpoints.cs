using MesXray.AI;
using MesXray.AI.Evidence;
using MesXray.AI.Investigators;
using MesXray.Api.Bootstrap;
using MesXray.Domain.Abstractions;
using MesXray.Domain.Evidence;
using MesXray.Domain.Graph;
using MesXray.Graph.Queries;
using MesXray.Runtime.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MesXray.Api.Endpoints;

/// <summary>Minimal API surface from design §7 (plus a few read-only helpers for the UI).</summary>
public static class XRayEndpoints
{
    public static IEndpointRouteBuilder MapXRayEndpoints(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api/xray").WithTags("X-Ray");

        api.MapGet("/health", (GraphBootstrapper bootstrapper) => Results.Ok(new { status = "ok", graph = bootstrapper.Report }))
           .WithName("Health");

        MapCases(api);
        MapGraph(api);
        MapTrace(api);
        MapRuntime(api);
        MapAi(api);
        return routes;
    }

    // ------------------------------------------------------------------ cases

    private static void MapCases(RouteGroupBuilder api)
    {
        api.MapGet("/cases", (GraphBootstrapper bootstrapper) => Results.Ok(bootstrapper.Case is null ? Array.Empty<object>() : [new { bootstrapper.Case.Id, bootstrapper.Case.Title }]))
           .WithName("ListCases");

        api.MapGet("/cases/{caseId}", Results<Ok<CaseOverview>, NotFound<ProblemDetails>> (string caseId, GraphBootstrapper bootstrapper, IGraphRepository graph, ITraceStore traces) =>
        {
            var definition = bootstrapper.Case;
            if (definition is null || !string.Equals(definition.Id, caseId, StringComparison.OrdinalIgnoreCase))
            {
                return TypedResults.NotFound(new ProblemDetails { Title = "Case not found", Detail = $"Case '{caseId}' is not loaded.", Status = 404 });
            }

            var keyFields = definition.KeyFields.Select(graph.FindNode).Where(n => n is not null).Select(n => n!).ToList();
            var parameters = definition.SystemParameters.Select(graph.FindNode).Where(n => n is not null).Select(n => n!).ToList();
            var gaps = definition.KnownGaps.Select(g =>
            {
                var node = graph.FindNode(g.NodeId);
                return new GapStatus(g.NodeId, g.Reason, g.Priority, node?.Status ?? NodeStatus.Unknown, node?.Name);
            }).ToList();

            var overview = new CaseOverview(
                definition,
                keyFields,
                parameters,
                gaps,
                traces.List().Select(Summarize).ToList(),
                RuntimeToolGateway.AllowedTools,
                RuntimeToolGateway.ForbiddenTools,
                bootstrapper.Report);
            return TypedResults.Ok(overview);
        }).WithName("GetCase");
    }

    // ------------------------------------------------------------------ graph

    private static void MapGraph(RouteGroupBuilder api)
    {
        api.MapGet("/graph", (string? root, int? depth, bool? columns, string? mode, GraphQueryService queries, GraphBootstrapper bootstrapper) =>
        {
            var rootId = root ?? bootstrapper.Case?.RootNodeId ?? throw new NodeNotFoundException("(no root)");
            var subgraph = string.Equals(mode, "architecture", StringComparison.OrdinalIgnoreCase)
                ? queries.GetArchitecture(rootId)
                : queries.GetSubgraph(rootId, depth ?? 3, columns ?? true);
            return Results.Ok(subgraph);
        }).WithName("GetGraph");

        api.MapGet("/nodes/{**id}", (string id, string? traceId, string? scope, GraphQueryService queries, ITraceStore traces) =>
            Results.Ok(NodeDetails(Uri.UnescapeDataString(id), traceId, scope, queries, traces)))
           .WithName("GetNode");

        api.MapGet("/node", (string id, string? traceId, string? scope, GraphQueryService queries, ITraceStore traces) =>
            Results.Ok(NodeDetails(id, traceId, scope, queries, traces)))
           .WithName("GetNodeByQuery");

        api.MapGet("/search", (string q, int? limit, IGraphRepository graph) =>
        {
            var term = q.Trim();
            var hits = graph.Nodes
                .Where(n => n.Id.Contains(term, StringComparison.OrdinalIgnoreCase) || n.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || (n.QualifiedName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                .OrderBy(n => n.Name.Length)
                .ThenBy(n => n.Id, StringComparer.Ordinal)
                .Take(Math.Clamp(limit ?? 20, 1, 100))
                .ToList();
            return Results.Ok(hits);
        }).WithName("SearchNodes");
    }

    private static NodeDetailsResponse NodeDetails(string id, string? traceId, string? scope, GraphQueryService queries, ITraceStore traces)
    {
        var details = queries.GetNodeDetails(id);
        var runtime = traceId is null ? null : traces.Find(traceId);
        var values = runtime is null
            ? []
            : runtime.Evidence
                .Where(e => e.NodeId == id && (scope is null || e.Scope is null || string.Equals(e.Scope, scope, StringComparison.OrdinalIgnoreCase)))
                .Select(e => new RuntimeValue(e.Id, e.Scope, e.Label, e.Value, e.EvidenceType.ToString()))
                .ToList();
        return new NodeDetailsResponse(details, values);
    }

    // ------------------------------------------------------------------ trace & impact

    private static void MapTrace(RouteGroupBuilder api)
    {
        api.MapGet("/trace/field", (string field, string? traceId, string? scope, FieldTraceService tracer, ITraceStore traces) =>
        {
            var runtime = ResolveTrace(traceId, traces);
            return Results.Ok(tracer.Trace(field, runtime, scope));
        }).WithName("TraceField");

        api.MapGet("/impact/{**id}", (string id, int? depth, ImpactService impact) =>
            Results.Ok(impact.Analyze(Uri.UnescapeDataString(id), depth ?? ImpactService.MaxDepth)))
           .WithName("Impact");

        api.MapGet("/impact", (string id, int? depth, ImpactService impact) =>
            Results.Ok(impact.Analyze(id, depth ?? ImpactService.MaxDepth)))
           .WithName("ImpactByQuery");
    }

    // ------------------------------------------------------------------ runtime

    private static void MapRuntime(RouteGroupBuilder api)
    {
        api.MapPost("/runtime/pick-order", async Task<IResult> (PickOrderRequestBody body, RuntimeToolGateway gateway, ITraceStore traces, CancellationToken ct) =>
        {
            var arguments = new Dictionary<string, string>(StringComparer.Ordinal) { ["orderNo"] = body.OrderNo ?? string.Empty };
            if (!string.IsNullOrWhiteSpace(body.Facility))
            {
                arguments["facility"] = body.Facility;
            }

            if (!string.IsNullOrWhiteSpace(body.PickGroup))
            {
                arguments["pickGroup"] = body.PickGroup;
            }

            ToolResult result;
            if (!string.IsNullOrWhiteSpace(body.MaterialNo))
            {
                arguments["materialNo"] = body.MaterialNo;
                result = await gateway.InvokeAsync(new ToolCall(RuntimeToolGateway.TraceMaterial, arguments), "ui", ct);
            }
            else
            {
                result = await gateway.InvokeAsync(new ToolCall(RuntimeToolGateway.TracePickOrder, arguments), "ui", ct);
            }

            if (!result.Allowed)
            {
                return Results.Problem(title: "Runtime operation denied", detail: result.DenyReason, statusCode: StatusCodes.Status403Forbidden, extensions: new Dictionary<string, object?> { ["auditId"] = result.AuditId });
            }

            var traceId = result.Result?.TryGetProperty("traceId", out var t) == true ? t.GetString() : null;
            var trace = traceId is null ? null : traces.Find(traceId);
            return trace is null
                ? Results.NotFound(new ProblemDetails { Title = "No runtime data", Detail = result.Result?.TryGetProperty("unknown", out var u) == true ? u.GetString() : "The runtime adapter returned no trace.", Status = 404 })
                : Results.Ok(trace);
        }).WithName("TracePickOrder");

        api.MapGet("/runtime/traces", (ITraceStore traces) => Results.Ok(traces.List().Select(Summarize).ToList()))
           .WithName("ListTraces");

        api.MapGet("/runtime/traces/{traceId}", Results<Ok<RuntimeTrace>, NotFound<ProblemDetails>> (string traceId, ITraceStore traces) =>
        {
            var trace = traces.Find(traceId);
            return trace is null
                ? TypedResults.NotFound(new ProblemDetails { Title = "Trace not found", Detail = $"Trace '{traceId}' is not stored.", Status = 404 })
                : TypedResults.Ok(trace);
        }).WithName("GetTrace");

        api.MapPost("/runtime/tools", async Task<IResult> (ToolCallBody body, RuntimeToolGateway gateway, CancellationToken ct) =>
        {
            var call = new ToolCall(body.Tool ?? string.Empty, body.Arguments ?? new Dictionary<string, string>(StringComparer.Ordinal));
            var result = await gateway.InvokeAsync(call, "ui", ct);
            return result.Allowed ? Results.Ok(result) : Results.Json(result, statusCode: StatusCodes.Status403Forbidden);
        }).WithName("InvokeTool");

        api.MapGet("/runtime/tools", () => Results.Ok(new { allowed = RuntimeToolGateway.AllowedTools, forbidden = RuntimeToolGateway.ForbiddenTools }))
           .WithName("ListTools");

        api.MapGet("/runtime/audit", (int? count, IToolAuditLog audit) => Results.Ok(audit.Recent(Math.Clamp(count ?? 50, 1, 500))))
           .WithName("ToolAudit");
    }

    // ------------------------------------------------------------------ AI

    private static void MapAi(RouteGroupBuilder api)
    {
        api.MapPost("/ai/explain", async Task<IResult> (AI.Contracts.ExplainRequest request, FieldTraceService tracer, ITraceStore traces, EvidenceBundleBuilder bundles, IAiInvestigator investigator, AiOptions options, CancellationToken ct) =>
        {
            var runtime = ResolveTrace(request.TraceId, traces);
            var trace = tracer.Trace(request.FocusNodeId, runtime, request.Scope);
            var bundle = bundles.Build(trace, runtime, request.Question, request.AllowedEvidenceIds, options.MaxEvidenceItems, request.Language);
            var explanation = await investigator.ExplainAsync(bundle, ct);
            return Results.Ok(new ExplainResponse(explanation, bundle.Items, trace.Field.Id, bundle.TraceId, bundle.Scope));
        }).WithName("Explain");

        api.MapPost("/ai/investigate", async Task<IResult> (AI.Contracts.InvestigateRequest request, FieldTraceService tracer, ITraceStore traces, EvidenceBundleBuilder bundles, IAiInvestigator investigator, AiOptions options, GraphBootstrapper bootstrapper, CancellationToken ct) =>
        {
            var runtime = traces.Find(request.TraceId)
                ?? throw new Runtime.RuntimeDataUnavailableException($"Trace '{request.TraceId}' is not available; run a Live Trace first.");
            var focus = request.FocusNodeId
                ?? (bootstrapper.Case is { KeyFields.Count: > 0 } c ? c.KeyFields[0] : null)
                ?? throw new ArgumentException("focusNodeId is required.");
            var trace = tracer.Trace(focus, runtime, request.Scope);
            var bundle = bundles.Build(trace, runtime, request.Question, request.AllowedEvidenceIds, options.MaxEvidenceItems, request.Language);
            var explanation = await investigator.InvestigateAsync(bundle, ct);
            return Results.Ok(new ExplainResponse(explanation, bundle.Items, trace.Field.Id, bundle.TraceId, bundle.Scope));
        }).WithName("Investigate");

        api.MapPost("/ai/impact-summary", async Task<IResult> (ImpactSummaryBody body, ImpactService impacts, IAiInvestigator investigator, CancellationToken ct) =>
        {
            var impact = impacts.Analyze(body.NodeId);
            var explanation = await investigator.SummarizeImpactAsync(impact, body.Language, ct);
            return Results.Ok(new { explanation, impact.Summary, impact.KeyPaths });
        }).WithName("ImpactSummary");
    }

    private static RuntimeTrace? ResolveTrace(string? traceId, ITraceStore traces)
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            return null;
        }

        return traces.Find(traceId) ?? throw new Runtime.RuntimeDataUnavailableException($"Trace '{traceId}' is not available; run a Live Trace first.");
    }

    private static TraceSummary Summarize(RuntimeTrace trace)
        => new(
            trace.TraceId,
            trace.EntityType,
            trace.EntityKey,
            trace.Environment,
            trace.ObservedAt,
            trace.Evidence.Count,
            trace.FixtureId,
            trace.Evidence.Select(e => e.Scope).Where(s => s is not null).Select(s => s!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.Ordinal).ToList());
}
