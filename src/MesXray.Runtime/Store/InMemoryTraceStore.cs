using System.Collections.Concurrent;
using MesXray.Domain.Abstractions;
using MesXray.Domain.Evidence;

namespace MesXray.Runtime.Store;

/// <summary>Process-local trace store; sufficient for the POC (traces are re-creatable from fixtures).</summary>
public sealed class InMemoryTraceStore : ITraceStore
{
    private readonly ConcurrentDictionary<string, RuntimeTrace> _traces = new(StringComparer.OrdinalIgnoreCase);

    public void Save(RuntimeTrace trace) => _traces[trace.TraceId] = trace;

    public RuntimeTrace? Find(string traceId) => _traces.GetValueOrDefault(traceId);

    public IReadOnlyList<RuntimeTrace> List()
        => _traces.Values.OrderByDescending(t => t.ObservedAt).ThenBy(t => t.TraceId, StringComparer.Ordinal).ToList();
}
