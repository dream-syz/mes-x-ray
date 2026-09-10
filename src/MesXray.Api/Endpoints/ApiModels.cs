using System.Text.Json;
using MesXray.AI.Contracts;
using MesXray.AI.Evidence;
using MesXray.Api.Bootstrap;
using MesXray.Domain.Graph;
using MesXray.Graph.Cases;

namespace MesXray.Api.Endpoints;

/// <summary>A known gap with the current status of the node it refers to.</summary>
public sealed record GapStatus(string NodeId, string Reason, string Priority, NodeStatus Status, string? Name);

/// <summary>A trace available for Live Trace overlay.</summary>
public sealed record TraceSummary(string TraceId, string EntityType, string EntityKey, string Environment, DateTimeOffset ObservedAt, int EvidenceCount, string? FixtureId, IReadOnlyList<string> Scopes);

/// <summary>Response of <c>GET /api/xray/cases/{caseId}</c>.</summary>
public sealed record CaseOverview(
    CaseDefinition Case,
    IReadOnlyList<Node> KeyFields,
    IReadOnlyList<Node> SystemParameters,
    IReadOnlyList<GapStatus> KnownGaps,
    IReadOnlyList<TraceSummary> Traces,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<string> ForbiddenTools,
    GraphBuildReport? Build);

/// <summary>Body of <c>POST /api/xray/runtime/pick-order</c>.</summary>
public sealed record PickOrderRequestBody(string OrderNo, string? Facility = null, string? PickGroup = null, string? MaterialNo = null);

/// <summary>Body of <c>POST /api/xray/runtime/tools</c>.</summary>
public sealed record ToolCallBody(string Tool, Dictionary<string, string>? Arguments = null);

/// <summary>Body of <c>POST /api/xray/ai/impact-summary</c>.</summary>
public sealed record ImpactSummaryBody(string NodeId);

/// <summary>Explanation plus the evidence the AI was allowed to cite, so the UI can render evidence chips.</summary>
public sealed record ExplainResponse(Explanation Explanation, IReadOnlyList<EvidenceItem> Evidence, string FocusNodeId, string? TraceId, string? Scope);

/// <summary>Node details with optional runtime values from a trace.</summary>
public sealed record NodeDetailsResponse(Graph.Queries.NodeDetails Details, IReadOnlyList<Graph.Queries.RuntimeValue> RuntimeValues);

public static class JsonDefaults
{
    public static JsonElement Empty => JsonDocument.Parse("{}").RootElement.Clone();
}
