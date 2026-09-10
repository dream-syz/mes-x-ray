using System.Text.Json;
using MesXray.AI;
using MesXray.AI.Contracts;
using MesXray.AI.Evidence;
using MesXray.AI.Investigators;
using MesXray.AI.Llm;
using MesXray.AI.Validation;
using Microsoft.Extensions.Logging.Abstractions;

namespace MesXray.Graph.Tests;

/// <summary>Design §9 / AC-03 / AC-06: evidence-bound explanations, hypotheses for gaps, and validator enforcement.</summary>
public sealed class AiInvestigatorTests : IClassFixture<AssembledGraphFixture>
{
    private readonly AssembledGraphFixture _fx;
    private readonly AiOptions _options = new();

    public AiInvestigatorTests(AssembledGraphFixture fx)
    {
        _fx = fx;
    }

    private EvidenceBundle Bundle(string field, string? question = null, string? scope = "T12288", bool withTrace = true, IReadOnlyList<string>? allowed = null)
    {
        var runtime = withTrace ? _fx.DemoTrace : null;
        var trace = _fx.Tracer.Trace(field, runtime, withTrace ? scope : null);
        return new EvidenceBundleBuilder(_fx.Store).Build(trace, runtime, question, allowed, _options.MaxEvidenceItems);
    }

