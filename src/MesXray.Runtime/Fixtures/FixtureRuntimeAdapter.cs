using System.Text.Json;
using MesXray.Domain.Abstractions;
using MesXray.Domain.Evidence;
using MesXray.Domain.Graph;
using MesXray.Domain.Serialization;
using MesXray.Runtime.Security;

namespace MesXray.Runtime.Fixtures;

/// <summary>
/// Runtime Adapter backed by sanitised fixtures. Implements exactly the whitelisted operations of
/// <see cref="IRuntimeAdapter"/>; refuses fixtures recorded in a non-allowed environment. The same contract can later be
/// implemented against a read-only TEST/UAT database without touching callers.
/// </summary>
public sealed class FixtureRuntimeAdapter : IRuntimeAdapter
{
    private readonly RuntimeOptions _options;
    private readonly EvidenceBinder _binder;
    private readonly IReadOnlyList<RuntimeFixture> _fixtures;

    public FixtureRuntimeAdapter(RuntimeOptions options, IRedactor redactor)
    {
        _options = options;
        _binder = new EvidenceBinder(redactor, options);
        _fixtures = LoadFixtures(options.FixtureRoot);
    }

    public IReadOnlyList<RuntimeFixture> Fixtures => _fixtures;

    public Task<RuntimeTrace> TracePickOrderAsync(PickOrderTraceRequest request, CancellationToken cancellationToken = default)
    {
        var fixture = FindByOrder(request.OrderNo)
            ?? throw new RuntimeDataUnavailableException($"No fixture is recorded for pick order '{request.OrderNo}'. Import a sanitised fixture or connect a TEST/UAT adapter.");

        EnsureEnvironmentAllowed(fixture);
        return Task.FromResult(_binder.Bind(fixture));
    }

    public Task<RuntimeTrace> TraceMaterialAsync(string orderNo, string materialNo, CancellationToken cancellationToken = default)
    {
        var fixture = FindByOrder(orderNo)
            ?? throw new RuntimeDataUnavailableException($"No fixture is recorded for pick order '{orderNo}'.");

        EnsureEnvironmentAllowed(fixture);
        var trace = _binder.Bind(fixture, traceIdOverride: $"{fixture.TraceId ?? fixture.FixtureId}:{materialNo}");
        var scoped = trace.Evidence
            .Where(e => e.Scope is null || string.Equals(e.Scope, materialNo, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var unknowns = trace.Unknowns.ToList();
        if (!scoped.Any(e => string.Equals(e.Scope, materialNo, StringComparison.OrdinalIgnoreCase)))
        {
            unknowns.Add($"Material '{materialNo}' does not appear in the recorded trace for '{orderNo}'.");
        }

        return Task.FromResult(trace with { Evidence = scoped, Unknowns = unknowns });
    }

    public Task<RuntimeEvidence?> ReadSystemParameterAsync(string name, CancellationToken cancellationToken = default)
    {
        var nodeId = NodeIds.SystemParameter(name);
        var evidence = _fixtures
            .Where(f => IsEnvironmentAllowed(f) && f.SystemParameters.ContainsKey(name))
            .Select(f => _binder.Bind(f).Evidence.FirstOrDefault(e => e.NodeId == nodeId))
            .FirstOrDefault(e => e is not null);

        return Task.FromResult(evidence);
    }

    public Task<JsonElement?> ReadPickOrderResponseAsync(string fixtureId, CancellationToken cancellationToken = default)
    {
        var fixture = _fixtures.FirstOrDefault(f => string.Equals(f.FixtureId, fixtureId, StringComparison.OrdinalIgnoreCase));
        if (fixture is null)
        {
            return Task.FromResult<JsonElement?>(null);
        }

        EnsureEnvironmentAllowed(fixture);
        return Task.FromResult(_binder.Bind(fixture).Response);
    }

    /// <summary>Traces pre-registered by fixtures (so a demo can reference <c>trace-demo-001</c> before any request).</summary>
    public IEnumerable<RuntimeTrace> PreloadedTraces()
        => _fixtures.Where(f => f.TraceId is not null && IsEnvironmentAllowed(f)).Select(f => _binder.Bind(f));

    private RuntimeFixture? FindByOrder(string orderNo)
        => _fixtures.FirstOrDefault(f => string.Equals(f.EntityKey, orderNo.Trim(), StringComparison.OrdinalIgnoreCase));

    private bool IsEnvironmentAllowed(RuntimeFixture fixture)
        => _options.AllowedEnvironments.Contains(fixture.Environment, StringComparer.OrdinalIgnoreCase);

    private void EnsureEnvironmentAllowed(RuntimeFixture fixture)
    {
        if (!IsEnvironmentAllowed(fixture))
        {
            throw new RuntimeAccessDeniedException($"Environment '{fixture.Environment}' is not in the allowed list ({string.Join(", ", _options.AllowedEnvironments)}).");
        }
    }

    private static List<RuntimeFixture> LoadFixtures(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }

        var fixtures = new List<RuntimeFixture>();
        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.Ordinal))
        {
            using var stream = File.OpenRead(file);
            var fixture = JsonSerializer.Deserialize<RuntimeFixture>(stream, XRayJson.Options);
            if (fixture is not null)
            {
                fixtures.Add(fixture);
            }
        }

        return fixtures;
    }
}
