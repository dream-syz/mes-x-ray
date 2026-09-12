namespace MesXray.Scanner.Sql;

/// <summary>
/// Rule-layer configuration for the T-SQL scanner. The parser is generic; these lists encode how *this* codebase
/// reads system parameters so the scanner can turn <c>dbo.AF_GetSystemParameterValue('WMS_Enabled', ...)</c> into a
/// SystemParameter node instead of an opaque function call.
/// </summary>
public sealed class SqlScannerOptions
{
    /// <summary>
    /// Functions whose first string-literal argument is a system parameter name: scalar readers
    /// (<c>dbo.AF_GetSystemParameterValue('X', @Facility)</c>) and typed variants, including the table-valued form
    /// (<c>SELECT * FROM dbo.AF_GetSystemParameterValueListString('X') [bit]</c>).
    /// </summary>
    public IReadOnlyList<string> SystemParameterFunctions { get; init; } =
    [
        "AF_GetSystemParameterValue",
        "AF_GetSystemParameterValueint",
        "AF_GetSystemParameterValueListString",
        "AF_GetSystemParameter",
        "AF_GetParameterValue",
        "AF_GetSystemParam",
    ];

    /// <summary>Tables that store system parameters; a <c>Name = 'X'</c> predicate on them is a parameter read.</summary>
    public IReadOnlyList<string> SystemParameterTables { get; init; } =
    [
        "SYSTEM_PARAMETER",
        "AT_SYSTEM_PARAMETER",
    ];

    /// <summary>Column holding the parameter name in <see cref="SystemParameterTables"/>.</summary>
    public string SystemParameterNameColumn { get; init; } = "Name";

    /// <summary>Default schema for unqualified object names.</summary>
    public string DefaultSchema { get; init; } = "dbo";

    /// <summary>Maximum characters kept for expression display names.</summary>
    public int MaxExpressionNameLength { get; init; } = 80;

    /// <summary>
    /// Maximum characters of an <c>IF</c> predicate kept as the branch condition of statements it guards
    /// (<c>IF EXISTS (...) RETURN 1000</c>). EXISTS predicates are abbreviated to their tables and literal comparisons first.
    /// </summary>
    public int MaxBranchConditionLength { get; init; } = 120;

    public static SqlScannerOptions Default { get; } = new();
}
