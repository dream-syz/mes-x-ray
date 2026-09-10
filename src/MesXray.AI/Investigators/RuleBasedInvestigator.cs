using System.Globalization;
using System.Text.RegularExpressions;
using MesXray.AI.Contracts;
using MesXray.AI.Evidence;
using MesXray.Domain.Graph;
using MesXray.Graph.Queries;

namespace MesXray.AI.Investigators;

/// <summary>
/// Deterministic, offline investigator. Every statement is derived from the evidence bundle by explicit rules and
/// cites the ids it used, so the demo works without any LLM and the output is reproducible. It is also the fallback
/// and the validator's reference for the LLM-backed investigator.
/// </summary>
public sealed partial class RuleBasedInvestigator : IAiInvestigator
{
    public const string ProviderName = "rules";
    public const string ModelName = "rule-engine/0.1.0";

    private readonly AiOptions _options;
    private readonly TimeProvider _clock;

    public RuleBasedInvestigator(AiOptions options, TimeProvider? clock = null)
    {
        _options = options;
        _clock = clock ?? TimeProvider.System;
    }

    public string Provider => ProviderName;

    public Task<Explanation> ExplainAsync(EvidenceBundle bundle, CancellationToken cancellationToken = default)
        => Task.FromResult(Explain(bundle, investigate: false));

    public Task<Explanation> InvestigateAsync(EvidenceBundle bundle, CancellationToken cancellationToken = default)
        => Task.FromResult(Explain(bundle, investigate: true));

    public Task<Explanation> SummarizeImpactAsync(ImpactResult impact, CancellationToken cancellationToken = default)
    {
        var cited = new SortedSet<string>(StringComparer.Ordinal) { impact.Origin.Id };
        var facts = new List<KnownFact>();

        foreach (var path in impact.KeyPaths.Take(12))
        {
            var names = path.Select(id => impact.Affected.FirstOrDefault(a => a.Node.Id == id)?.Node.Name ?? (id == impact.Origin.Id ? impact.Origin.Name : NodeIds.LeafName(id))).ToList();
            var evidence = path.ToList();
            foreach (var id in evidence)
            {
                cited.Add(id);
            }

            facts.Add(new KnownFact($"A change of {impact.Origin.Name} reaches {names[^1]} via {string.Join(" -> ", names)}.", evidence));
        }

        var unknowns = impact.Affected
            .Where(a => a.Node.Status != NodeStatus.Known)
            .Select(a => $"{a.Node.Name} ({a.Node.Id}) is {a.Node.Status}: downstream effects beyond it cannot be confirmed.")
            .ToList();

        var summaryParts = impact.Summary.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {kv.Key}");
        var summary = impact.Affected.Count == 0
            ? $"No downstream node depends on {impact.Origin.Name}."
            : $"{impact.Origin.Name} affects {impact.Affected.Count} downstream node(s): {string.Join(", ", summaryParts)}.";

        return Task.FromResult(new Explanation
        {
            Summary = summary,
            Steps =
            [
                $"Started from {impact.Origin.Id}.",
                $"Followed downstream (FlowsTo) and dependent (DependsOn) edges plus structural containment; {impact.Affected.Count} node(s) reached{(impact.Truncated ? " (truncated)" : string.Empty)}.",
                $"Selected {impact.KeyPaths.Count} key path(s) ending at pages, APIs or JSON fields.",
            ],
            KnownFacts = facts,
            Unknowns = unknowns,
            Confidence = unknowns.Count == 0 ? 0.9 : 0.7,
            Verdict = unknowns.Count == 0 ? ExplainVerdict.Known : ExplainVerdict.NeedMoreEvidence,
            Audit = Audit(cited, note: "impact-summary"),
        });
    }

    private Explanation Explain(EvidenceBundle bundle, bool investigate)
    {
        var cited = new SortedSet<string>(StringComparer.Ordinal);
        var steps = new List<string>();
        var facts = new List<KnownFact>();
        var hypotheses = new List<Hypothesis>();
        var nextSteps = new List<string>();

        string[] Cite(params IEnumerable<string?>[] groups)
        {
            var ids = groups.SelectMany(g => g).Where(id => id is not null && bundle.AllowedEvidenceIds.Contains(id)).Select(id => id!).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var id in ids)
            {
                cited.Add(id);
            }

            return ids;
        }

