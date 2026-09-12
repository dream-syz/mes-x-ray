using System.Text.Json;
using MesXray.AI.Contracts;
using MesXray.AI.Evidence;
using MesXray.AI.Llm;
using MesXray.AI.Prompts;
using MesXray.AI.Validation;
using MesXray.Domain.Serialization;
using MesXray.Graph.Queries;
using Microsoft.Extensions.Logging;

namespace MesXray.AI.Investigators;

/// <summary>
/// LLM-backed investigator: renders the evidence bundle into the prompt, requests structured JSON, and passes the
/// answer through <see cref="EvidenceBindingValidator"/>. Falls back to the rule-based investigator when the model
/// is unavailable so that the demo never depends on network access.
/// </summary>
public sealed class LlmInvestigator : IAiInvestigator
{
    private readonly ILlmClient _client;
    private readonly RuleBasedInvestigator _fallback;
    private readonly EvidenceBindingValidator _validator;
    private readonly AiOptions _options;
    private readonly ILogger<LlmInvestigator> _logger;
    private readonly TimeProvider _clock;

    public LlmInvestigator(ILlmClient client, RuleBasedInvestigator fallback, EvidenceBindingValidator validator, AiOptions options, ILogger<LlmInvestigator> logger, TimeProvider? clock = null)
    {
        _client = client;
        _fallback = fallback;
        _validator = validator;
        _options = options;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public string Provider => AiOptions.OpenAiCompatibleProvider;

    public Task<Explanation> ExplainAsync(EvidenceBundle bundle, CancellationToken cancellationToken = default)
        => RunAsync(bundle, investigate: false, cancellationToken);

    public Task<Explanation> InvestigateAsync(EvidenceBundle bundle, CancellationToken cancellationToken = default)
        => RunAsync(bundle, investigate: true, cancellationToken);

    /// <summary>Impact summaries are purely structural; the rule engine is authoritative.</summary>
    public Task<Explanation> SummarizeImpactAsync(ImpactResult impact, string? language = null, CancellationToken cancellationToken = default)
        => _fallback.SummarizeImpactAsync(impact, language, cancellationToken);

    private async Task<Explanation> RunAsync(EvidenceBundle bundle, bool investigate, CancellationToken cancellationToken)
    {
        try
        {
            var messages = new List<LlmMessage>
            {
                new("system", PromptLibrary.System()),
                new("user", PromptLibrary.User(bundle, investigate)),
            };

            var completion = await _client.CompleteJsonAsync(messages, PromptLibrary.ExplanationSchema(), "xray_explanation", cancellationToken).ConfigureAwait(false);
            var draft = Parse(completion, bundle, investigate);
            var report = _validator.Validate(draft, bundle);
            var downgraded = report.DowngradedFacts.Count;
            if (downgraded > 0)
            {
                _logger.LogInformation("Evidence binding downgraded {Count} fact(s) from the LLM answer.", downgraded);
            }

            return report.Explanation;
        }
        catch (LlmUnavailableException ex) when (_options.FallbackToRules)
        {
            _logger.LogWarning("LLM unavailable ({Reason}); using rule-based investigator.", ex.Message);
            var fallback = investigate
                ? await _fallback.InvestigateAsync(bundle, cancellationToken).ConfigureAwait(false)
                : await _fallback.ExplainAsync(bundle, cancellationToken).ConfigureAwait(false);
            return fallback with { Audit = fallback.Audit with { Note = $"fallback from {_options.Model}: {ex.Message}" } };
        }
    }

    private Explanation Parse(LlmCompletion completion, EvidenceBundle bundle, bool investigate)
    {
        var draft = JsonSerializer.Deserialize<LlmDraft>(completion.Json.GetRawText(), XRayJson.Options)
                    ?? throw new LlmUnavailableException("LLM returned an empty object.");

        return new Explanation
        {
            Summary = draft.Summary ?? string.Empty,
            Steps = draft.Steps ?? [],
            KnownFacts = (draft.KnownFacts ?? []).Select(f => new KnownFact(f.Text ?? string.Empty, f.EvidenceIds ?? [])).ToList(),
            Hypotheses = (draft.Hypotheses ?? []).Select(h => new Hypothesis(h.Text ?? string.Empty, h.Status ?? HypothesisStatus.Unverified, h.SuggestedCheck, h.EvidenceIds)).ToList(),
            Unknowns = draft.Unknowns ?? [],
            NextSteps = draft.NextSteps ?? [],
            Confidence = Math.Clamp(draft.Confidence ?? 0.5, 0, 1),
            Verdict = draft.Verdict ?? ExplainVerdict.NeedMoreEvidence,
            Audit = new AiAudit(Provider, completion.Model, _options.PromptVersion, _clock.GetUtcNow(), [], investigate ? "investigate" : "explain"),
        };
    }

    private sealed record LlmDraft(
        string? Summary,
        List<string>? Steps,
        List<LlmFact>? KnownFacts,
        List<LlmHypothesis>? Hypotheses,
        List<string>? Unknowns,
        List<string>? NextSteps,
        double? Confidence,
        ExplainVerdict? Verdict);

    private sealed record LlmFact(string? Text, List<string>? EvidenceIds);

    private sealed record LlmHypothesis(string? Text, HypothesisStatus? Status, string? SuggestedCheck, List<string>? EvidenceIds);
}
