using MesXray.AI.Contracts;
using MesXray.AI.Evidence;
using MesXray.Graph.Queries;

namespace MesXray.AI.Investigators;

/// <summary>
/// Evidence interpreter (design §9). Implementations must only state facts that cite ids from
/// <see cref="EvidenceBundle.AllowedEvidenceIds"/>; everything else is a hypothesis or an unknown.
/// </summary>
public interface IAiInvestigator
{
    string Provider { get; }

    /// <summary>Explain Field / Explain Node: where a value comes from, how it is computed, under which conditions.</summary>
    Task<Explanation> ExplainAsync(EvidenceBundle bundle, CancellationToken cancellationToken = default);

    /// <summary>Investigate: answer a question about a trace with steps, facts, hypotheses and unknowns.</summary>
    Task<Explanation> InvestigateAsync(EvidenceBundle bundle, CancellationToken cancellationToken = default);

    /// <summary>Impact Summary: which APIs / UI fields are affected by a change of the origin node.</summary>
    /// <param name="impact">Downstream analysis to summarise.</param>
    /// <param name="language"><c>en</c> (default) or <c>zh</c>; see <see cref="AiLanguage"/>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<Explanation> SummarizeImpactAsync(ImpactResult impact, string? language = null, CancellationToken cancellationToken = default);
}
