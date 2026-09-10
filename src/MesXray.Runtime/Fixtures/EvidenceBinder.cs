using System.Globalization;
using System.Text.Json;
using MesXray.Domain.Evidence;
using MesXray.Domain.Graph;
using MesXray.Runtime.Security;

namespace MesXray.Runtime.Fixtures;

/// <summary>
/// Turns a runtime fixture into <see cref="RuntimeEvidence"/> items bound to graph node ids:
/// <c>param:*</c> for system parameters, <c>spcol:*</c> / <c>sp:*</c> for procedure rows and timings, and
/// <c>json:*</c> for every leaf of the API response. Evidence ids are deterministic (<c>ev-001</c>, ...) so that AI
/// conclusions can cite them reproducibly.
/// </summary>
public sealed class EvidenceBinder
{
    private readonly IRedactor _redactor;
    private readonly RuntimeOptions _options;

    public EvidenceBinder(IRedactor redactor, RuntimeOptions options)
    {
        _redactor = redactor;
        _options = options;
    }

    public RuntimeTrace Bind(RuntimeFixture fixture, string? traceIdOverride = null)
    {
        var traceId = traceIdOverride ?? fixture.TraceId ?? $"trace-{fixture.FixtureId}";
        var evidence = new List<RuntimeEvidence>();
        var unknowns = new List<string>(fixture.Unknowns);
        var counter = 0;

        RuntimeEvidence Add(string nodeId, RuntimeEvidenceType type, JsonElement value, string? scope, string? label)
        {
            counter++;
            var item = new RuntimeEvidence
            {
                Id = $"ev-{counter.ToString("000", CultureInfo.InvariantCulture)}",
                TraceId = traceId,
                EntityType = fixture.EntityType,
                EntityKey = fixture.EntityKey,
                NodeId = nodeId,
                EvidenceType = type,
                Value = _redactor.Redact(value),
                Scope = scope,
                Label = label,
                ObservedAt = fixture.ObservedAt,
                Environment = fixture.Environment,
            };
            evidence.Add(item);
            return item;
        }

        foreach (var (name, value) in fixture.SystemParameters.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            Add(NodeIds.SystemParameter(name), RuntimeEvidenceType.Parameter, Scalar(value), scope: null, label: name);
        }

        foreach (var (procedure, result) in fixture.ProcedureResults.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var ownerKey = procedure.Trim('[', ']');
            var spId = $"sp:{ownerKey}";
            if (result.DurationMs is { } duration)
            {
                Add(spId, RuntimeEvidenceType.Timing, Scalar(duration), scope: null, label: $"{ownerKey} {duration} ms");
            }

            var rows = result.Rows;
            if (rows.Count > _options.MaxRows)
            {
                unknowns.Add($"{ownerKey}: {rows.Count - _options.MaxRows} rows beyond the {_options.MaxRows}-row limit were not captured.");
                rows = rows.Take(_options.MaxRows).ToList();
            }

            foreach (var row in rows)
            {
                string? scope = null;
                if (result.RowKey is not null && row.TryGetValue(result.RowKey, out var key))
                {
                    var keyText = key.ValueKind == JsonValueKind.String ? key.GetString() : key.GetRawText();
                    scope = string.Equals(keyText, fixture.EntityKey, StringComparison.OrdinalIgnoreCase) ? null : keyText;
                }

                foreach (var (column, value) in row.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    Add(NodeIds.ResultColumn(ownerKey, column), RuntimeEvidenceType.QueryResult, value, scope,
                        scope is null ? column : $"{scope}.{column}");
                }
            }
        }

        JsonElement? response = null;
        if (fixture.Response is { } rawResponse)
        {
            response = _redactor.Redact(rawResponse);
            BindResponse(response.Value, path: string.Empty, scope: null, Add);
        }

        return new RuntimeTrace
        {
            TraceId = traceId,
            EntityType = fixture.EntityType,
            EntityKey = fixture.EntityKey,
            Environment = fixture.Environment,
            ObservedAt = fixture.ObservedAt,
            Response = response,
            Evidence = evidence,
            Unknowns = unknowns,
            FixtureId = fixture.FixtureId,
        };
    }

    private void BindResponse(JsonElement element, string path, string? scope, Func<string, RuntimeEvidenceType, JsonElement, string?, string?, RuntimeEvidence> add)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var itemScope = scope;
                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("materialNumber", out var material) && material.ValueKind == JsonValueKind.String)
                    {
                        itemScope = material.GetString();
                    }

                    BindResponse(item, path, itemScope, add);
                }

                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (_redactor.IsSensitiveKey(property.Name))
                    {
                        continue;
                    }

                    var childPath = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
                    BindResponse(property.Value, childPath, scope, add);
                }

                break;

            default:
                if (path.Length > 0)
                {
                    add(NodeIds.Json(path), RuntimeEvidenceType.ApiResponse, element, scope, scope is null ? path : $"{scope}.{NodeIds.LeafName(NodeIds.Json(path))}");
                }

                break;
        }
    }

    private static JsonElement Scalar<T>(T value)
        => JsonSerializer.SerializeToElement(value);
}