        var focus = bundle.Focus;
        var root = bundle.Hops.Count > 0 ? bundle.Hops[0] : null;
        steps.Add($"Resolved focus node {focus.Id} ({focus.Type}, {focus.Status}).");

        if (focus.Status == NodeStatus.Unknown || root is null)
        {
            return new Explanation
            {
                Summary = $"Unknown: {focus.Name} has no scanned definition, so nothing can be explained from evidence.",
                Steps = steps,
                Unknowns = bundle.Unknowns,
                NextSteps = [$"Scan or import the definition of {focus.Name} and rebuild the graph."],
                Confidence = 0.0,
                Verdict = ExplainVerdict.Unknown,
                Audit = Audit(cited, note: "focus-unknown"),
            };
        }

        // 1. Runtime value of the focus.
        if (root.RuntimeEvidenceIds.Count > 0)
        {
            var ids = Cite(root.RuntimeEvidenceIds, [root.NodeId]);
            facts.Add(new KnownFact($"Observed value: {string.Join("; ", root.RuntimeValueTexts)} (trace {bundle.TraceId}, {bundle.Environment}).", ids));
            steps.Add($"Overlaid live trace {bundle.TraceId}{(bundle.Scope is null ? string.Empty : $" scoped to {bundle.Scope}")}.");
        }
        else if (bundle.TraceId is not null)
        {
            steps.Add($"Live trace {bundle.TraceId} carries no value for {focus.Name}.");
        }
        else
        {
            steps.Add("No live trace supplied: static explanation only.");
        }

        // 2. Execution path.
        if (bundle.ExecutionPath.Count > 0)
        {
            var ids = Cite(bundle.ExecutionPath.Select(n => n.Id));
            facts.Add(new KnownFact($"Execution path: {string.Join(" -> ", bundle.ExecutionPath.Select(n => n.Name))}.", ids));
            steps.Add($"Reconstructed the static execution path with {bundle.ExecutionPath.Count} node(s).");
        }

        // 3. Lineage chain.
        var lineageHops = bundle.Hops.Where(h => h.Depth > 0 && !h.IsRepeat).ToList();
        steps.Add($"Walked {lineageHops.Count} upstream hop(s) through serialization, Dapper mapping, SQL aliases and expressions.");

        var childrenOf = BuildChildren(bundle.Hops);
        var parentOf = BuildParents(bundle.Hops);

        foreach (var hop in lineageHops.Where(h => h.ViaRelation is RelationType.SerializesAs or RelationType.MapsTo or RelationType.AliasOf or RelationType.Returns or RelationType.EnrichedBy).Take(8))
        {
            var child = parentOf.GetValueOrDefault(hop);
            if (child is null)
            {
                continue;
            }

            var ids = Cite([hop.ViaEdgeId, hop.NodeId, child.NodeId], hop.RuntimeEvidenceIds);
            var value = hop.RuntimeValueTexts.Count > 0 ? $" (observed {string.Join("; ", hop.RuntimeValueTexts)})" : string.Empty;
            facts.Add(new KnownFact($"{child.Name} {Verb(hop.ViaRelation!.Value)} {Qualify(hop)}{value}.", ids));
        }

