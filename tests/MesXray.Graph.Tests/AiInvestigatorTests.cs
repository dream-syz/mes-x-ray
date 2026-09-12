using System.Text.Json;
using MesXray.AI;
using MesXray.AI.Contracts;
using MesXray.AI.Evidence;
using MesXray.AI.Investigators;
using MesXray.AI.Llm;
using MesXray.AI.Validation;
using MesXray.Domain.Graph;
using MesXray.Graph.Queries;
using MesXray.Graph.Store;
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

    private EvidenceBundle Bundle(string field, string? question = null, string? scope = "T12288", bool withTrace = true, IReadOnlyList<string>? allowed = null, string? language = null)
    {
        var runtime = withTrace ? _fx.DemoTrace : null;
        var trace = _fx.Tracer.Trace(field, runtime, withTrace ? scope : null);
        return new EvidenceBundleBuilder(_fx.Store).Build(trace, runtime, question, allowed, _options.MaxEvidenceItems, language);
    }

    [Fact]
    public async Task Chinese_explanation_keeps_ids_values_and_verdict_identical_to_the_english_one()
    {
        var investigator = new RuleBasedInvestigator(_options);
        var english = await investigator.InvestigateAsync(Bundle("availableQuantity", "Why is Available Quantity 0?"));
        var chinese = await investigator.InvestigateAsync(Bundle("availableQuantity", "Why is Available Quantity 0?", language: "zh-CN"));

        Assert.Equal(english.Verdict, chinese.Verdict);
        Assert.Equal(english.Confidence, chinese.Confidence);
        Assert.Equal(english.KnownFacts.Count, chinese.KnownFacts.Count);
        Assert.Equal(english.Audit.EvidenceIds, chinese.Audit.EvidenceIds);
        Assert.Equal(english.KnownFacts.Select(f => f.EvidenceIds), chinese.KnownFacts.Select(f => f.EvidenceIds));

        Assert.Contains(chinese.KnownFacts, f => f.Text.Contains("观测值", StringComparison.Ordinal) && f.Text.Contains("availableQuantity = 0", StringComparison.Ordinal));
        Assert.Contains(chinese.KnownFacts, f => f.Text.Contains("生效的分支", StringComparison.Ordinal) && f.Text.Contains("WMS_Enabled = 1", StringComparison.Ordinal));
        Assert.Contains(chinese.KnownFacts, f => f.Text.Contains("返回常量 1000", StringComparison.Ordinal) && f.Text.Contains("AF_Pick_GetAvailableQuantity", StringComparison.Ordinal));
        Assert.Contains(chinese.KnownFacts, f => f.Text.Contains("取决于行数据", StringComparison.Ordinal) && f.Text.Contains("DET2_ILG_ProductDeliveryMethod.DeliveryMethod", StringComparison.Ordinal));
        Assert.Contains("需要更多证据：观测值的解释仍是未验证的假设", chinese.Summary, StringComparison.Ordinal);
        Assert.Contains(chinese.Hypotheses, h => h.Text.Contains("ISNULL 的回退值 0", StringComparison.Ordinal) && h.Text.Contains("ISNULL(SUM(QuantityOnHand), 0)", StringComparison.Ordinal));
        Assert.Contains(chinese.NextSteps, s => s.Contains("TEST 环境只读查询", StringComparison.Ordinal) && s.Contains("dbo.DET2_ILG_ProductDeliveryMethod", StringComparison.Ordinal));
        Assert.All(chinese.Steps, s => Assert.DoesNotContain("Resolved focus", s, StringComparison.Ordinal));

        // Unsupported tags fall back to English rather than failing.
        Assert.Equal("en", Bundle("availableQuantity", language: "fr").Language);
        Assert.Equal("zh", Bundle("availableQuantity", language: "zh-Hans").Language);
    }

    [Fact]
    public async Task Impact_summary_is_available_in_chinese()
    {
        var impact = _fx.Impact.Analyze("param:WMS_Enabled");
        var explanation = await new RuleBasedInvestigator(_options).SummarizeImpactAsync(impact, "zh");
        Assert.Contains("WMS_Enabled 影响", explanation.Summary, StringComparison.Ordinal);
        Assert.Contains(explanation.KnownFacts, f => f.Text.Contains("传导到", StringComparison.Ordinal) && f.Text.Contains("Web VP", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AC03_explain_availableQuantity_follows_the_udf_into_INVENTORY2_and_is_known()
    {
        var investigator = new RuleBasedInvestigator(_options);
        var explanation = await investigator.ExplainAsync(Bundle("availableQuantity", "Why is Available Quantity 0?"));

        // The static lineage is complete: WMS branch -> AF_Pick_GetAvailableQuantity -> RETURN -> InventoryData -> INVENTORY2.
        Assert.Equal(ExplainVerdict.Known, explanation.Verdict);
        Assert.Empty(explanation.Hypotheses);
        Assert.Contains(explanation.KnownFacts, f => f.Text.Contains("WMS_Enabled = 1", StringComparison.Ordinal) && f.Text.Contains("Active branch", StringComparison.Ordinal));
        Assert.Contains(explanation.KnownFacts, f => f.Text.StartsWith("AF_Pick_GetAvailableQuantity = IF EXISTS", StringComparison.Ordinal) && f.Text.Contains("RETURN 1000 ELSE RETURN ISNULL(SUM(QuantityOnHand), 0)", StringComparison.Ordinal));
        Assert.Contains(explanation.KnownFacts, f => f.Text.Contains("returns the constant 1000", StringComparison.Ordinal));
        Assert.Contains(explanation.KnownFacts, f => f.Text.Contains("depends on row data", StringComparison.Ordinal) && f.Text.Contains("dbo.DET2_ILG_ProductDeliveryMethod.DeliveryMethod", StringComparison.Ordinal));
        Assert.Contains(explanation.KnownFacts, f => f.Text.Contains("Local variable in QuantityOnHand: @UseQuantityAllocated = 0 -> 1 WHEN EXISTS", StringComparison.Ordinal));
        Assert.Contains(explanation.KnownFacts, f => f.Text.Contains("Base table columns", StringComparison.Ordinal) && f.Text.Contains("dbo.INVENTORY2.QuantityOnHand", StringComparison.Ordinal));
        Assert.Contains(explanation.KnownFacts, f => f.Text.Contains("availableQuantity = 0", StringComparison.Ordinal));

        // The runtime note (values computed inside SQL Server were not captured) is reported but does not make the lineage unknown.
        Assert.Contains(explanation.Unknowns, u => u.Contains("AF_Pick_GetAvailableQuantity was evaluated inside SQL Server", StringComparison.Ordinal));
        Assert.Contains("1 runtime detail(s)", explanation.Summary, StringComparison.Ordinal);
        Assert.Contains(explanation.NextSteps, s => s.Contains("read-only", StringComparison.Ordinal) && s.Contains("dbo.DET2_ILG_ProductDeliveryMethod", StringComparison.Ordinal));
        Assert.DoesNotContain(explanation.NextSteps, s => s.Contains("read_system_parameter('DeliveryMethod')", StringComparison.Ordinal));

        Assert.InRange(explanation.Confidence, 0.85, 0.95);
        Assert.Equal(RuleBasedInvestigator.ProviderName, explanation.Audit.Provider);
        Assert.NotEmpty(explanation.Audit.EvidenceIds);
        Assert.Equal(_options.PromptVersion, explanation.Audit.PromptVersion);
        Assert.DoesNotContain(explanation.Steps, s => s.Contains("I think", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Investigate_explains_the_observed_value_with_a_hypothesis_and_needs_more_evidence()
    {
        var investigator = new RuleBasedInvestigator(_options);
        var zero = await investigator.InvestigateAsync(Bundle("availableQuantity", "Why is Available Quantity 0?"));

        // 0 matches the ISNULL default of the function's own RETURN expression, not of the outer ISNULL(udf(...), 0):
        // a scanned function is not accused of returning NULL from the outside.
        Assert.Equal(ExplainVerdict.NeedMoreEvidence, zero.Verdict);
        var hypothesis = Assert.Single(zero.Hypotheses);
        Assert.StartsWith("AF_Pick_GetAvailableQuantity equals the ISNULL fallback 0 of `ISNULL(SUM(QuantityOnHand), 0)`", hypothesis.Text, StringComparison.Ordinal);
        Assert.Equal(HypothesisStatus.Unverified, hypothesis.Status);
        Assert.Contains("read-only", hypothesis.SuggestedCheck, StringComparison.Ordinal);
        Assert.NotEmpty(hypothesis.EvidenceIds!);
        Assert.Contains("unverified hypothesis", zero.Summary, StringComparison.Ordinal);
        Assert.InRange(zero.Confidence, 0.7, 0.85);

        // 1000 is the constant of the LVP branch.
        var thousand = await investigator.InvestigateAsync(Bundle("availableQuantity", "Why 1000?", scope: "T55102"));
        Assert.Equal(ExplainVerdict.NeedMoreEvidence, thousand.Verdict);
        var constant = Assert.Single(thousand.Hypotheses);
        Assert.Contains("equals the constant 1000 returned when EXISTS (DET2_ILG_ProductDeliveryMethod WHERE DIP.DeliveryMethod = 'LVP' ...)", constant.Text, StringComparison.Ordinal);
        Assert.Contains("DeliveryMethod = 'LVP'", constant.SuggestedCheck, StringComparison.Ordinal);

        // 860 matches neither a constant nor an ISNULL default: the evidenced lineage stands on its own.
        var regular = await investigator.InvestigateAsync(Bundle("availableQuantity", "Why 860?", scope: "T40917"));
        Assert.Equal(ExplainVerdict.Known, regular.Verdict);
        Assert.Empty(regular.Hypotheses);
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
    public async Task Investigate_adds_the_isnull_fallback_hypothesis_only_when_the_observed_value_equals_the_default()
    {
        var explanation = await new RuleBasedInvestigator(_options).InvestigateAsync(Bundle("availableQuantity", "Why is availableQuantity 0 for T12288?"));
        Assert.Contains(explanation.Hypotheses, h => h.Text.Contains("ISNULL fallback 0", StringComparison.Ordinal));
        Assert.DoesNotContain(explanation.Hypotheses, h => h.Text.Contains("ISNULL(dbo.AF_Pick_GetAvailableQuantity", StringComparison.Ordinal));

        var other = await new RuleBasedInvestigator(_options).InvestigateAsync(Bundle("availableQuantity", "Why 1000?", scope: "T55102"));
        Assert.DoesNotContain(other.Hypotheses, h => h.Text.Contains("ISNULL fallback", StringComparison.Ordinal));
    }

    [Fact]
    public void Bundle_carries_constant_branches_and_local_variable_notes_as_citable_evidence()
    {
        var bundle = Bundle("availableQuantity");
        var udf = Assert.Single(bundle.Hops, h => h.NodeId == "udf:dbo.AF_Pick_GetAvailableQuantity");
        var constant = Assert.Single(udf.LiteralBranches);
        Assert.Equal("1000", constant.Literal);
        Assert.StartsWith("EXISTS (DET2_ILG_ProductDeliveryMethod", constant.Condition, StringComparison.Ordinal);
        Assert.Contains(constant.LineageId, bundle.AllowedEvidenceIds);

        var caseExpr = Assert.Single(bundle.Hops, h => h.NodeId == "expr:dbo.AF_Pick_GetAvailableQuantity.InventoryData.QuantityOnHand");
        Assert.Single(caseExpr.Notes, n => n.StartsWith("@UseQuantityAllocated = 0 -> 1 WHEN EXISTS", StringComparison.Ordinal));

        Assert.Empty(bundle.Unknowns);
        Assert.Single(bundle.RuntimeNotes, n => n.Contains("AF_Pick_GetAvailableQuantity", StringComparison.Ordinal));

        var prompt = MesXray.AI.Prompts.PromptLibrary.User(bundle, investigate: true);
        Assert.Contains("- constant 1000 when EXISTS (DET2_ILG_ProductDeliveryMethod", prompt, StringComparison.Ordinal);
        Assert.Contains("- note: @UseQuantityAllocated = 0 -> 1 WHEN EXISTS", prompt, StringComparison.Ordinal);
        Assert.Contains("# Runtime details not captured", prompt, StringComparison.Ordinal);
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
        var store = new InMemoryGraphStore();
        store.Load(new GraphSnapshot
        {
            Nodes = [new Node { Id = "udf:dbo.AF_Unscanned", Type = NodeType.Function, Name = "AF_Unscanned", Layer = Layer.Data, Status = NodeStatus.Unknown }],
        });
        var trace = new FieldTraceService(store, new GraphQueryService(store)).Trace("udf:dbo.AF_Unscanned");
        var bundle = new EvidenceBundleBuilder(store).Build(trace, null, null, null, _options.MaxEvidenceItems);

        var explanation = await new RuleBasedInvestigator(_options).ExplainAsync(bundle);
        Assert.Equal(ExplainVerdict.Unknown, explanation.Verdict);
        Assert.Equal(0.0, explanation.Confidence);
        Assert.Empty(explanation.KnownFacts);
        Assert.Contains(explanation.NextSteps, s => s.Contains("AF_Unscanned", StringComparison.Ordinal));
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
        Assert.Equal(ExplainVerdict.NeedMoreEvidence, report.Explanation.Verdict); // downgraded facts survive as hypotheses
        Assert.True(report.Explanation.Confidence <= 0.75);
        Assert.Equal([bundle.Items[0].Id], report.Explanation.Audit.EvidenceIds);
    }

    [Fact]
    public void Validator_keeps_known_when_only_runtime_notes_remain_but_not_when_hypotheses_do()
    {
        var bundle = Bundle("availableQuantity");
        Assert.NotEmpty(bundle.RuntimeNotes);
        var audit = new AiAudit("test", "m", "p", DateTimeOffset.UtcNow, []);
        var grounded = new Explanation { Summary = "s", KnownFacts = [new KnownFact("supported", [bundle.Items[0].Id])], Unknowns = bundle.RuntimeNotes, Confidence = 0.9, Verdict = ExplainVerdict.Known, Audit = audit };

        var known = new EvidenceBindingValidator().Validate(grounded, bundle).Explanation;
        Assert.Equal(ExplainVerdict.Known, known.Verdict);
        Assert.Equal(bundle.RuntimeNotes, known.Unknowns);

        var withHypothesis = new EvidenceBindingValidator().Validate(grounded with { Hypotheses = [new Hypothesis("maybe", HypothesisStatus.Unverified, "check")] }, bundle).Explanation;
        Assert.Equal(ExplainVerdict.NeedMoreEvidence, withHypothesis.Verdict);

        var withStaticGap = new EvidenceBindingValidator().Validate(grounded with { Unknowns = ["dbo.Something is not scanned"] }, bundle).Explanation;
        Assert.Equal(ExplainVerdict.NeedMoreEvidence, withStaticGap.Verdict);
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
