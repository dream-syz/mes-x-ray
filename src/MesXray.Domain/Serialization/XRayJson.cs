using System.Text.Json;
using System.Text.Json.Serialization;

namespace MesXray.Domain.Serialization;

/// <summary>
/// The one JSON dialect used for fixtures, snapshots and the HTTP API: camelCase members, string enums, nulls omitted.
/// Keeping this in the domain guarantees the ground-truth files and the API speak the same language.
/// </summary>
public static class XRayJson
{
    public static JsonSerializerOptions Options { get; } = Create(indented: false);

    public static JsonSerializerOptions Indented { get; } = Create(indented: true);

    public static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static void Apply(JsonSerializerOptions target)
    {
        target.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        target.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        target.PropertyNameCaseInsensitive = true;
        target.ReadCommentHandling = JsonCommentHandling.Skip;
        target.AllowTrailingCommas = true;
        if (!target.Converters.Any(c => c is JsonStringEnumConverter))
        {
            target.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        }
    }
}