        // 4. Expressions and branches.
        foreach (var expr in bundle.HopsOfType(NodeType.Expression))
        {
            var produced = parentOf.GetValueOrDefault(expr);
            var producedName = produced?.Name ?? "value";
            var exprIds = Cite([expr.NodeId, expr.ViaEdgeId, produced?.NodeId]);
            facts.Add(new KnownFact($"{producedName} = {expr.Expression ?? expr.Name}.", exprIds));

            var children = childrenOf.GetValueOrDefault(expr) ?? [];
            var control = children.FirstOrDefault(c => c.ViaRelation == RelationType.ControlledBy);
            var branches = children.Where(c => c.Condition is not null).GroupBy(c => c.Condition!, StringComparer.Ordinal).ToList();

            foreach (var branch in branches)
            {
                var ids = Cite(branch.Select(b => b.ViaEdgeId), branch.Select(b => b.NodeId));
                facts.Add(new KnownFact($"Branch `{branch.Key}` of {producedName} uses {string.Join(", ", branch.Select(Qualify))}.", ids));
            }

            if (control is not null)
            {
                var controlIds = Cite([control.ViaEdgeId, control.NodeId], control.RuntimeEvidenceIds);
                var controlValue = control.RuntimeValueTexts.Count > 0 ? control.RuntimeValueTexts[0] : null;
                if (controlValue is null)
                {
                    facts.Add(new KnownFact($"Which branch of {producedName} applies is decided by system parameter {control.Name}; its runtime value was not observed.", controlIds));
                    nextSteps.Add($"Run read_system_parameter('{control.Name}') to determine the active branch.");
                    continue;
                }

                facts.Add(new KnownFact($"{producedName} is controlled by {control.Name} = {ValueOf(controlValue)} at runtime.", controlIds));
                var active = ActiveBranch(ValueOf(controlValue), branches.Select(b => b.Key).ToList());
                if (active is null)
                {
                    hypotheses.Add(new Hypothesis($"No branch condition of {producedName} matches {control.Name} = {ValueOf(controlValue)}; the CASE may yield NULL.", HypothesisStatus.Unverified, "Compare the CASE conditions with the observed parameter value."));
                    continue;
                }

                var activeSources = branches.First(b => b.Key == active).ToList();
                var activeIds = Cite(activeSources.Select(s => s.ViaEdgeId), activeSources.Select(s => s.NodeId), control.RuntimeEvidenceIds);
                facts.Add(new KnownFact($"Active branch for this trace: `{active}` -> {string.Join(", ", activeSources.Select(Qualify))}.", activeIds));
                steps.Add($"Evaluated CASE conditions of {producedName} against {control.Name} = {ValueOf(controlValue)}.");

                var unknownSources = activeSources.Where(s => s.Status != NodeStatus.Known).ToList();
                foreach (var unknown in unknownSources)
                {
                    hypotheses.Add(new Hypothesis(
                        $"{producedName} is determined inside {unknown.Name}, whose definition is not available; the observed value cannot be verified further.",
                        HypothesisStatus.Unverified,
                        $"Import the definition of {unknown.Name} (or capture its return value in TEST) and rescan.",
                        Cite([unknown.NodeId])));
                    nextSteps.Add($"Scan the definition of {unknown.Name}.");
                }

                if (investigate)
                {
                    AddIsNullHypothesis(root, activeSources, producedName, hypotheses, Cite);
                }
            }
        }

        // 5. Base columns and parameters.
        var columns = bundle.HopsOfType(NodeType.Column).Select(h => h.Name).Distinct(StringComparer.Ordinal).ToList();
        if (columns.Count > 0)
        {
            var columnHops = bundle.HopsOfType(NodeType.Column).ToList();
            var ids = Cite(columnHops.Select(h => h.NodeId));
            var tables = columnHops.Select(h => h.ContainerNodeId).Where(t => t is not null).Distinct(StringComparer.Ordinal).Select(t => NodeIds.OwnerKeyOf(t!)).ToList();
            facts.Add(new KnownFact($"Base table columns involved: {string.Join(", ", columnHops.Select(Qualify).Distinct(StringComparer.Ordinal))}{(tables.Count > 0 ? $" (tables {string.Join(", ", tables)})" : string.Empty)}.", ids));
        }

        foreach (var parameter in bundle.HopsOfType(NodeType.SystemParameter).DistinctBy(h => h.NodeId))
        {
            if (parameter.ViaRelation == RelationType.ControlledBy)
            {
                continue; // already covered by the branch facts
            }

            var ids = Cite([parameter.NodeId, parameter.ViaEdgeId], parameter.RuntimeEvidenceIds);
            var value = parameter.RuntimeValueTexts.Count > 0 ? $" = {ValueOf(parameter.RuntimeValueTexts[0])}" : string.Empty;
            facts.Add(new KnownFact($"System parameter {parameter.Name}{value} feeds {parentOf.GetValueOrDefault(parameter)?.Name ?? focus.Name}.", ids));
        }

        // 6. Unknowns & pending.
        var unknownHops = bundle.Hops.Where(h => h.Status != NodeStatus.Known && !h.IsRepeat).DistinctBy(h => h.NodeId).ToList();
        foreach (var pending in unknownHops.Where(h => h.Status == NodeStatus.Pending))
        {
            nextSteps.Add($"Finish scanning {pending.Name} (marked Pending).");
        }

