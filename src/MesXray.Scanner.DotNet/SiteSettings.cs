using System.Text.Json;
using System.Text.Json.Serialization;
using MesXray.Domain.Serialization;

namespace MesXray.Scanner.DotNet;

/// <summary>
/// One configuration value of the site under study, as bound to an options property at runtime. Values here are
/// evidence of the <c>Configuration</c> kind: the scanner uses them to resolve names the code only knows at runtime
/// (a Dapper call whose procedure name comes from an options property), citing the setting instead of guessing.
/// </summary>
/// <param name="Value">The configured value, e.g. <c>dbo.AP_Pick_GetPutStorageBin</c>.</param>
/// <param name="Provided">When the value was provided (free text, e.g. a date), for the audit trail.</param>
/// <param name="Note">Free-text context, e.g. which gap the value closes.</param>
public sealed record SiteSetting(string Value, string? Provided = null, string? Note = null);

/// <summary>
/// Sanitised configuration of one site, keyed by the options property that receives each value
/// (<c>OptionsType.Property</c>, e.g. <c>PickingOptions.StorageBinProcedure</c>). Read from
/// <c>source/config/site-settings.json</c> of a case; absent settings simply leave runtime-computed names Unknown.
/// </summary>
public sealed class SiteSettings
{
    public static SiteSettings Empty { get; } = new(string.Empty, new Dictionary<string, SiteSetting>(StringComparer.Ordinal));

    private readonly IReadOnlyDictionary<string, SiteSetting> _values;

    public SiteSettings(string source, IReadOnlyDictionary<string, SiteSetting> values)
    {
        Source = source;
        _values = values;
    }

    /// <summary>How the settings file is cited in evidence references (path relative to the case's source folder).</summary>
    public string Source { get; }

    public int Count => _values.Count;

    public bool TryGet(string optionsType, string property, out SiteSetting setting)
        => _values.TryGetValue(Key(optionsType, property), out setting!);

    public static string Key(string optionsType, string property) => $"{optionsType}.{property}";

    /// <summary>Evidence reference of one setting: <c>config/site-settings.json#PickingOptions.StorageBinProcedure</c>.</summary>
    public string EvidenceRef(string key) => $"{Source}#{key}";

    /// <summary>Reads a settings file; a missing file yields <see cref="Empty"/> so cases without configuration input keep working.</summary>
    public static SiteSettings ReadIfExists(string path, string source)
    {
        if (!File.Exists(path))
        {
            return Empty;
        }

        using var stream = File.OpenRead(path);
        var file = JsonSerializer.Deserialize<SiteSettingsFile>(stream, XRayJson.Options)
                   ?? throw new InvalidDataException($"Site settings file '{path}' is empty.");
        var values = new Dictionary<string, SiteSetting>(StringComparer.Ordinal);
        foreach (var (key, entry) in file.Values)
        {
            if (string.IsNullOrWhiteSpace(entry.Value))
            {
                throw new InvalidDataException($"Site setting '{key}' in '{path}' has no value.");
            }

            values[key] = entry;
        }

        return new SiteSettings(source, values);
    }

    private sealed class SiteSettingsFile
    {
        [JsonPropertyName("values")]
        public Dictionary<string, SiteSetting> Values { get; init; } = new(StringComparer.Ordinal);
    }
}
