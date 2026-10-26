using System.Text.RegularExpressions;
using MesXray.AI.Contracts;
using MesXray.AI.Evidence;
using MesXray.Domain.Graph;
using MesXray.Graph.Queries;

namespace MesXray.AI.Investigators;

/// <summary>
/// Deterministic, offline investigator. Every statement is derived from the evidence bundle by explicit rules and
/// cites the ids it used, so the demo works without any LLM and the output is reproducible. It is also the fallback
/// and the validator's reference for the LLM-backed investigator. Sentences come from <see cref="InvestigatorPhrases"/>
/// in the bundle's language; identifiers, expressions, values and evidence ids are never translated.
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

    public Task<Explanation> SummarizeImpactAsync(ImpactResult impact, string? language = null, CancellationToken cancellationToken = default)
    {
        var p = InvestigatorPhrases.For(language);
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

            facts.Add(new KnownFact(p.ImpactReaches(impact.Origin.Name, names[^1], names), evidence));
        }

        var unknowns = impact.Affected
            .Where(a => a.Node.Status != NodeStatus.Known)
            .Select(a => p.ImpactUnknownNode(a.Node.Name, a.Node.Id, a.Node.Status))
            .ToList();

        var summary = impact.Affected.Count == 0
            ? p.ImpactNoDownstream(impact.Origin.Name)
            : p.ImpactSummary(impact.Origin.Name, impact.Affected.Count, impact.Summary.OrderByDescending(kv => kv.Value));

        return Task.FromResult(new Explanation
        {
            Summary = summary,
            Steps =
            [
                p.ImpactStepStart(impact.Origin.Id),
                p.ImpactStepFollowed(impact.Affected.Count, impact.Truncated),
                p.ImpactStepKeyPaths(impact.KeyPaths.Count),
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
        var p = InvestigatorPhrases.For(bundle.Language);
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
        steps.Add(p.StepResolvedFocus(focus.Id, focus.Type, focus.Status));

        if (focus.Status == NodeStatus.Unknown || root is null)
        {
            return new Explanation
            {
                Summary = p.SummaryUnknownFocus(focus.Name),
                Steps = steps,
                Unknowns = bundle.Unknowns,
                NextSteps = [p.NextScanFocus(focus.Name)],
                Confidence = 0.0,
                Verdict = ExplainVerdict.Unknown,
                Audit = Audit(cited, note: "focus-unknown"),
            };
        }

        // 1. Runtime value of the focus.
        if (root.RuntimeEvidenceIds.Count > 0)
        {
            var ids = Cite(root.RuntimeEvidenceIds, [root.NodeId]);
            facts.Add(new KnownFact(p.FactObservedValue(string.Join("; ", root.RuntimeValueTexts), bundle.TraceId, bundle.Environment), ids));
            steps.Add(p.StepOverlaidTrace(bundle.TraceId!, bundle.Scope));
            if (TryRowContext(bundle, out var rowFact, out var rowIds))
            {
                facts.Add(new KnownFact(p.FactRowContext(rowFact.PickQuantity, rowFact.OnHand, rowFact.Allocated, rowFact.StorageBin), Cite(rowIds)));
            }
        }
        else if (bundle.TraceId is not null)
        {
            steps.Add(p.StepTraceNoValue(bundle.TraceId, focus.Name));
        }
        else
        {
            steps.Add(p.StepNoLiveTrace());
        }

        // 2. Execution path.
        if (bundle.ExecutionPath.Count > 0)
        {
            var ids = Cite(bundle.ExecutionPath.Select(n => n.Id));
            facts.Add(new KnownFact(p.FactExecutionPath(bundle.ExecutionPath.Select(n => n.Name)), ids));
            steps.Add(p.StepExecutionPath(bundle.ExecutionPath.Count));
        }

        // 3. Lineage chain.
        var lineageHops = bundle.Hops.Where(h => h.Depth > 0 && !h.IsRepeat).ToList();
        steps.Add(p.StepWalkedHops(lineageHops.Count));

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
            facts.Add(new KnownFact(p.FactLink(child.Name, hop.ViaRelation!.Value, p.Qualify(hop), hop.RuntimeValueTexts), ids));
        }

        // 4. Expressions and branches. Hops are depth-first, so the outermost expression is examined first; once a
        //    hypothesis explains the observed value, deeper ISNULL/constant coincidences are not repeated.
        var valueExplained = false;
        var valueConfirmed = false;
        string? causeFunction = null;
        string? causeValue = null;
        var rowTablesToCheck = new SortedSet<string>(StringComparer.Ordinal);
        var functionsReported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expr in bundle.HopsOfType(NodeType.Expression))
        {
            var produced = parentOf.GetValueOrDefault(expr);
            var producedName = produced?.Name ?? p.DefaultProducedName;
            var exprIds = Cite([expr.NodeId, expr.ViaEdgeId, produced?.NodeId]);
            var expression = expr.Expression ?? expr.Name;
            facts.Add(new KnownFact(p.FactExpression(producedName, expression), exprIds));

            if (produced is { Type: NodeType.Function } && ReturnValueTexts(produced) is { Count: > 0 } returns && functionsReported.Add(produced.NodeId))
            {
                var functionIds = Cite(produced.RuntimeEvidenceIds, [produced.NodeId]);
                facts.Add(new KnownFact(p.FactFunctionReturned(produced.Name, returns), functionIds));
                causeFunction = produced.Name;
                causeValue = ValueOf(returns[0]);
            }

            foreach (var note in expr.Notes.Where(n => expression.Contains(VariableOf(n), StringComparison.OrdinalIgnoreCase)))
            {
                facts.Add(new KnownFact(p.FactLocalVariable(producedName, note), exprIds));
            }

            var children = childrenOf.GetValueOrDefault(expr) ?? [];
            var controls = children.Where(c => c.ViaRelation == RelationType.ControlledBy).ToList();
            var branches = children.Where(c => c.Condition is not null).GroupBy(c => c.Condition!, StringComparer.Ordinal).ToList();
            var constants = produced?.LiteralBranches ?? [];
            var observedForProduced = ObservedReturnValue(produced) ?? ObservedReturnValue(root);

            foreach (var branch in branches)
            {
                var ids = Cite(branch.Select(b => b.ViaEdgeId), branch.Select(b => b.NodeId));
                facts.Add(new KnownFact(p.FactBranchUses(branch.Key, producedName, branch.Select(p.Qualify)), ids));
            }

            foreach (var constant in constants)
            {
                var ids = Cite([constant.LineageId, produced?.NodeId, expr.NodeId]);
                facts.Add(new KnownFact(p.FactBranchConstant(constant.Condition ?? "ELSE", producedName, constant.Literal), ids));
            }

            if (controls.Count == 0)
            {
                continue;
            }

            var control = controls.FirstOrDefault(c => c.RuntimeValueTexts.Count > 0);
            if (control is null)
            {
                // The branch condition is not observed in this trace: say what decides it and how to check it.
                var controlIds = Cite(controls.Select(c => c.ViaEdgeId), controls.Select(c => c.NodeId));
                foreach (var parameter in controls.Where(c => c.Type == NodeType.SystemParameter).DistinctBy(c => c.NodeId))
                {
                    facts.Add(new KnownFact(p.FactBranchUndecided(producedName, parameter.Name), controlIds));
                    nextSteps.Add(p.NextReadParameter(parameter.Name));
                }

                var columns = controls.Where(c => c.Type == NodeType.Column).DistinctBy(c => c.NodeId).ToList();
                if (columns.Count > 0)
                {
                    facts.Add(new KnownFact(p.FactBranchDependsOnRows(producedName, columns.Select(p.Qualify)), controlIds));
                    if (!valueConfirmed)
                    {
                        rowTablesToCheck.UnionWith(TablesOf(columns));
                    }
                }

                if (investigate && !valueExplained)
                {
                    var confirmed = produced?.Type == NodeType.Function && ReturnValueTexts(produced).Count > 0;
                    valueExplained |= AddConstantHypothesis(p, observedForProduced, constants, producedName, hypotheses, Cite, confirmed);
                    valueExplained |= AddIsNullHypothesis(p, observedForProduced, root, children.Where(c => c.Condition is not null).ToList(), producedName, hypotheses, Cite, childrenOf, confirmed);
                    valueConfirmed |= confirmed && valueExplained;
                }

                continue;
            }

            var controlIdsDecided = Cite([control.ViaEdgeId, control.NodeId], control.RuntimeEvidenceIds);
            var controlValue = ValueOf(control.RuntimeValueTexts[0]);
            facts.Add(new KnownFact(p.FactControlledBy(producedName, control.Name, controlValue), controlIdsDecided));
            var active = ActiveBranch(controlValue, branches.Select(b => b.Key).ToList());
            if (active is null)
            {
                hypotheses.Add(new Hypothesis(p.HypothesisNoBranchMatches(producedName, control.Name, controlValue), HypothesisStatus.Unverified, p.CheckCompareCase));
                continue;
            }

            var activeSources = branches.First(b => b.Key == active).ToList();
            var activeIds = Cite(activeSources.Select(s => s.ViaEdgeId), activeSources.Select(s => s.NodeId), control.RuntimeEvidenceIds);
            facts.Add(new KnownFact(p.FactActiveBranch(active, activeSources.Select(p.Qualify)), activeIds));
            steps.Add(p.StepEvaluatedCase(producedName, control.Name, controlValue));

            var unknownSources = activeSources.Where(s => s.Status != NodeStatus.Known).ToList();
            foreach (var unknown in unknownSources)
            {
                hypotheses.Add(new Hypothesis(
                    p.HypothesisUnknownSource(producedName, unknown.Name),
                    HypothesisStatus.Unverified,
                    p.CheckImportDefinition(unknown.Name),
                    Cite([unknown.NodeId])));
                nextSteps.Add(p.NextScanDefinition(unknown.Name));
            }

            if (investigate && !valueExplained)
            {
                var functionSource = activeSources.FirstOrDefault(s => s.Type == NodeType.Function && ReturnValueTexts(s).Count > 0);
                var confirmed = functionSource is not null;
                var observed = ObservedReturnValue(functionSource) ?? observedForProduced;
                valueExplained |= AddIsNullHypothesis(p, observed, root, activeSources, producedName, hypotheses, Cite, childrenOf, confirmed);
                valueConfirmed |= confirmed && valueExplained;
            }
        }

        if (rowTablesToCheck.Count > 0 && !valueConfirmed)
        {
            nextSteps.Add(p.NextCheckRows(rowTablesToCheck.ToList()));
        }

        // 5. Base columns and parameters.
        var columnHops = bundle.HopsOfType(NodeType.Column).ToList();
        if (columnHops.Count > 0)
        {
            var ids = Cite(columnHops.Select(h => h.NodeId));
            var tables = columnHops.Select(h => h.ContainerNodeId).Where(t => t is not null).Distinct(StringComparer.Ordinal).Select(t => NodeIds.OwnerKeyOf(t!)).ToList();
            facts.Add(new KnownFact(p.FactBaseColumns(columnHops.Select(p.Qualify).Distinct(StringComparer.Ordinal), tables), ids));
        }

        foreach (var parameter in bundle.HopsOfType(NodeType.SystemParameter).DistinctBy(h => h.NodeId))
        {
            if (parameter.ViaRelation == RelationType.ControlledBy)
            {
                continue; // already covered by the branch facts
            }

            var ids = Cite([parameter.NodeId, parameter.ViaEdgeId], parameter.RuntimeEvidenceIds);
            var value = parameter.RuntimeValueTexts.Count > 0 ? ValueOf(parameter.RuntimeValueTexts[0]) : null;
            facts.Add(new KnownFact(p.FactParameterFeeds(parameter.Name, value, parentOf.GetValueOrDefault(parameter)?.Name ?? focus.Name), ids));
        }

        // 6. Unknowns & pending. Static gaps (unscanned definitions) and a missing runtime value for an investigation
        //    force Need More Evidence; runtime details that were merely not captured are reported but do not.
        var unknownHops = bundle.Hops.Where(h => h.Status != NodeStatus.Known && !h.IsRepeat).DistinctBy(h => h.NodeId).ToList();
        foreach (var pending in unknownHops.Where(h => h.Status == NodeStatus.Pending))
        {
            nextSteps.Add(p.NextFinishPending(pending.Name));
        }

        var unknowns = bundle.Unknowns.ToList();
        var runtimeNotes = bundle.RuntimeNotes.ToList();
        steps.Add(p.StepCheckedDefinitions(unknownHops.Count));

        if (investigate && root.RuntimeEvidenceIds.Count == 0 && bundle.TraceId is not null)
        {
            unknowns.Add(p.UnknownNoRuntimeValue(focus.Name, bundle.TraceId, bundle.Scope));
            nextSteps.Add(p.NextRunTracePickOrder);
        }

        var staticGap = unknowns.Count > 0 || unknownHops.Count > 0;
        var openHypotheses = hypotheses.Count(h => h.Status == HypothesisStatus.Unverified);
        var hypothesisOnly = !staticGap && openHypotheses > 0;
        var verdict = staticGap || openHypotheses > 0 ? ExplainVerdict.NeedMoreEvidence : ExplainVerdict.Known;
        var confidence = Confidence(unknownHops.Count, facts.Count, root.RuntimeEvidenceIds.Count > 0, runtimeNotes.Count, hypothesisOnly);

        string? cause = null;
        if (verdict == ExplainVerdict.Known && causeFunction is not null && causeValue is not null)
        {
            cause = p.CauseFunctionReturned(causeFunction, causeValue);
        }

        var summary = p.Summary(bundle.Focus.Name, facts.Count, OriginatingProcedure(bundle), root.RuntimeValueTexts, verdict, unknownHops.Select(h => h.Name), hypothesisOnly, runtimeNotes.Count, cause);

        return new Explanation
        {
            Summary = summary,
            Steps = steps,
            KnownFacts = facts,
            Hypotheses = hypotheses,
            Unknowns = unknowns.Concat(runtimeNotes).Distinct(StringComparer.Ordinal).ToList(),
            NextSteps = nextSteps.Distinct(StringComparer.Ordinal).ToList(),
            Confidence = confidence,
            Verdict = verdict,
            Audit = Audit(cited, note: investigate ? "investigate" : "explain"),
        };
    }

    /// <summary>The stored procedure the value originates in: the first SP hop, else the SP on the execution path, else the owner of a SQL hop.</summary>
    private static string? OriginatingProcedure(EvidenceBundle bundle)
    {
        var sp = bundle.HopsOfType(NodeType.StoredProcedure).FirstOrDefault()?.Name
                 ?? bundle.ExecutionPath.FirstOrDefault(n => n.Type == NodeType.StoredProcedure)?.Name;
        return sp ?? bundle.Hops.Where(h => h.ContainerNodeId is not null && h.ContainerNodeId.StartsWith("sp:", StringComparison.Ordinal)).Select(h => NodeIds.OwnerKeyOf(h.ContainerNodeId!)).FirstOrDefault();
    }

    /// <summary>
    /// The observed value equals the ISNULL default of a candidate branch. When the producing function's return was
    /// captured, the match is <see cref="HypothesisStatus.Supported"/>; otherwise it stays unverified. Skipped when
    /// the branch wraps a scanned function without a captured return (its own RETURN expression is examined instead).
    /// </summary>
    private static bool AddIsNullHypothesis(
        InvestigatorPhrases p,
        string? observed,
        HopSummary root,
        List<HopSummary> candidates,
        string producedName,
        List<Hypothesis> hypotheses,
        Func<IEnumerable<string?>[], string[]> cite,
        Dictionary<HopSummary, List<HopSummary>> childrenOf,
        bool confirmed)
    {
        if (observed is null)
        {
            return false;
        }

        // A scanned function on this branch is examined via its own RETURN expression; do not treat the outer
        // ISNULL(udf(...), 0) as the explanation even when the function return is already captured.
        if (candidates.Any(s => s.Type == NodeType.Function && s.Status == NodeStatus.Known && childrenOf.ContainsKey(s)))
        {
            return false;
        }

        var added = false;
        foreach (var expression in candidates.Select(s => s.Expression).Where(e => e is not null).Distinct(StringComparer.Ordinal))
        {
            var match = IsNullDefault().Match(expression!);
            if (match.Success && string.Equals(match.Groups["default"].Value.Trim(), observed, StringComparison.OrdinalIgnoreCase))
            {
                var sources = candidates.Where(s => s.Expression == expression).ToList();
                var text = confirmed
                    ? p.HypothesisIsNullFallbackConfirmed(producedName, observed, expression!)
                    : p.HypothesisIsNullFallback(producedName, observed, expression!);
                hypotheses.Add(new Hypothesis(
                    text,
                    confirmed ? HypothesisStatus.Supported : HypothesisStatus.Unverified,
                    confirmed ? null : p.CheckInnerFunction,
                    cite([root.RuntimeEvidenceIds, sources.Select(s => s.NodeId), sources.Select(s => s.ViaEdgeId), sources.SelectMany(s => s.RuntimeEvidenceIds)])));
                added = true;
            }
        }

        return added;
    }

    /// <summary>The observed value equals a constant branch (<c>RETURN 1000</c>): supported when the function return was captured.</summary>
    private static bool AddConstantHypothesis(
        InvestigatorPhrases p,
        string? observed,
        IReadOnlyList<LiteralBranch> constants,
        string producedName,
        List<Hypothesis> hypotheses,
        Func<IEnumerable<string?>[], string[]> cite,
        bool confirmed)
    {
        if (observed is null)
        {
            return false;
        }

        var added = false;
        foreach (var constant in constants.Where(c => c.Condition is not null && ValuesEqual(c.Literal, observed)))
        {
            var text = confirmed
                ? p.HypothesisConstantBranchConfirmed(producedName, observed, constant.Condition!)
                : p.HypothesisConstantBranch(producedName, observed, constant.Condition!);
            hypotheses.Add(new Hypothesis(
                text,
                confirmed ? HypothesisStatus.Supported : HypothesisStatus.Unverified,
                confirmed ? null : p.CheckConstantBranch(constant.Condition!),
                cite([[constant.LineageId]])));
            added = true;
        }

        return added;
    }

    /// <summary>
    /// Runtime value texts that are return/result values, not timing chips overlaid on the same SP/UDF node
    /// (labels like <c>dbo.X 6 ms = 6</c>).
    /// </summary>
    private static IReadOnlyList<string> ReturnValueTexts(HopSummary? hop)
    {
        if (hop is null || hop.RuntimeValueTexts.Count == 0)
        {
            return [];
        }

        var returns = hop.RuntimeValueTexts.Where(t => !t.Contains(" ms = ", StringComparison.Ordinal)).ToList();
        return returns.Count > 0 ? returns : hop.RuntimeValueTexts;
    }

    private static string? ObservedReturnValue(HopSummary? hop)
        => ReturnValueTexts(hop).Select(ValueOf).FirstOrDefault();

    private readonly record struct RowContextValues(string PickQuantity, string OnHand, string Allocated, string StorageBin);

    /// <summary>Sibling API-response leaves of the same material row, when present in the evidence bundle.</summary>
    private static bool TryRowContext(EvidenceBundle bundle, out RowContextValues values, out IEnumerable<string?>[] evidenceIds)
    {
        values = default;
        evidenceIds = [];
        if (bundle.Scope is null)
        {
            return false;
        }

        string? Value(string suffix)
        {
            var item = bundle.Items.FirstOrDefault(i =>
                i.Kind == EvidenceKind.Runtime
                && i.NodeId == $"json:pickOrderRows.{suffix}"
                && string.Equals(i.Scope, bundle.Scope, StringComparison.OrdinalIgnoreCase)
                && i.Value is not null);
            return item?.Value is { } v ? EvidenceBundleBuilder.Format(v).Trim('"') : null;
        }

        var pickQuantity = Value("pickQuantity");
        var onHand = Value("pickStorageBin.onHandQuantity");
        var allocated = Value("pickStorageBin.allocatedQuantity");
        var storageBin = Value("pickStorageBin.storageBin");
        if (pickQuantity is null || onHand is null || allocated is null || storageBin is null)
        {
            return false;
        }

        values = new RowContextValues(pickQuantity, onHand, allocated, storageBin);
        evidenceIds =
        [
            bundle.Items.Where(i =>
                    i.Kind == EvidenceKind.Runtime
                    && i.NodeId is "json:pickOrderRows.pickQuantity"
                        or "json:pickOrderRows.pickStorageBin.onHandQuantity"
                        or "json:pickOrderRows.pickStorageBin.allocatedQuantity"
                        or "json:pickOrderRows.pickStorageBin.storageBin"
                    && string.Equals(i.Scope, bundle.Scope, StringComparison.OrdinalIgnoreCase))
                .Select(i => (string?)i.Id),
        ];
        return true;
    }

    private static IReadOnlyList<string> TablesOf(IEnumerable<HopSummary> columns)
        => columns.Select(c => c.ContainerNodeId).Where(t => t is not null).Distinct(StringComparer.Ordinal).Select(t => NodeIds.OwnerKeyOf(t!)).ToList();

    /// <summary>The variable a scanner note is about: <c>@UseQuantityAllocated</c> in <c>@UseQuantityAllocated = 0 -> 1 WHEN ...</c>.</summary>
    private static string VariableOf(string note)
    {
        var idx = note.IndexOf(' ', StringComparison.Ordinal);
        return idx < 0 ? note : note[..idx];
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

    /// <summary>
    /// 0.95 minus 0.2 per Unknown/Pending hop, 0.05 without a runtime value, 0.05 per runtime detail that was not
    /// captured, and 0.1 when the observed value rests on an unverified hypothesis; clamped to [0.3, 0.95].
    /// </summary>
    private static double Confidence(int unknownCount, int factCount, bool hasRuntime, int runtimeNotes, bool hypothesisOnly)
    {
        if (factCount == 0)
        {
            return 0.0;
        }

        var confidence = 0.95 - 0.2 * unknownCount - (hasRuntime ? 0.0 : 0.05) - 0.05 * runtimeNotes - (hypothesisOnly ? 0.1 : 0.0);
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
