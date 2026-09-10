namespace MesXray.AI.Contracts;

/// <summary>Request body of <c>POST /api/xray/ai/explain</c> (design §7.1).</summary>
public sealed record ExplainRequest
{
    /// <summary>Node to explain: a JSON field, model field, result column, SP or parameter id (or a short field name).</summary>
    public required string FocusNodeId { get; init; }

    public string? Question { get; init; }

    /// <summary>Live trace to overlay; when null the explanation is purely static.</summary>
    public string? TraceId { get; init; }

    /// <summary>Row scope inside the trace (e.g. material number).</summary>
    public string? Scope { get; init; }

    /// <summary>Optional restriction: only these evidence ids may be cited.</summary>
    public IReadOnlyList<string>? AllowedEvidenceIds { get; init; }
}

/// <summary>Request body of <c>POST /api/xray/ai/investigate</c>: a question about a trace, answered read-only.</summary>
public sealed record InvestigateRequest
{
    /// <summary>Optional free-text question; when omitted the investigator explains the focus field's observed value.</summary>
    public string? Question { get; init; }

    public required string TraceId { get; init; }

    public string? FocusNodeId { get; init; }

    public string? Scope { get; init; }

    public IReadOnlyList<string>? AllowedEvidenceIds { get; init; }
}

public enum HypothesisStatus
{
    Unverified,
    Supported,
    Refuted,
}

public enum ExplainVerdict
{
    /// <summary>Every step of the explanation is backed by evidence.</summary>
    Known,

    /// <summary>The path contains Unknown/Pending nodes or missing runtime evidence.</summary>
    NeedMoreEvidence,

    /// <summary>Nothing can be said from the available evidence.</summary>
    Unknown,
}

/// <summary>A statement that is directly backed by evidence ids from the bundle.</summary>
public sealed record KnownFact(string Text, IReadOnlyList<string> EvidenceIds);

/// <summary>A statement the evidence does not (yet) prove. Always carries a status and, when possible, how to check it.</summary>
public sealed record Hypothesis(string Text, HypothesisStatus Status, string? SuggestedCheck = null, IReadOnlyList<string>? EvidenceIds = null);

/// <summary>Who produced the conclusion and from what - required by design §10.</summary>
public sealed record AiAudit(
    string Provider,
    string Model,
    string PromptVersion,
    DateTimeOffset Timestamp,
    IReadOnlyList<string> EvidenceIds,
    string? Note = null);

/// <summary>Response of Explain / Investigate (design §7.1, extended with auditable steps and verdict).</summary>
public sealed record Explanation
{
    public required string Summary { get; init; }

    /// <summary>Auditable steps that were taken - never a hidden chain of thought.</summary>
    public IReadOnlyList<string> Steps { get; init; } = [];

    public IReadOnlyList<KnownFact> KnownFacts { get; init; } = [];

    public IReadOnlyList<Hypothesis> Hypotheses { get; init; } = [];

    public IReadOnlyList<string> Unknowns { get; init; } = [];

    public IReadOnlyList<string> NextSteps { get; init; } = [];

    public double Confidence { get; init; }

    public ExplainVerdict Verdict { get; init; }

    public required AiAudit Audit { get; init; }

    public int EvidenceCount => Audit.EvidenceIds.Count;
}
