using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MesXray.Domain.Abstractions;
using MesXray.Domain.Serialization;

namespace MesXray.Runtime.Security;

/// <summary>A request to run one runtime tool, as issued by the UI or the AI Investigator.</summary>
public sealed record ToolCall(string Tool, IReadOnlyDictionary<string, string> Arguments);

/// <summary>Outcome of a tool call. Denied calls carry a reason and never a result.</summary>
public sealed record ToolResult(
    string AuditId,
    string Tool,
    bool Allowed,
    string? DenyReason,
    JsonElement? Result,
    DateTimeOffset Timestamp);

/// <summary>Audit entry for every tool call - allowed or not. Arguments are redacted before being stored.</summary>
public sealed record ToolAuditEntry(
    string AuditId,
    DateTimeOffset Timestamp,
    string Tool,
    bool Allowed,
    string? Reason,
    IReadOnlyDictionary<string, string> Arguments,
    string Caller);

public interface IToolAuditLog
{
    void Record(ToolAuditEntry entry);

    IReadOnlyList<ToolAuditEntry> Recent(int count = 50);
}

public sealed class InMemoryToolAuditLog : IToolAuditLog
{
    private readonly ConcurrentQueue<ToolAuditEntry> _entries = new();

    public void Record(ToolAuditEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > 1000 && _entries.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<ToolAuditEntry> Recent(int count = 50)
        => _entries.Reverse().Take(count).ToList();
}

/// <summary>
/// The whitelist from design §5.2 / §10. Anything that is not one of the four read-only operations is rejected with
/// a reason - including <c>execute_arbitrary_sql</c>, <c>update_system_parameter</c> and <c>update_pick_order</c>.
/// Arguments are validated against a conservative character set so no SQL or path fragments can pass through.
/// </summary>
public sealed partial class RuntimeToolGateway
{
    public const string TracePickOrder = "trace_pick_order";
    public const string TraceMaterial = "trace_material";
    public const string ReadSystemParameter = "read_system_parameter";
    public const string ReadPickOrderResponse = "read_pick_order_response";

    public static readonly IReadOnlyList<string> AllowedTools = [TracePickOrder, TraceMaterial, ReadSystemParameter, ReadPickOrderResponse];

    /// <summary>Operations the design explicitly forbids; listed so the deny reason can say why.</summary>
    public static readonly IReadOnlyList<string> ForbiddenTools = ["execute_arbitrary_sql", "execute_sql", "update_system_parameter", "update_pick_order", "delete_pick_order", "write_sql"];

    private static readonly Dictionary<string, string[]> RequiredArguments = new(StringComparer.Ordinal)
    {
        [TracePickOrder] = ["orderNo"],
        [TraceMaterial] = ["orderNo", "materialNo"],
        [ReadSystemParameter] = ["name"],
        [ReadPickOrderResponse] = ["fixtureId"],
    };

    private readonly IRuntimeAdapter _adapter;
    private readonly ITraceStore _traces;
    private readonly IToolAuditLog _audit;
    private readonly IRedactor _redactor;
    private readonly RuntimeOptions _options;
    private readonly TimeProvider _clock;
    private long _sequence;

