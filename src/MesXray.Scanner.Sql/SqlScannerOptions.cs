namespace MesXray.Scanner.Sql;

/// <summary>
/// Rule-layer configuration for the T-SQL scanner. The parser is generic; these lists encode how *this* codebase
/// reads system parameters so the scanner can turn <c>dbo.AF_GetSystemParameterValue('WMS_Enabled', ...)</c> into a
/// SystemParameter node instead of an opaque function call.
/// </summary>
public sealed class SqlScannerOptions
{
    /// <summary>Scalar functions whose first string-literal argument is a system parameter name.</summary>
    public IReadOnlyList<string> SystemParameterFunctions { get; init; } =
    [
        "AF_GetSystemParameterValue",
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

    public static SqlScannerOptions Default { get; } = new();
}
