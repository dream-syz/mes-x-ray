using System.Text.Json;
using MesXray.Domain.Evidence;

namespace MesXray.Domain.Abstractions;

/// <summary>Input for the whitelisted <c>trace_pick_order</c> operation. Free text is validated before use.</summary>
public sealed record PickOrderTraceRequest(string OrderNo, string? Facility, string? PickGroup, string? User);

/// <summary>
/// The only way the system (including the AI Investigator) can touch runtime data. Every member corresponds to one
/// whitelisted, read-only operation from the design document. There is deliberately no "execute SQL" member.
/// </summary>
public interface IRuntimeAdapter
{
    /// <summary>Whitelisted operation <c>trace_pick_order(orderNo, facility, pickGroup, user)</c>.</summary>
    Task<RuntimeTrace> TracePickOrderAsync(PickOrderTraceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Whitelisted operation <c>trace_material(orderNo, materialNo)</c>: evidence scoped to one material row.</summary>
    Task<RuntimeTrace> TraceMaterialAsync(string orderNo, string materialNo, CancellationToken cancellationToken = default);

    /// <summary>Whitelisted operation <c>read_system_parameter(name)</c>.</summary>
    Task<RuntimeEvidence?> ReadSystemParameterAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Whitelisted operation <c>read_pick_order_response(fixtureId)</c>.</summary>
    Task<JsonElement?> ReadPickOrderResponseAsync(string fixtureId, CancellationToken cancellationToken = default);
}

/// <summary>Stores completed traces so that Explain/Investigate can reference them by trace id.</summary>
public interface ITraceStore
{
    void Save(RuntimeTrace trace);

    RuntimeTrace? Find(string traceId);

    IReadOnlyList<RuntimeTrace> List();
}