    [Fact]
    public async Task AC03_explain_availableQuantity_names_the_udf_and_marks_it_unknown()
    {
        var investigator = new RuleBasedInvestigator(_options);
        var explanation = await investigator.ExplainAsync(Bundle("availableQuantity", "Why is Available Quantity 0?"));

        Assert.Equal(ExplainVerdict.NeedMoreEvidence, explanation.Verdict);
        Assert.Contains(explanation.KnownFacts, f => f.Text.Contains("WMS_Enabled = 1", StringComparison.Ordinal) && f.Text.Contains("Active branch", StringComparison.Ordinal));
        Assert.Contains(explanation.KnownFacts, f => f.Text.Contains("AF_Pick_GetAvailableQuantity", StringComparison.Ordinal));
        Assert.Contains(explanation.KnownFacts, f => f.Text.Contains("availableQuantity = 0", StringComparison.Ordinal));
        Assert.Contains(explanation.Hypotheses, h => h.Text.Contains("AF_Pick_GetAvailableQuantity", StringComparison.Ordinal) && h.Status == HypothesisStatus.Unverified && h.SuggestedCheck is not null);
        Assert.Contains(explanation.Unknowns, u => u.Contains("AF_Pick_GetAvailableQuantity", StringComparison.Ordinal));
        Assert.InRange(explanation.Confidence, 0.3, 0.9);
        Assert.Equal(RuleBasedInvestigator.ProviderName, explanation.Audit.Provider);
        Assert.NotEmpty(explanation.Audit.EvidenceIds);
        Assert.Equal(_options.PromptVersion, explanation.Audit.PromptVersion);
        Assert.DoesNotContain(explanation.Steps, s => s.Contains("I think", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Every_known_fact_cites_only_evidence_from_the_bundle()
    {
        var bundle = Bundle("availableQuantity");
        var explanation = await new RuleBasedInvestigator(_options).ExplainAsync(bundle);

        Assert.NotEmpty(explanation.KnownFacts);
        foreach (var fact in explanation.KnownFacts)
        {
            Assert.NotEmpty(fact.EvidenceIds);
            Assert.All(fact.EvidenceIds, id => Assert.Contains(id, bundle.AllowedEvidenceIds));
        }

        Assert.All(explanation.Audit.EvidenceIds, id => Assert.Contains(id, bundle.AllowedEvidenceIds));
    }

    [Fact]
    public async Task Investigate_adds_the_isnull_fallback_hypothesis_when_the_observed_value_equals_the_default()
    {
        var explanation = await new RuleBasedInvestigator(_options).InvestigateAsync(Bundle("availableQuantity", "Why is availableQuantity 0 for T12288?"));
        Assert.Contains(explanation.Hypotheses, h => h.Text.Contains("ISNULL fallback 0", StringComparison.Ordinal));

        var other = await new RuleBasedInvestigator(_options).InvestigateAsync(Bundle("availableQuantity", "Why 1000?", scope: "T55102"));
        Assert.DoesNotContain(other.Hypotheses, h => h.Text.Contains("ISNULL fallback", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fully_evidenced_fields_are_known_with_high_confidence()
    {
        var explanation = await new RuleBasedInvestigator(_options).ExplainAsync(Bundle("json:pickOrderRows.pickStorageBin.allocatedQuantity"));
        Assert.Equal(ExplainVerdict.Known, explanation.Verdict);
        Assert.Empty(explanation.Hypotheses);
        Assert.Contains(explanation.KnownFacts, f => f.Text.Contains("SUM(APPQD.Quantity - ISNULL(APPQD.PickedQuantity, 0))", StringComparison.Ordinal));
        Assert.True(explanation.Confidence >= 0.9);
    }

    [Fact]
    public async Task AC06_destination_wagon_storage_bin_is_explicitly_unknown()
    {
        var explanation = await new RuleBasedInvestigator(_options).InvestigateAsync(Bundle("json:pickOrderRows.destinationWagon.storageBin.location", "Where does the wagon storage bin come from?"));
        Assert.Equal(ExplainVerdict.NeedMoreEvidence, explanation.Verdict);
        Assert.Contains(explanation.Unknowns, u => u.Contains("GetStorageBin", StringComparison.Ordinal));
        Assert.Contains(explanation.NextSteps, s => s.Contains("GetStorageBin", StringComparison.Ordinal));
        Assert.DoesNotContain(explanation.KnownFacts, f => f.Text.Contains("GetStorageBin", StringComparison.Ordinal) && f.Text.Contains("returns", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_focus_yields_unknown_verdict_and_zero_confidence()
    {
        var explanation = await new RuleBasedInvestigator(_options).ExplainAsync(Bundle("udf:dbo.AF_Pick_GetAvailableQuantity", withTrace: false));
        Assert.Equal(ExplainVerdict.Unknown, explanation.Verdict);
        Assert.Equal(0.0, explanation.Confidence);
        Assert.Empty(explanation.KnownFacts);
    }

    [Fact]
    public async Task Static_explanation_without_trace_asks_for_the_parameter_value()
    {
        var explanation = await new RuleBasedInvestigator(_options).ExplainAsync(Bundle("availableQuantity", withTrace: false));
        Assert.Contains(explanation.KnownFacts, f => f.Text.Contains("runtime value was not observed", StringComparison.Ordinal));
        Assert.Contains(explanation.NextSteps, s => s.Contains("read_system_parameter", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_downgrades_facts_without_valid_evidence()
    {
        var bundle = Bundle("availableQuantity");
        var candidate = new Explanation
        {
            Summary = "draft",
            KnownFacts =
            [
                new KnownFact("supported", [bundle.Items[0].Id]),
                new KnownFact("hallucinated id", ["ev-999"]),
                new KnownFact("no evidence at all", []),
            ],
            Confidence = 0.99,
            Verdict = ExplainVerdict.Known,
            Audit = new AiAudit("test", "m", "p", DateTimeOffset.UtcNow, []),
        };

        var report = new EvidenceBindingValidator().Validate(candidate, bundle);
        Assert.Single(report.Explanation.KnownFacts);
        Assert.Equal(2, report.DowngradedFacts.Count);
        Assert.Equal(2, report.Explanation.Hypotheses.Count);
        Assert.All(report.Explanation.Hypotheses, h => Assert.Equal(HypothesisStatus.Unverified, h.Status));
        Assert.Equal(ExplainVerdict.NeedMoreEvidence, report.Explanation.Verdict); // path has an Unknown UDF
        Assert.True(report.Explanation.Confidence <= 0.75);
        Assert.Equal([bundle.Items[0].Id], report.Explanation.Audit.EvidenceIds);
    }

    [Fact]
    public void Validator_returns_unknown_when_nothing_is_evidenced()
    {
        var bundle = Bundle("availableQuantity");
        var candidate = new Explanation { Summary = "x", KnownFacts = [new KnownFact("made up", ["nope"])], Confidence = 1, Verdict = ExplainVerdict.Known, Audit = new AiAudit("t", "m", "p", DateTimeOffset.UtcNow, []) };
        var report = new EvidenceBindingValidator().Validate(candidate, bundle);
        Assert.Equal(ExplainVerdict.Unknown, report.Explanation.Verdict);
        Assert.Equal(0.0, report.Explanation.Confidence);
        Assert.StartsWith("Unknown / Need More Evidence", report.Explanation.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Llm_investigator_falls_back_to_rules_when_the_model_is_unavailable()
    {
        var investigator = new LlmInvestigator(new UnavailableClient(), new RuleBasedInvestigator(_options), new EvidenceBindingValidator(), _options, NullLogger<LlmInvestigator>.Instance);
        var explanation = await investigator.ExplainAsync(Bundle("availableQuantity"));
        Assert.Equal(RuleBasedInvestigator.ProviderName, explanation.Audit.Provider);
        Assert.Contains("fallback", explanation.Audit.Note, StringComparison.Ordinal);
        Assert.NotEmpty(explanation.KnownFacts);
    }

    [Fact]
    public async Task Llm_answers_are_validated_against_the_bundle()
    {
        var bundle = Bundle("availableQuantity");
        var json = $$"""
        {
          "summary": "model summary",
          "steps": ["looked at hops"],
          "knownFacts": [
            { "text": "grounded", "evidenceIds": ["{{bundle.Items[0].Id}}"] },
            { "text": "invented", "evidenceIds": ["ev-424242"] }
          ],
          "hypotheses": [],
          "unknowns": [],
          "nextSteps": [],
          "confidence": 0.95,
          "verdict": "known"
        }
        """;
        var investigator = new LlmInvestigator(new CannedClient(json), new RuleBasedInvestigator(_options), new EvidenceBindingValidator(), _options, NullLogger<LlmInvestigator>.Instance);
        var explanation = await investigator.ExplainAsync(bundle);

        Assert.Equal(AiOptions.OpenAiCompatibleProvider, explanation.Audit.Provider);
        Assert.Equal("canned-model", explanation.Audit.Model);
        Assert.Single(explanation.KnownFacts);
        Assert.Contains(explanation.Hypotheses, h => h.Text == "invented");
        Assert.Equal(ExplainVerdict.NeedMoreEvidence, explanation.Verdict);
        Assert.Contains("downgraded", explanation.Audit.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Impact_summary_lists_key_paths_with_node_evidence()
    {
        var impact = _fx.Impact.Analyze("param:WMS_Enabled");
        var explanation = await new RuleBasedInvestigator(_options).SummarizeImpactAsync(impact);
        Assert.Contains("WMS_Enabled affects", explanation.Summary, StringComparison.Ordinal);
        Assert.Contains(explanation.KnownFacts, f => f.Text.Contains("Web VP", StringComparison.Ordinal));
        Assert.All(explanation.KnownFacts, f => Assert.NotEmpty(f.EvidenceIds));
    }

    private sealed class UnavailableClient : ILlmClient
    {
        public string Model => "none";

        public bool IsConfigured => false;

        public Task<LlmCompletion> CompleteJsonAsync(IReadOnlyList<LlmMessage> messages, JsonElement jsonSchema, string schemaName, CancellationToken cancellationToken = default)
            => throw new LlmUnavailableException("not configured");
    }

    private sealed class CannedClient : ILlmClient
    {
        private readonly string _json;

        public CannedClient(string json)
        {
            _json = json;
        }

        public string Model => "canned-model";

        public bool IsConfigured => true;

        public Task<LlmCompletion> CompleteJsonAsync(IReadOnlyList<LlmMessage> messages, JsonElement jsonSchema, string schemaName, CancellationToken cancellationToken = default)
        {
            Assert.Contains("EVIDENCE BUNDLE", messages[1].Content, StringComparison.Ordinal);
            Assert.DoesNotContain("chain of thought", messages[1].Content, StringComparison.OrdinalIgnoreCase);
            using var doc = JsonDocument.Parse(_json);
            return Task.FromResult(new LlmCompletion(doc.RootElement.Clone(), Model, 10, 20));
        }
    }
}
