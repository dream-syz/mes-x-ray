using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MesXray.AI.Llm;

/// <summary>
/// Chat-completions client for any OpenAI-compatible endpoint (public API or an internal gateway approved by the
/// company's data classification policy). The key is read from the environment variable named in
/// <see cref="AiOptions.ApiKeyEnvironmentVariable"/> and never logged or echoed.
/// </summary>
public sealed class OpenAiCompatibleLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly AiOptions _options;
    private readonly string? _apiKey;

    public OpenAiCompatibleLlmClient(HttpClient http, AiOptions options)
    {
        _http = http;
        _options = options;
        _apiKey = Environment.GetEnvironmentVariable(options.ApiKeyEnvironmentVariable);
        _http.Timeout = options.Timeout;
    }

    public string Model => _options.Model;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_options.BaseUrl);

    public async Task<LlmCompletion> CompleteJsonAsync(IReadOnlyList<LlmMessage> messages, JsonElement jsonSchema, string schemaName, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new LlmUnavailableException($"LLM provider is not configured: set XRay:AI:BaseUrl and the {_options.ApiKeyEnvironmentVariable} environment variable.");
        }

        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["temperature"] = 0,
            ["messages"] = new JsonArray(messages.Select(m => (JsonNode)new JsonObject { ["role"] = m.Role, ["content"] = m.Content }).ToArray()),
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = schemaName,
                    ["strict"] = false,
                    ["schema"] = JsonNode.Parse(jsonSchema.GetRawText()),
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_options.BaseUrl!.TrimEnd('/') + "/"), "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = JsonContent.Create(body);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmUnavailableException("LLM endpoint unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LlmUnavailableException("LLM request timed out.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new LlmUnavailableException($"LLM endpoint returned HTTP {(int)response.StatusCode}.");
            }

            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            var content = root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
                ? choices[0].GetProperty("message").GetProperty("content").GetString()
                : null;

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new LlmUnavailableException("LLM returned an empty completion.");
            }

            JsonElement json;
            try
            {
                using var parsed = JsonDocument.Parse(content);
                json = parsed.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new LlmUnavailableException("LLM did not return valid JSON.", ex);
            }

            int? promptTokens = null;
            int? completionTokens = null;
            if (root.TryGetProperty("usage", out var usage))
            {
                promptTokens = usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : null;
                completionTokens = usage.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : null;
            }

            var model = root.TryGetProperty("model", out var m) ? m.GetString() ?? _options.Model : _options.Model;
            return new LlmCompletion(json, model, promptTokens, completionTokens);
        }
    }
}
