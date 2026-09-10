using System.Text.Json;

namespace MesXray.AI.Llm;

/// <summary>A chat message. Only <c>system</c> and <c>user</c> roles are used.</summary>
public sealed record LlmMessage(string Role, string Content);

/// <summary>Result of one structured-output completion.</summary>
public sealed record LlmCompletion(JsonElement Json, string Model, int? PromptTokens, int? CompletionTokens);

/// <summary>Minimal LLM abstraction: structured JSON output for a fixed schema. No tools, no memory.</summary>
public interface ILlmClient
{
    string Model { get; }

    bool IsConfigured { get; }

    Task<LlmCompletion> CompleteJsonAsync(IReadOnlyList<LlmMessage> messages, JsonElement jsonSchema, string schemaName, CancellationToken cancellationToken = default);
}

/// <summary>The LLM provider is not configured or returned an unusable answer.</summary>
public sealed class LlmUnavailableException : Exception
{
    public LlmUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
