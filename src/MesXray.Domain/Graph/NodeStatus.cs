namespace MesXray.Domain.Graph;

/// <summary>
/// Whether we actually have the definition of a node. "No evidence, no conclusion":
/// anything that is only referenced (never scanned) must be surfaced as Unknown, never guessed.
/// </summary>
public enum NodeStatus
{
    /// <summary>Definition was scanned or manually curated; details are trustworthy.</summary>
    Known,

    /// <summary>Known to exist and deliberately not scanned yet (e.g. GetStorageBin in the first POC).</summary>
    Pending,

    /// <summary>Only referenced by another node; definition not available. UI shows "Need More Evidence".</summary>
    Unknown,
}