        var unknowns = bundle.Unknowns.ToList();
        steps.Add($"Checked definitions: {unknownHops.Count} Unknown/Pending node(s) on the path.");

        if (investigate && root.RuntimeEvidenceIds.Count == 0 && bundle.TraceId is not null)
        {
            unknowns.Add($"No runtime value for {focus.Name} in trace {bundle.TraceId}{(bundle.Scope is null ? string.Empty : $" / {bundle.Scope}")}.");
            nextSteps.Add("Run trace_pick_order for the order to capture the value.");
        }

        var confidence = Confidence(unknownHops.Count, facts.Count, root.RuntimeEvidenceIds.Count > 0);
        var verdict = unknowns.Count == 0 && unknownHops.Count == 0 ? ExplainVerdict.Known : ExplainVerdict.NeedMoreEvidence;

        var summary = BuildSummary(bundle, root, verdict, unknownHops, facts);

        return new Explanation
        {
            Summary = summary,
            Steps = steps,
            KnownFacts = facts,
            Hypotheses = hypotheses,
            Unknowns = unknowns.Distinct(StringComparer.Ordinal).ToList(),
            NextSteps = nextSteps.Distinct(StringComparer.Ordinal).ToList(),
            Confidence = confidence,
            Verdict = verdict,
            Audit = Audit(cited, note: investigate ? "investigate" : "explain"),
        };
    }

    private static string BuildSummary(EvidenceBundle bundle, HopSummary root, ExplainVerdict verdict, List<HopSummary> unknownHops, List<KnownFact> facts)
    {
        var sp = bundle.HopsOfType(NodeType.StoredProcedure).FirstOrDefault()
                 ?? bundle.ExecutionPath.Where(n => n.Type == NodeType.StoredProcedure).Select(n => new HopSummary(0, n.Id, n.Name, n.Type, n.Status, null, null, null, null, null, null, [], [], false)).FirstOrDefault();
        var spName = sp?.Name ?? bundle.Hops.Where(h => h.ContainerNodeId is not null && h.ContainerNodeId.StartsWith("sp:", StringComparison.Ordinal)).Select(h => NodeIds.OwnerKeyOf(h.ContainerNodeId!)).FirstOrDefault();
        var value = root.RuntimeValueTexts.Count > 0 ? $" Observed {string.Join("; ", root.RuntimeValueTexts)}." : string.Empty;
        var origin = spName is null ? string.Empty : $" It originates in {spName}.";
        var caveat = verdict switch
        {
            ExplainVerdict.Known => " Every hop is backed by scanned code or runtime evidence.",
            _ => $" Need more evidence: {string.Join(", ", unknownHops.Select(h => h.Name))}.",
        };
        return $"{bundle.Focus.Name} is explained through {facts.Count} evidence-backed fact(s).{origin}{value}{caveat}";
    }

    private static void AddIsNullHypothesis(HopSummary root, List<HopSummary> activeSources, string producedName, List<Hypothesis> hypotheses, Func<IEnumerable<string?>[], string[]> cite)
    {
        var observed = root.RuntimeValueTexts.Select(ValueOf).FirstOrDefault();
        if (observed is null)
        {
            return;
        }

        foreach (var expression in activeSources.Select(s => s.Expression).Where(e => e is not null).Distinct(StringComparer.Ordinal))
        {
            var match = IsNullDefault().Match(expression!);
            if (match.Success && string.Equals(match.Groups["default"].Value.Trim(), observed, StringComparison.OrdinalIgnoreCase))
            {
                hypotheses.Add(new Hypothesis(
                    $"{producedName} equals the ISNULL fallback {observed} of `{expression}`; the inner value may have been NULL (e.g. the function returned no row).",
                    HypothesisStatus.Unverified,
                    "Execute the inner function for this material in TEST and check whether it returns NULL.",
                    cite([root.RuntimeEvidenceIds, activeSources.Select(s => s.NodeId)])));
            }
        }
    }

    private static string? ActiveBranch(string value, IReadOnlyList<string> conditions)
    {
        string? fallback = null;
        foreach (var condition in conditions)
        {
            if (string.Equals(condition, "ELSE", StringComparison.OrdinalIgnoreCase))
            {
                fallback = condition;
                continue;
            }

            var match = SimpleEquality().Match(condition);
            if (!match.Success)
            {
                continue;
            }

            // The condition variable (e.g. @WMS_Enabled) may be named differently from the parameter; the edge that
            // led here is the ControlledBy edge, so the binding is already established by the scanner.
            if (ValuesEqual(match.Groups["value"].Value, value))
            {
                return condition;
            }
        }

        return fallback;
    }

    private static bool ValuesEqual(string literal, string observed)
    {
        static string Normalize(string s)
        {
            var t = s.Trim().Trim('\'', '"');
            return t.ToUpperInvariant() switch
            {
                "TRUE" => "1",
                "FALSE" => "0",
                _ => t,
            };
        }

        return string.Equals(Normalize(literal), Normalize(observed), StringComparison.OrdinalIgnoreCase);
    }

    private static string ValueOf(string runtimeText)
    {
        var idx = runtimeText.IndexOf(" = ", StringComparison.Ordinal);
        var raw = idx < 0 ? runtimeText : runtimeText[(idx + 3)..];
        return raw.Trim().Trim('"');
    }

    private static string Verb(RelationType relation) => relation switch
    {
        RelationType.SerializesAs => "is the JSON serialization of",
        RelationType.MapsTo => "is mapped by name (Dapper) from",
        RelationType.AliasOf => "is an alias of",
        RelationType.Returns => "is returned by",
        RelationType.EnrichedBy => "is enriched by",
        RelationType.Produces => "is produced by",
        RelationType.DerivedFrom => "derives from",
        RelationType.ComputedBy => "is computed by",
        RelationType.ControlledBy => "is controlled by",
        _ => relation.ToString().ToLowerInvariant(),
    };

    private static string Qualify(HopSummary hop)
    {
        var owner = hop.ContainerNodeId is null ? null : NodeIds.OwnerKeyOf(hop.ContainerNodeId);
        var status = hop.Status == NodeStatus.Known ? string.Empty : $" [{hop.Status}]";
        return hop.Type switch
        {
            NodeType.Column when owner is not null => $"{owner}.{hop.Name}{status}",
            NodeType.ResultColumn or NodeType.IntermediateColumn when owner is not null => $"{owner}.{hop.Name}{status}",
            NodeType.Field when owner is not null => $"{NodeIds.LeafName(hop.ContainerNodeId!)}.{hop.Name}{status}",
            NodeType.Function => $"function {hop.Name}{status}",
            NodeType.SystemParameter => $"parameter {hop.Name}{status}",
            _ => $"{hop.Name}{status}",
        };
    }

    private static double Confidence(int unknownCount, int factCount, bool hasRuntime)
    {
        if (factCount == 0)
        {
            return 0.0;
        }

        var confidence = 0.95 - 0.2 * unknownCount - (hasRuntime ? 0.0 : 0.05);
        return Math.Round(Math.Clamp(confidence, 0.3, 0.95), 2);
    }

    private static Dictionary<HopSummary, List<HopSummary>> BuildChildren(IReadOnlyList<HopSummary> hops)
    {
        var result = new Dictionary<HopSummary, List<HopSummary>>();
        var stack = new List<HopSummary>();
        foreach (var hop in hops)
        {
            while (stack.Count > 0 && stack[^1].Depth >= hop.Depth)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            if (stack.Count > 0)
            {
                if (!result.TryGetValue(stack[^1], out var list))
                {
                    list = [];
                    result[stack[^1]] = list;
                }

                list.Add(hop);
            }

            stack.Add(hop);
        }

        return result;
    }

    private static Dictionary<HopSummary, HopSummary> BuildParents(IReadOnlyList<HopSummary> hops)
    {
        var result = new Dictionary<HopSummary, HopSummary>();
        foreach (var (parent, children) in BuildChildren(hops))
        {
            foreach (var child in children)
            {
                result[child] = parent;
            }
        }

        return result;
    }

    private AiAudit Audit(IEnumerable<string> cited, string? note)
        => new(ProviderName, ModelName, _options.PromptVersion, _clock.GetUtcNow(), cited.ToList(), note);

    [GeneratedRegex(@"^\s*(?<var>@\w+)\s*=\s*(?<value>[^\s]+)\s*$")]
    private static partial Regex SimpleEquality();

    [GeneratedRegex(@"^ISNULL\((?<inner>.*),\s*(?<default>[^,()]+)\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex IsNullDefault();
}