    public RuntimeToolGateway(IRuntimeAdapter adapter, ITraceStore traces, IToolAuditLog audit, IRedactor redactor, RuntimeOptions options, TimeProvider? clock = null)
    {
        _adapter = adapter;
        _traces = traces;
        _audit = audit;
        _redactor = redactor;
        _options = options;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<ToolResult> InvokeAsync(ToolCall call, string caller = "ui", CancellationToken cancellationToken = default)
    {
        var timestamp = _clock.GetUtcNow();
        var auditId = $"audit-{Interlocked.Increment(ref _sequence).ToString("0000", CultureInfo.InvariantCulture)}";
        var redactedArgs = call.Arguments.ToDictionary(kv => kv.Key, kv => _redactor.IsSensitiveKey(kv.Key) ? Redactor.Mask : _redactor.RedactText(kv.Value), StringComparer.Ordinal);

        var denial = Validate(call);
        if (denial is not null)
        {
            _audit.Record(new ToolAuditEntry(auditId, timestamp, call.Tool, false, denial, redactedArgs, caller));
            return new ToolResult(auditId, call.Tool, false, denial, null, timestamp);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        try
        {
            var result = await ExecuteAsync(call, timeout.Token).ConfigureAwait(false);
            _audit.Record(new ToolAuditEntry(auditId, timestamp, call.Tool, true, null, redactedArgs, caller));
            return new ToolResult(auditId, call.Tool, true, null, result, timestamp);
        }
        catch (RuntimeDataUnavailableException ex)
        {
            _audit.Record(new ToolAuditEntry(auditId, timestamp, call.Tool, true, "no data: " + ex.Message, redactedArgs, caller));
            return new ToolResult(auditId, call.Tool, true, null, JsonSerializer.SerializeToElement(new { unknown = ex.Message }, XRayJson.Options), timestamp);
        }
        catch (RuntimeAccessDeniedException ex)
        {
            _audit.Record(new ToolAuditEntry(auditId, timestamp, call.Tool, false, ex.Message, redactedArgs, caller));
            return new ToolResult(auditId, call.Tool, false, ex.Message, null, timestamp);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var reason = $"Operation exceeded the {_options.Timeout.TotalSeconds:0}s timeout.";
            _audit.Record(new ToolAuditEntry(auditId, timestamp, call.Tool, true, reason, redactedArgs, caller));
            return new ToolResult(auditId, call.Tool, true, null, JsonSerializer.SerializeToElement(new { unknown = reason }, XRayJson.Options), timestamp);
        }
    }

    /// <summary>Returns a deny reason, or null when the call is acceptable.</summary>
    public string? Validate(ToolCall call)
    {
        if (string.IsNullOrWhiteSpace(call.Tool))
        {
            return "Tool name is required.";
        }

        if (ForbiddenTools.Contains(call.Tool, StringComparer.OrdinalIgnoreCase))
        {
            return $"'{call.Tool}' is forbidden by policy: the X-Ray runtime is read-only and never executes arbitrary SQL or writes business data.";
        }

        if (!AllowedTools.Contains(call.Tool, StringComparer.Ordinal))
        {
            return $"'{call.Tool}' is not a whitelisted tool. Allowed: {string.Join(", ", AllowedTools)}.";
        }

        foreach (var required in RequiredArguments[call.Tool])
        {
            if (!call.Arguments.TryGetValue(required, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return $"Argument '{required}' is required for {call.Tool}.";
            }
        }

        foreach (var (name, value) in call.Arguments)
        {
            if (value.Length > _options.MaxArgumentLength)
            {
                return $"Argument '{name}' exceeds {_options.MaxArgumentLength} characters.";
            }

            if (!SafeArgument().IsMatch(value) || value.Contains("..", StringComparison.Ordinal))
            {
                return $"Argument '{name}' contains characters outside the allowed set [A-Za-z0-9 _.:-] (no path separators or '..').";
            }

            if (SqlFragment().IsMatch(value))
            {
                return $"Argument '{name}' looks like a SQL fragment and was rejected.";
            }
        }

        return null;
    }

    private async Task<JsonElement?> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        switch (call.Tool)
        {
            case TracePickOrder:
                {
                    var request = new PickOrderTraceRequest(
                        call.Arguments["orderNo"],
                        call.Arguments.GetValueOrDefault("facility"),
                        call.Arguments.GetValueOrDefault("pickGroup"),
                        User: null);
                    var trace = await _adapter.TracePickOrderAsync(request, cancellationToken).ConfigureAwait(false);
                    _traces.Save(trace);
                    return JsonSerializer.SerializeToElement(new { trace.TraceId, trace.EntityKey, trace.Environment, evidenceCount = trace.Evidence.Count, trace.Unknowns }, XRayJson.Options);
                }

            case TraceMaterial:
                {
                    var trace = await _adapter.TraceMaterialAsync(call.Arguments["orderNo"], call.Arguments["materialNo"], cancellationToken).ConfigureAwait(false);
                    _traces.Save(trace);
                    return JsonSerializer.SerializeToElement(new { trace.TraceId, trace.EntityKey, scope = call.Arguments["materialNo"], evidenceCount = trace.Evidence.Count, trace.Unknowns }, XRayJson.Options);
                }

            case ReadSystemParameter:
                {
                    var evidence = await _adapter.ReadSystemParameterAsync(call.Arguments["name"], cancellationToken).ConfigureAwait(false);
                    return evidence is null
                        ? JsonSerializer.SerializeToElement(new { unknown = $"System parameter '{call.Arguments["name"]}' was not observed." }, XRayJson.Options)
                        : JsonSerializer.SerializeToElement(new { evidence.Id, evidence.NodeId, evidence.Value, evidence.Environment }, XRayJson.Options);
                }

            case ReadPickOrderResponse:
                return await _adapter.ReadPickOrderResponseAsync(call.Arguments["fixtureId"], cancellationToken).ConfigureAwait(false);

            default:
                throw new RuntimeAccessDeniedException($"'{call.Tool}' is not whitelisted.");
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9 _.:\-]+$")]
    private static partial Regex SafeArgument();

    [GeneratedRegex(@"(--|/\*|;|\b(select|insert|update|delete|drop|exec|execute|union|xp_)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex SqlFragment();
}
