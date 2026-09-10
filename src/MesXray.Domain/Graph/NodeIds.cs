namespace MesXray.Domain.Graph;

/// <summary>
/// Stable node id conventions. Scanners, the hand-curated ground truth and manual overrides must all produce the
/// same id for the same thing, otherwise merging and cross-scanner linking cannot work.
/// <list type="bullet">
/// <item><c>page:WebVP.PickOrderDetails</c></item>
/// <item><c>api:GET /cwp/v1/picking/pickOrder</c></item>
/// <item><c>method:PickOrderService.GetPickOrderRows</c> (type + member; namespace lives in QualifiedName)</item>
/// <item><c>model:CWPPickOrderRow</c>, <c>field:CWPPickOrderRow.AvailableQuantity</c></item>
/// <item><c>json:pickOrderRows.availableQuantity</c> (array markers omitted)</item>
/// <item><c>sp:dbo.AP_Pick_GetPickOrderRows</c>, <c>spcol:dbo.AP_Pick_GetPickOrderRows.AvailableQuantity</c></item>
/// <item><c>cte:dbo.AP_X.MaterialTotals</c>, <c>ctecol:dbo.AP_X.MaterialTotals.TotalAvailableQuantity</c></item>
/// <item><c>tmp:dbo.AP_X.#LocationList</c>, <c>tmpcol:dbo.AP_X.#LocationList.StorageBin</c></item>
/// <item><c>expr:dbo.AP_X.MainResults.AvailableQuantity</c> (the expression producing that column)</item>
/// <item><c>udf:dbo.AF_Pick_GetAvailableQuantity</c>, <c>table:dbo.PRODUCT</c>, <c>column:dbo.PRODUCT.ProductNo</c></item>
/// <item><c>param:WMS_Enabled</c></item>
/// </list>
/// </summary>
public static class NodeIds
{
    public const string DefaultSchema = "dbo";

    public static string Page(string name) => $"page:{name}";

    public static string Api(string httpMethod, string route)
        => $"api:{httpMethod.ToUpperInvariant()} {NormalizeRoute(route)}";

    public static string Method(string typeName, string methodName) => $"method:{typeName}.{methodName}";

    public static string Model(string typeName) => $"model:{typeName}";

    public static string Field(string typeName, string propertyName) => $"field:{typeName}.{propertyName}";

    public static string Json(string path) => $"json:{path}";

    public static string StoredProcedure(string name, string? schema = null) => $"sp:{Qualify(schema, name)}";

    public static string Function(string name, string? schema = null) => $"udf:{Qualify(schema, name)}";

    public static string Table(string name, string? schema = null) => $"table:{Qualify(schema, name)}";

    public static string Column(string tableName, string columnName, string? schema = null)
        => $"column:{Qualify(schema, tableName)}.{columnName}";

    /// <summary>Column of a stored procedure's final result set. <paramref name="ownerKey"/> is e.g. <c>dbo.AP_X</c>.</summary>
    public static string ResultColumn(string ownerKey, string columnName) => $"spcol:{ownerKey}.{columnName}";

    public static string Intermediate(string ownerKey, string relationName)
        => $"{IntermediatePrefix(relationName)}:{ownerKey}.{relationName}";

    public static string IntermediateColumn(string ownerKey, string relationName, string columnName)
        => $"{IntermediatePrefix(relationName)}col:{ownerKey}.{relationName}.{columnName}";

    public static string Expression(string ownerKey, string relationName, string columnName)
        => $"expr:{ownerKey}.{relationName}.{columnName}";

    public static string SystemParameter(string name) => $"param:{name}";

    public static string Branch(string typeName, string methodName, string slug) => $"branch:{typeName}.{methodName}.{slug}";

    /// <summary>The owner key (schema.object) used to build member ids for SQL objects.</summary>
    public static string SqlOwnerKey(string name, string? schema = null) => Qualify(schema, name);

    /// <summary>Extracts the owner key from an <c>sp:</c> or <c>udf:</c> node id.</summary>
    public static string OwnerKeyOf(string sqlObjectNodeId)
    {
        var idx = sqlObjectNodeId.IndexOf(':', StringComparison.Ordinal);
        return idx < 0 ? sqlObjectNodeId : sqlObjectNodeId[(idx + 1)..];
    }

    public static string Prefix(string nodeId)
    {
        var idx = nodeId.IndexOf(':', StringComparison.Ordinal);
        return idx < 0 ? string.Empty : nodeId[..idx];
    }

    /// <summary>Last dotted segment of an id: <c>json:a.b.c</c> -> <c>c</c>.</summary>
    public static string LeafName(string nodeId)
    {
        var body = nodeId[(nodeId.IndexOf(':', StringComparison.Ordinal) + 1)..];
        var idx = body.LastIndexOf('.');
        return idx < 0 ? body : body[(idx + 1)..];
    }

    public static string NormalizeRoute(string route)
    {
        var trimmed = route.Trim().Trim('/');
        return "/" + trimmed;
    }

    private static string Qualify(string? schema, string name)
    {
        var s = string.IsNullOrWhiteSpace(schema) ? DefaultSchema : schema.Trim('[', ']');
        return $"{s}.{name.Trim('[', ']')}";
    }

    private static string IntermediatePrefix(string relationName)
        => relationName.StartsWith('#') ? "tmp" : "cte";
}
