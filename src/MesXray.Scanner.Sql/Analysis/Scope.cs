namespace MesXray.Scanner.Sql.Analysis;

/// <summary>Kind of relation a column can be resolved against.</summary>
public enum RelationKind
{
    BaseTable,
    Cte,
    TempTable,
    DerivedTable,
    Result,
}

/// <summary>An intermediate relation (CTE, temp table, derived table) or the final result set, with its column nodes.</summary>
public sealed class RelationInfo
{
    public RelationInfo(string name, RelationKind kind, string? nodeId)
    {
        Name = name;
        Kind = kind;
        NodeId = nodeId;
    }

    public string Name { get; }

    public RelationKind Kind { get; }

    /// <summary>Node id of the relation itself (null for the final result set, whose owner is the SP).</summary>
    public string? NodeId { get; }

    /// <summary>Column name -> column node id, case-insensitive like SQL Server's default collation.</summary>
    public Dictionary<string, string> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Columns in declaration order (for positional INSERT ... SELECT mapping).</summary>
    public List<string> ColumnOrder { get; } = [];

    public void AddColumn(string name, string nodeId)
    {
        if (!Columns.ContainsKey(name))
        {
            ColumnOrder.Add(name);
        }

        Columns[name] = nodeId;
    }
}

/// <summary>What a table alias in a FROM clause points at.</summary>
public sealed record RelationRef(RelationKind Kind, string Schema, string Name, RelationInfo? Intermediate);

/// <summary>Aliases visible to one query (FROM/JOIN/APPLY), with the enclosing scope for correlated references.</summary>
public sealed class QueryScope
{
    private readonly Dictionary<string, RelationRef> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly QueryScope? _parent;

    public QueryScope(QueryScope? parent = null)
    {
        _parent = parent;
    }

    public IEnumerable<RelationRef> Relations => _aliases.Values;

    public void Add(string alias, RelationRef relation) => _aliases[alias] = relation;

    public RelationRef? Resolve(string alias)
        => _aliases.TryGetValue(alias, out var r) ? r : _parent?.Resolve(alias);

    /// <summary>Resolves an unqualified column: unique relation in scope, or an intermediate that projects the column.</summary>
    public RelationRef? ResolveUnqualified(string column)
    {
        var owning = _aliases.Values.Where(r => r.Intermediate is not null && r.Intermediate.Columns.ContainsKey(column)).Distinct().ToList();
        if (owning.Count == 1)
        {
            return owning[0];
        }

        var distinct = _aliases.Values.Distinct().ToList();
        if (distinct.Count == 1)
        {
            return distinct[0];
        }

        return _parent?.ResolveUnqualified(column);
    }
}
