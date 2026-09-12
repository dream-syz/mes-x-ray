using MesXray.AI.Contracts;
using MesXray.AI.Evidence;

namespace MesXray.AI.Validation;

/// <summary>
/// Enforces "no evidence, no conclusion": a known fact must cite at least one allowed evidence id. Facts that cite
/// nothing, or cite ids outside the bundle, are downgraded to unverified hypotheses. The verdict and confidence are
/// recomputed from what survived.
/// </summary>
public sealed class EvidenceBindingValidator
{
    public ValidationReport Validate(Explanation candidate, EvidenceBundle bundle)
    {
        var allowed = bundle.AllowedEvidenceIds;
        var facts = new List<KnownFact>();
        var hypotheses = new List<Hypothesis>(candidate.Hypotheses.Select(h => h with { EvidenceIds = h.EvidenceIds?.Where(allowed.Contains).ToList() }));
        var downgraded = new List<string>();
        var cited = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var fact in candidate.KnownFacts)
        {
            var valid = fact.EvidenceIds.Where(allowed.Contains).Distinct(StringComparer.Ordinal).ToList();
            var invalid = fact.EvidenceIds.Except(valid, StringComparer.Ordinal).ToList();

            if (valid.Count == 0)
            {
                downgraded.Add(fact.Text);
                hypotheses.Add(new Hypothesis(fact.Text, HypothesisStatus.Unverified, "Claimed as fact without valid evidence; needs an evidence id from the bundle."));
                continue;
            }

            foreach (var id in valid)
            {
                cited.Add(id);
            }

            facts.Add(invalid.Count == 0 ? fact : fact with { EvidenceIds = valid });
        }

        // Static gaps (unscanned definitions, unknowns claimed by the model) and surviving hypotheses keep the verdict
        // at Need More Evidence; runtime details that were merely not captured are reported but do not.
        var unknowns = candidate.Unknowns.Concat(bundle.Unknowns).Concat(bundle.RuntimeNotes).Distinct(StringComparer.Ordinal).ToList();
        var staticGap = unknowns.Except(bundle.RuntimeNotes, StringComparer.Ordinal).Any()
                        || bundle.Hops.Any(h => h.Status != Domain.Graph.NodeStatus.Known);
        var verdict = facts.Count == 0
            ? ExplainVerdict.Unknown
            : staticGap || hypotheses.Count > 0
                ? ExplainVerdict.NeedMoreEvidence
                : ExplainVerdict.Known;

        var confidence = facts.Count == 0
            ? 0.0
            : Math.Round(Math.Min(candidate.Confidence, verdict == ExplainVerdict.Known ? 0.95 : 0.75), 2);

        var note = downgraded.Count == 0 ? candidate.Audit.Note : $"{candidate.Audit.Note}; {downgraded.Count} unsupported fact(s) downgraded to hypotheses";

        var validated = candidate with
        {
            KnownFacts = facts,
            Hypotheses = hypotheses,
            Unknowns = unknowns,
            Verdict = verdict,
            Confidence = confidence,
            Summary = facts.Count == 0 ? "Unknown / Need More Evidence: no statement could be tied to evidence. " + candidate.Summary : candidate.Summary,
            Audit = candidate.Audit with { EvidenceIds = cited.ToList(), Note = note },
        };

        return new ValidationReport(validated, downgraded);
    }
}

/// <summary>The validated explanation plus the facts that had to be downgraded.</summary>
public sealed record ValidationReport(Explanation Explanation, IReadOnlyList<string> DowngradedFacts);
