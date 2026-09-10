using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MesXray.Runtime.Security;

/// <summary>Removes credentials, hosts and personal data from anything that leaves the runtime layer (design §10).</summary>
public interface IRedactor
{
    string RedactText(string text);

    JsonElement Redact(JsonElement element);

    /// <summary>True when the member name itself marks sensitive content (user, token, host...).</summary>
    bool IsSensitiveKey(string key);
}

/// <summary>Rule-based redactor: sensitive member names are blanked, well-known secret shapes are masked in text.</summary>
public sealed partial class Redactor : IRedactor
{
    public const string Mask = "[redacted]";

    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "user", "username", "userName", "userid", "user_id", "login", "operator", "createdBy", "modifiedBy",
        "password", "pwd", "secret", "token", "accessToken", "refreshToken", "jwt", "authorization", "apiKey", "api_key",
        "host", "hostname", "server", "dataSource", "connectionString",
        "email", "mail", "phone", "mobile", "ip", "ipAddress",
    };

    public bool IsSensitiveKey(string key) => SensitiveKeys.Contains(key);

    public string RedactText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = Jwt().Replace(text, Mask);
        result = Bearer().Replace(result, "Bearer " + Mask);
        result = KeyValueSecret().Replace(result, m => $"{m.Groups[1].Value}={Mask}");
        result = IPv4().Replace(result, Mask);
        result = InternalHost().Replace(result, Mask);
        result = Email().Replace(result, Mask);
        return result;
    }

    public JsonElement Redact(JsonElement element)
    {
        var node = JsonNode.Parse(element.GetRawText());
        var redacted = RedactNode(node, parentKey: null);
        return redacted is null
            ? JsonDocument.Parse("null").RootElement.Clone()
            : JsonDocument.Parse(redacted.ToJsonString()).RootElement.Clone();
    }

    private JsonNode? RedactNode(JsonNode? node, string? parentKey)
    {
        switch (node)
        {
            case JsonObject obj:
                {
                    var copy = new JsonObject();
                    foreach (var (key, value) in obj)
                    {
                        copy[key] = IsSensitiveKey(key) && value is JsonValue
                            ? JsonValue.Create(Mask)
                            : RedactNode(value, key);
                    }

                    return copy;
                }

            case JsonArray array:
                {
                    var copy = new JsonArray();
                    foreach (var item in array)
                    {
                        copy.Add(RedactNode(item, parentKey));
                    }

                    return copy;
                }

            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(RedactText(text));

            default:
                return node?.DeepClone();
        }
    }

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._\-]+", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"\b(password|pwd|user id|uid|user|token|secret|api[_-]?key|server|data source|host)\s*=\s*[^;\s,]+", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueSecret();

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")]
    private static partial Regex IPv4();

    [GeneratedRegex(@"\b[\w-]+(?:\.[\w-]+)*\.(?:corp|internal|local|intranet|lan)\b", RegexOptions.IgnoreCase)]
    private static partial Regex InternalHost();

    [GeneratedRegex(@"\b[\w.+-]+@[\w-]+(?:\.[\w-]+)+\b")]
    private static partial Regex Email();
}
