namespace MesXray.Domain.Graph;

/// <summary>Where a piece of structural evidence (an edge) came from.</summary>
public enum EvidenceType
{
    /// <summary>Derived from C# syntax/semantic analysis.</summary>
    Roslyn,

    /// <summary>Derived from T-SQL parsing (ScriptDom + rules).</summary>
    SqlParser,

    /// <summary>Observed at runtime (fixture or live diagnostics).</summary>
    Runtime,

    /// <summary>Hand-curated ground truth or manual override.</summary>
    Manual,

    /// <summary>Derived deterministically by linking outputs of two scanners (e.g. Dapper column -> property).</summary>
    Linker,

    /// <summary>
    /// Read from the site's configuration: the value an options property is bound to at the site under study
    /// (e.g. the stored procedure name a Dapper call takes from <c>PickingOptions.StorageBinProcedure</c>).
    /// </summary>
    Configuration,
}
