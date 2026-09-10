namespace MesXray.AI;

/// <summary>
/// AI Investigator configuration. The API key is never stored here: only the name of the environment variable that
/// holds it, so that neither appsettings nor Git ever contain a credential.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "XRay:AI";

    public const string RulesProvider = "rules";
    public const string OpenAiCompatibleProvider = "openai-compatible";

    /// <summary><c>rules</c> (offline, deterministic - default) or <c>openai-compatible</c>.</summary>
    public string Provider { get; set; } = RulesProvider;

    /// <summary>Base URL of an OpenAI-compatible chat completions endpoint, e.g. an internal gateway approved by data classification policy.</summary>
    public string? BaseUrl { get; set; }

    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Environment variable holding the API key.</summary>
    public string ApiKeyEnvironmentVariable { get; set; } = "MESXRAY_AI_API_KEY";

    /// <summary>Version stamp of the prompt templates; recorded in every audit record.</summary>
    public string PromptVersion { get; set; } = "explain-v1";

    /// <summary>Upper bound on evidence items passed to the model.</summary>
    public int MaxEvidenceItems { get; set; } = 120;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>When the LLM fails (no key, network, invalid output) fall back to the rule-based investigator instead of erroring.</summary>
    public bool FallbackToRules { get; set; } = true;
}
