using MesXray.Domain.Graph;
using MesXray.Domain.Scanning;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace MesXray.Scanner.Sql.Analysis;

/// <summary>
/// Analyses one stored procedure or function body and emits:
/// <list type="bullet">
/// <item>SP -> USES_PARAMETER -> SystemParameter (with the local variable it is loaded into and how it is used).</item>
/// <item>SP -> READS -> Table, SP -> CALLS_FUNCTION -> Function, SP -> EXECUTES_SP -> SP.</item>
/// <item>Result columns, CTE/temp/derived-table columns, and for every projected expression: EXPR -> PRODUCES -> column,
/// EXPR -> DERIVED_FROM / COMPUTED_BY / CONTROLLED_BY -> sources, plus <see cref="FieldLineage"/> records per branch.</item>
/// </list>
/// Anything the rules cannot resolve is reported as a diagnostic and left out - never guessed.
/// </summary>
public sealed class SqlObjectAnalyzer : ISourceResolver
{
    public const string ResultRelation = "$";

    private readonly SnapshotBuilder _builder;
    private readonly SqlScannerOptions _options;
    private readonly string _file;
    private readonly string _ownerId;
    private readonly string _ownerKey;
    private readonly Dictionary<string, string> _variableBindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RelationInfo> _relations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Variable, SortedSet<string> Usages)> _parameterUsage = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _writeTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ExpressionSourceCollector _collector;
    private QueryScope _currentScope = new();
    private int _resultSets;

    public SqlObjectAnalyzer(SnapshotBuilder builder, SqlScannerOptions options, string file, string ownerId, string ownerKey)
    {
        _builder = builder;
        _options = options;
        _file = file;
        _ownerId = ownerId;
        _ownerKey = ownerKey;
        _collector = new ExpressionSourceCollector(this);
    }

    public int ResultSets => _resultSets;

    public void Analyze(StatementList? body)
    {
        if (body is null)
        {
            return;
        }

        CollectWriteTargets(body);
        VisitStatements(body);
        RegisterRemainingTableReads(body);
        RegisterFunctionCalls(body);
        FlushParameterUsage();
    }

    // ------------------------------------------------------------------
    // Statements
    // ------------------------------------------------------------------

    private void VisitStatements(StatementList list)
    {
        foreach (var statement in list.Statements)
        {
            VisitStatement(statement);
        }
    }

    private void VisitStatement(TSqlStatement statement)
    {
        switch (statement)
        {
            case DeclareVariableStatement declare:
                foreach (var element in declare.Declarations)
                {
                    if (element.Value is not null)
                    {
                        BindVariable(element.VariableName.Value, element.Value);
                    }
                }

                break;

            case SetVariableStatement set:
                BindVariable(set.Variable.Name, set.Expression);
                break;

            case SelectStatement select:
                VisitSelect(select);
                break;

            case InsertStatement insert:
                VisitInsert(insert.InsertSpecification);
                break;

            case CreateTableStatement create when SqlText.IsTempTable(create.SchemaObjectName.BaseIdentifier.Value):
                RegisterTempTable(create);
                break;

            case IfStatement ifStatement:
                RecordFilterVariables(ifStatement.Predicate, "branch");
                VisitStatement(ifStatement.ThenStatement);
                if (ifStatement.ElseStatement is not null)
                {
                    VisitStatement(ifStatement.ElseStatement);
                }

                break;

            case BeginEndBlockStatement block:
                VisitStatements(block.StatementList);
                break;

            case WhileStatement loop:
                VisitStatement(loop.Statement);
                break;

            case TryCatchStatement tryCatch:
                VisitStatements(tryCatch.TryStatements);
                VisitStatements(tryCatch.CatchStatements);
                break;

            case ExecuteStatement execute:
                VisitExecute(execute);
                break;

            default:
                break;
        }
    }

    private void VisitSelect(SelectStatement select)
    {
        if (select.WithCtesAndXmlNamespaces is not null)
        {
            foreach (var cte in select.WithCtesAndXmlNamespaces.CommonTableExpressions)
            {
                var name = cte.ExpressionName.Value;
                var relation = DefineIntermediate(name, RelationKind.Cte, cte);
                var explicitColumns = cte.Columns.Count > 0 ? cte.Columns.Select(c => c.Value).ToList() : null;
                AnalyzeQueryExpression(cte.QueryExpression, relation, parent: null, explicitColumns);
                _relations[name] = relation;
            }
        }

        if (select.QueryExpression is null)
        {
            return;
        }

        var specification = FirstSpecification(select.QueryExpression);
        if (specification?.SelectElements.Any(e => e is SelectSetVariable) == true)
        {
            AnalyzeVariableSelect(specification);
            return;
        }

        if (select.Into is not null)
        {
            var tempName = select.Into.BaseIdentifier.Value;
            var relation = DefineIntermediate(tempName, SqlText.IsTempTable(tempName) ? RelationKind.TempTable : RelationKind.DerivedTable, select);
            AnalyzeQueryExpression(select.QueryExpression, relation, parent: null, explicitColumns: null);
            _relations[tempName] = relation;
            return;
        }

        _resultSets++;
        var result = new RelationInfo(ResultRelation, RelationKind.Result, nodeId: null);
        AnalyzeQueryExpression(select.QueryExpression, result, parent: null, explicitColumns: null);
    }

    private void VisitInsert(InsertSpecification insert)
    {
        if (insert.Target is not NamedTableReference target || insert.InsertSource is not SelectInsertSource source)
        {
            return;
        }

        var name = target.SchemaObject.BaseIdentifier.Value;
        var columns = insert.Columns.Select(c => c.MultiPartIdentifier.Identifiers.Last().Value).ToList();

        if (SqlText.IsTempTable(name) || _relations.ContainsKey(name))
        {
            if (!_relations.TryGetValue(name, out var relation))
            {
                relation = DefineIntermediate(name, RelationKind.TempTable, insert);
                _relations[name] = relation;
            }

            var targetColumns = columns.Count > 0 ? columns : relation.ColumnOrder.ToList();
            AnalyzeQueryExpression(source.Select, relation, parent: null, targetColumns.Count > 0 ? targetColumns : null);
            return;
        }

        // Writing to a base table is outside the read-only POC: analyse the SELECT for reads only.
        _builder.Report(ScanDiagnosticSeverity.Info, $"{_ownerKey} writes to {name}; write lineage is out of scope for this iteration.", _file, insert.StartLine);
        var scratch = new RelationInfo(name, RelationKind.DerivedTable, nodeId: null);
        AnalyzeQueryExpression(source.Select, scratch, parent: null, explicitColumns: null, emitColumns: false);
    }

    private void VisitExecute(ExecuteStatement execute)
    {
        if (execute.ExecuteSpecification?.ExecutableEntity is not ExecutableProcedureReference procedure
            || procedure.ProcedureReference?.ProcedureReference?.Name is not { } name)
        {
            return;
        }

        var (schema, spName) = SqlText.ObjectName(name, _options.DefaultSchema);
        var spId = NodeIds.StoredProcedure(spName, schema);
        _builder.Reference(spId, NodeType.StoredProcedure, spName, Layer.Data, "Executed from another procedure; definition not scanned.", $"{schema}.{spName}");
        _builder.Link(_ownerId, RelationType.ExecutesSp, spId, EvidenceType.SqlParser, Evidence(execute));
    }

    private void RegisterTempTable(CreateTableStatement create)
    {
        var name = create.SchemaObjectName.BaseIdentifier.Value;
        var relation = DefineIntermediate(name, RelationKind.TempTable, create);
        foreach (var column in create.Definition.ColumnDefinitions)
        {
            var columnName = column.ColumnIdentifier.Value;
            var id = NodeIds.IntermediateColumn(_ownerKey, name, columnName);
            _builder.Define(new Node
            {
                Id = id,
                Type = NodeType.IntermediateColumn,
                Name = columnName,
                QualifiedName = $"{_ownerKey}.{name}.{columnName}",
                Layer = Layer.Data,
                Source = new SourceLocation(_file, column.StartLine, column.StartLine),
                Metadata = new Dictionary<string, string>
                {
                    ["relation"] = name,
                    ["dataType"] = SqlText.Of(column.DataType),
                },
            });
            _builder.Link(relation.NodeId!, RelationType.Contains, id, EvidenceType.SqlParser, Evidence(column));
            relation.AddColumn(columnName, id);
        }

        _relations[name] = relation;
    }

    private RelationInfo DefineIntermediate(string name, RelationKind kind, TSqlFragment fragment)
    {
        if (_relations.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var id = NodeIds.Intermediate(_ownerKey, name);
        _builder.Define(new Node
        {
            Id = id,
            Type = NodeType.Intermediate,
            Name = name,
            QualifiedName = $"{_ownerKey}.{name}",
            Layer = Layer.Data,
            Source = new SourceLocation(_file, fragment.StartLine, SqlText.EndLine(fragment)),
            Metadata = new Dictionary<string, string>
            {
                ["kind"] = kind switch
                {
                    RelationKind.Cte => "cte",
                    RelationKind.TempTable => "tempTable",
                    _ => "derivedTable",
                },
                ["owner"] = _ownerKey,
            },
        });
        _builder.Link(_ownerId, RelationType.Contains, id, EvidenceType.SqlParser, Evidence(fragment));
        return new RelationInfo(name, kind, id);
    }

    // ------------------------------------------------------------------
    // Queries
    // ------------------------------------------------------------------

    private void AnalyzeQueryExpression(QueryExpression query, RelationInfo target, QueryScope? parent, IReadOnlyList<string>? explicitColumns, bool emitColumns = true)
    {
        switch (query)
        {
            case QuerySpecification spec:
                AnalyzeSpecification(spec, target, parent, explicitColumns, emitColumns);
                break;
            case BinaryQueryExpression binary:
                // UNION/EXCEPT/INTERSECT: both sides feed the same positional columns.
                AnalyzeQueryExpression(binary.FirstQueryExpression, target, parent, explicitColumns, emitColumns);
                AnalyzeQueryExpression(binary.SecondQueryExpression, target, parent, explicitColumns ?? target.ColumnOrder.ToList(), emitColumns);
                break;
            case QueryParenthesisExpression paren:
                AnalyzeQueryExpression(paren.QueryExpression, target, parent, explicitColumns, emitColumns);
                break;
        }
    }

    private static QuerySpecification? FirstSpecification(QueryExpression query) => query switch
    {
        QuerySpecification spec => spec,
        BinaryQueryExpression binary => FirstSpecification(binary.FirstQueryExpression),
        QueryParenthesisExpression paren => FirstSpecification(paren.QueryExpression),
        _ => null,
    };

    private void AnalyzeSpecification(QuerySpecification spec, RelationInfo target, QueryScope? parent, IReadOnlyList<string>? explicitColumns, bool emitColumns)
    {
        var previousScope = _currentScope;
        var scope = new QueryScope(parent);
        _currentScope = scope;
        try
        {
            if (spec.FromClause is not null)
            {
                foreach (var reference in spec.FromClause.TableReferences)
                {
                    RegisterTableReference(reference, scope);
                }
            }

            RecordFilterVariables(spec.WhereClause?.SearchCondition, "filter");

            if (!emitColumns)
            {
                return;
            }

            var containerId = target.NodeId ?? _ownerId;
            var index = 0;
            foreach (var element in spec.SelectElements)
            {
                switch (element)
                {
                    case SelectScalarExpression scalar:
                        AnalyzeSelectItem(scalar, index, target, containerId, explicitColumns);
                        index++;
                        break;

                    case SelectStarExpression star:
                        index += ExpandStar(star, target, containerId, scope);
                        break;
                }
            }
        }
        finally
        {
            _currentScope = previousScope;
        }
    }

    private void AnalyzeSelectItem(SelectScalarExpression item, int index, RelationInfo target, string containerId, IReadOnlyList<string>? explicitColumns)
    {
        var name = explicitColumns is not null && index < explicitColumns.Count
            ? explicitColumns[index]
            : item.ColumnName?.Value
              ?? (item.Expression as ColumnReferenceExpression)?.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value
              ?? $"Column{index + 1}";

        var columnId = target.Kind == RelationKind.Result
            ? NodeIds.ResultColumn(_ownerKey, name)
            : target.Columns.TryGetValue(name, out var known) ? known : NodeIds.IntermediateColumn(_ownerKey, target.Name, name);

        var expressionText = SqlText.Of(item.Expression);
        _builder.Define(new Node
        {
            Id = columnId,
            Type = target.Kind == RelationKind.Result ? NodeType.ResultColumn : NodeType.IntermediateColumn,
            Name = name,
            QualifiedName = target.Kind == RelationKind.Result ? $"{_ownerKey}.{name}" : $"{_ownerKey}.{target.Name}.{name}",
            Layer = Layer.Data,
            Source = new SourceLocation(_file, item.StartLine, SqlText.EndLine(item)),
            Metadata = new Dictionary<string, string>
            {
                ["relation"] = target.Name,
                ["expression"] = expressionText,
            },
        });
        _builder.Link(containerId, RelationType.Contains, columnId, EvidenceType.SqlParser, Evidence(item));
        target.AddColumn(name, columnId);

        var evidence = Evidence(item);

        // 1. Plain column reference -> alias.
        if (item.Expression is ColumnReferenceExpression columnRef)
        {
            var resolved = ResolveColumn(columnRef);
            if (resolved is null)
            {
                _builder.AddLineage(new FieldLineage
                {
                    OutputFieldId = columnId,
                    SourceFieldId = null,
                    TransformType = TransformType.Direct,
                    Expression = expressionText,
                    Confidence = 0.5,
                });
                return;
            }

            var edge = _builder.Link(columnId, RelationType.AliasOf, resolved.Value.NodeId, EvidenceType.SqlParser, evidence);
            _builder.AddLineage(new FieldLineage
            {
                OutputFieldId = columnId,
                SourceFieldId = resolved.Value.NodeId,
                TransformType = TransformType.Direct,
                Expression = expressionText,
                EvidenceEdgeIds = [edge.Id],
            });
            return;
        }

        // 2. Anything else is an expression node that produces the column.
        var exprId = NodeIds.Expression(_ownerKey, target.Name, name);
        var kind = ExpressionSourceCollector.Kind(item.Expression);
        _builder.Define(new Node
        {
            Id = exprId,
            Type = NodeType.Expression,
            Name = SqlText.Truncate(expressionText, _options.MaxExpressionNameLength),
            QualifiedName = $"{_ownerKey}.{target.Name}.{name} = {expressionText}",
            Layer = Layer.Data,
            Source = new SourceLocation(_file, item.StartLine, SqlText.EndLine(item)),
            Metadata = new Dictionary<string, string>
            {
                ["expression"] = expressionText,
                ["kind"] = kind,
                ["produces"] = columnId,
            },
        });
        var produces = _builder.Link(exprId, RelationType.Produces, columnId, EvidenceType.SqlParser, evidence);

        if (ExpressionSourceCollector.IsLiteral(item.Expression))
        {
            _builder.AddLineage(new FieldLineage
            {
                OutputFieldId = columnId,
                SourceFieldId = null,
                TransformType = TransformType.Literal,
                Expression = expressionText,
                EvidenceEdgeIds = [produces.Id],
            });
            return;
        }

        var sources = _collector.Collect(item.Expression);
        if (sources.Count == 0)
        {
            _builder.AddLineage(new FieldLineage
            {
                OutputFieldId = columnId,
                SourceFieldId = null,
                TransformType = TransformType.Expression,
                Expression = expressionText,
                EvidenceEdgeIds = [produces.Id],
                Confidence = 0.5,
            });
            _builder.Report(ScanDiagnosticSeverity.Warning, $"No resolvable source for {columnId}: {SqlText.Truncate(expressionText, 120)}", _file, item.StartLine);
            return;
        }

        EmitSources(exprId, columnId, expressionText, sources, produces.Id, evidence);
    }

    private void EmitSources(string exprId, string columnId, string expressionText, List<SourceRef> sources, string producesEdgeId, string evidence)
    {
        foreach (var group in sources.GroupBy(s => (s.NodeId, Relation: RelationFor(s))))
        {
            var first = group.First();
            EnsureSourceNode(first);

            var conditions = group.Select(s => s.Condition).Where(c => c is not null).Distinct(StringComparer.Ordinal).ToList();
            var roles = group.Select(s => s.Role.ToString().ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
            var metadata = new Dictionary<string, string> { ["role"] = string.Join("|", roles) };
            if (conditions.Count > 0 && group.All(s => s.Condition is not null))
            {
                metadata["condition"] = string.Join(" | ", conditions);
            }

            if (first.NodeType == NodeType.SystemParameter)
            {
                var variable = _variableBindings.FirstOrDefault(kv => string.Equals(kv.Value, first.Name, StringComparison.OrdinalIgnoreCase)).Key;
                if (variable is not null)
                {
                    metadata["variable"] = variable;
                }

                NoteParameterUsage(first.Name, variable, first.Role == SourceRole.Control ? "branch" : "value");
            }

            var edge = _builder.Link(exprId, group.Key.Relation, first.NodeId, EvidenceType.SqlParser, evidence, 1.0, metadata);

            foreach (var source in group.DistinctBy(s => s.Condition ?? string.Empty))
            {
                _builder.AddLineage(new FieldLineage
                {
                    OutputFieldId = columnId,
                    SourceFieldId = source.NodeId,
                    TransformType = source.Role == SourceRole.Control ? TransformType.Conditional : source.Transform,
                    Expression = source.Role == SourceRole.Control ? expressionText : source.BranchExpression ?? expressionText,
                    Condition = source.Condition,
                    EvidenceEdgeIds = [edge.Id, producesEdgeId],
                });
            }
        }
    }

    private static RelationType RelationFor(SourceRef source) => source switch
    {
        { Role: SourceRole.Control } => RelationType.ControlledBy,
        { NodeType: NodeType.Function } => RelationType.ComputedBy,
        _ => RelationType.DerivedFrom,
    };

    private void EnsureSourceNode(SourceRef source)
    {
        switch (source.NodeType)
        {
            case NodeType.SystemParameter:
                _builder.Define(new Node
                {
                    Id = source.NodeId,
                    Type = NodeType.SystemParameter,
                    Name = source.Name,
                    QualifiedName = source.Name,
                    Layer = Layer.Config,
                    Metadata = new Dictionary<string, string> { ["discoveredIn"] = _ownerKey },
                });
                break;
            case NodeType.Function:
                _builder.Reference(source.NodeId, NodeType.Function, source.Name, Layer.Data,
                    "Function referenced but its definition was not scanned.", source.QualifiedName);
                _builder.Link(_ownerId, RelationType.CallsFunction, source.NodeId, EvidenceType.SqlParser, $"{_file}");
                break;
        }
    }

    private int ExpandStar(SelectStarExpression star, RelationInfo target, string containerId, QueryScope scope)
    {
        var relations = star.Qualifier is null
            ? scope.Relations.Distinct().ToList()
            : new[] { scope.Resolve(star.Qualifier.Identifiers.Last().Value) }.Where(r => r is not null).Select(r => r!).ToList();

        var count = 0;
        foreach (var relation in relations)
        {
            if (relation.Intermediate is null)
            {
                _builder.Report(ScanDiagnosticSeverity.Warning, $"SELECT * over base table {relation.Schema}.{relation.Name} in {_ownerKey}: columns unknown.", _file, star.StartLine);
                continue;
            }

            foreach (var columnName in relation.Intermediate.ColumnOrder)
            {
                var sourceId = relation.Intermediate.Columns[columnName];
                var columnId = target.Kind == RelationKind.Result
                    ? NodeIds.ResultColumn(_ownerKey, columnName)
                    : NodeIds.IntermediateColumn(_ownerKey, target.Name, columnName);
                _builder.Define(new Node
                {
                    Id = columnId,
                    Type = target.Kind == RelationKind.Result ? NodeType.ResultColumn : NodeType.IntermediateColumn,
                    Name = columnName,
                    Layer = Layer.Data,
                    Source = new SourceLocation(_file, star.StartLine, star.StartLine),
                    Metadata = new Dictionary<string, string> { ["relation"] = target.Name, ["expression"] = "*" },
                });
                _builder.Link(containerId, RelationType.Contains, columnId, EvidenceType.SqlParser, Evidence(star));
                var edge = _builder.Link(columnId, RelationType.AliasOf, sourceId, EvidenceType.SqlParser, Evidence(star));
                _builder.AddLineage(new FieldLineage { OutputFieldId = columnId, SourceFieldId = sourceId, TransformType = TransformType.Direct, Expression = "*", EvidenceEdgeIds = [edge.Id] });
                target.AddColumn(columnName, columnId);
                count++;
            }
        }

        return count;
    }

    private void AnalyzeVariableSelect(QuerySpecification spec)
    {
        var previousScope = _currentScope;
        var scope = new QueryScope();
        _currentScope = scope;
        try
        {
            if (spec.FromClause is not null)
            {
                foreach (var reference in spec.FromClause.TableReferences)
                {
                    RegisterTableReference(reference, scope);
                }
            }

            RecordFilterVariables(spec.WhereClause?.SearchCondition, "filter");

            // SELECT @X = Value FROM SYSTEM_PARAMETER WHERE Name = 'X'
            var parameterTable = scope.Relations.FirstOrDefault(r => r.Kind == RelationKind.BaseTable && _options.SystemParameterTables.Contains(r.Name, StringComparer.OrdinalIgnoreCase));
            var parameterName = parameterTable is null ? null : FindParameterNameLiteral(spec.WhereClause?.SearchCondition);

            foreach (var element in spec.SelectElements.OfType<SelectSetVariable>())
            {
                if (parameterName is not null)
                {
                    Bind(element.Variable.Name, parameterName, "declare");
                }
                else
                {
                    BindVariable(element.Variable.Name, element.Expression);
                }
            }
        }
        finally
        {
            _currentScope = previousScope;
        }
    }

    // ------------------------------------------------------------------
    // FROM clause
    // ------------------------------------------------------------------

    private void RegisterTableReference(TableReference reference, QueryScope scope)
    {
        switch (reference)
        {
            case NamedTableReference named:
                RegisterNamedTable(named, scope);
                break;

            case QualifiedJoin join:
                RegisterTableReference(join.FirstTableReference, scope);
                RegisterTableReference(join.SecondTableReference, scope);
                RecordFilterVariables(join.SearchCondition, "filter");
                break;

            case UnqualifiedJoin apply:
                RegisterTableReference(apply.FirstTableReference, scope);
                RegisterTableReference(apply.SecondTableReference, scope);
                break;

            case JoinParenthesisTableReference paren:
                RegisterTableReference(paren.Join, scope);
                break;

            case QueryDerivedTable derived:
                RegisterDerivedTable(derived, scope);
                break;

            case SchemaObjectFunctionTableReference tvf:
                {
                    var (schema, name) = SqlText.ObjectName(tvf.SchemaObject, _options.DefaultSchema);
                    var id = NodeIds.Function(name, schema);
                    _builder.Reference(id, NodeType.Function, name, Layer.Data, "Table-valued function referenced; definition not scanned.", $"{schema}.{name}");
                    _builder.Link(_ownerId, RelationType.CallsFunction, id, EvidenceType.SqlParser, Evidence(tvf));
                    _builder.Report(ScanDiagnosticSeverity.Info, $"Columns of table-valued function {schema}.{name} are not tracked.", _file, tvf.StartLine);
                    break;
                }

            case VariableTableReference:
                _builder.Report(ScanDiagnosticSeverity.Info, $"Table variable in {_ownerKey} is not tracked.", _file, reference.StartLine);
                break;

            default:
                _builder.Report(ScanDiagnosticSeverity.Info, $"Unsupported table reference {reference.GetType().Name} in {_ownerKey}.", _file, reference.StartLine);
                break;
        }
    }

    private void RegisterNamedTable(NamedTableReference named, QueryScope scope)
    {
        var name = named.SchemaObject.BaseIdentifier.Value;
        var alias = named.Alias?.Value ?? name;

        if (_relations.TryGetValue(name, out var intermediate))
        {
            scope.Add(alias, new RelationRef(intermediate.Kind, string.Empty, name, intermediate));
            return;
        }

        if (SqlText.IsTempTable(name))
        {
            var relation = DefineIntermediate(name, RelationKind.TempTable, named);
            _relations[name] = relation;
            scope.Add(alias, new RelationRef(RelationKind.TempTable, string.Empty, name, relation));
            return;
        }

        var (schema, tableName) = SqlText.ObjectName(named.SchemaObject, _options.DefaultSchema);
        var tableId = NodeIds.Table(tableName, schema);
        _builder.Define(new Node
        {
            Id = tableId,
            Type = NodeType.Table,
            Name = tableName,
            QualifiedName = $"{schema}.{tableName}",
            Layer = Layer.Data,
            Metadata = new Dictionary<string, string> { ["inferred"] = "referenced from SQL; schema metadata not imported" },
        });
        _builder.Link(_ownerId, RelationType.Reads, tableId, EvidenceType.SqlParser, Evidence(named));
        scope.Add(alias, new RelationRef(RelationKind.BaseTable, schema, tableName, null));
    }

    private void RegisterDerivedTable(QueryDerivedTable derived, QueryScope scope)
    {
        var alias = derived.Alias?.Value ?? $"Derived{derived.StartLine}";
        var relation = DefineIntermediate(alias, RelationKind.DerivedTable, derived);
        var explicitColumns = derived.Columns.Count > 0 ? derived.Columns.Select(c => c.Value).ToList() : null;
        AnalyzeQueryExpression(derived.QueryExpression, relation, parent: scope, explicitColumns);
        scope.Add(alias, new RelationRef(RelationKind.DerivedTable, string.Empty, alias, relation));
    }

    // ------------------------------------------------------------------
    // ISourceResolver
    // ------------------------------------------------------------------

    public (string NodeId, NodeType Type, string Name, string? QualifiedName)? ResolveColumn(ColumnReferenceExpression column)
    {
        var identifiers = column.MultiPartIdentifier?.Identifiers;
        if (identifiers is null || identifiers.Count == 0)
        {
            return null;
        }

        var columnName = identifiers[^1].Value;
        var relation = identifiers.Count >= 2
            ? _currentScope.Resolve(identifiers[^2].Value)
            : _currentScope.ResolveUnqualified(columnName);

        if (relation is null)
        {
            _builder.Report(ScanDiagnosticSeverity.Warning, $"Could not resolve column '{SqlText.Of(column)}' in {_ownerKey}.", _file, column.StartLine);
            return null;
        }

        if (relation.Intermediate is not null)
        {
            if (!relation.Intermediate.Columns.TryGetValue(columnName, out var id))
            {
                id = NodeIds.IntermediateColumn(_ownerKey, relation.Name, columnName);
                _builder.Define(new Node
                {
                    Id = id,
                    Type = NodeType.IntermediateColumn,
                    Name = columnName,
                    QualifiedName = $"{_ownerKey}.{relation.Name}.{columnName}",
                    Layer = Layer.Data,
                    Metadata = new Dictionary<string, string> { ["relation"] = relation.Name, ["inferred"] = "referenced before/without explicit projection" },
                });
                if (relation.Intermediate.NodeId is not null)
                {
                    _builder.Link(relation.Intermediate.NodeId, RelationType.Contains, id, EvidenceType.SqlParser, Evidence(column));
                }

                relation.Intermediate.AddColumn(columnName, id);
            }

            return (id, NodeType.IntermediateColumn, columnName, $"{relation.Name}.{columnName}");
        }

        var columnId = NodeIds.Column(relation.Name, columnName, relation.Schema);
        _builder.Define(new Node
        {
            Id = columnId,
            Type = NodeType.Column,
            Name = columnName,
            QualifiedName = $"{relation.Schema}.{relation.Name}.{columnName}",
            Layer = Layer.Data,
            Metadata = new Dictionary<string, string> { ["table"] = $"{relation.Schema}.{relation.Name}", ["inferred"] = "referenced from SQL; schema metadata not imported" },
        });
        _builder.Link(NodeIds.Table(relation.Name, relation.Schema), RelationType.Contains, columnId, EvidenceType.SqlParser, Evidence(column));
        return (columnId, NodeType.Column, columnName, $"{relation.Schema}.{relation.Name}.{columnName}");
    }

    public string? ParameterBoundTo(string variableName)
        => _variableBindings.GetValueOrDefault(variableName);

    public string? SystemParameterRead(FunctionCall call)
    {
        if (!_options.SystemParameterFunctions.Contains(call.FunctionName.Value, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return call.Parameters.FirstOrDefault() is StringLiteral literal ? literal.Value : null;
    }

    public (string Schema, string Name)? UserDefinedFunction(FunctionCall call)
    {
        if (call.CallTarget is MultiPartIdentifierCallTarget target)
        {
            var schema = target.MultiPartIdentifier.Identifiers.LastOrDefault()?.Value ?? _options.DefaultSchema;
            return (schema, call.FunctionName.Value);
        }

        return null;
    }

    public IEnumerable<SourceRef> CollectSubquery(ScalarSubquery subquery, CollectContext context)
    {
        if (FirstSpecification(subquery.QueryExpression) is not { } spec)
        {
            return [];
        }

        var previousScope = _currentScope;
        var scope = new QueryScope(previousScope);
        _currentScope = scope;
        try
        {
            if (spec.FromClause is not null)
            {
                foreach (var reference in spec.FromClause.TableReferences)
                {
                    RegisterTableReference(reference, scope);
                }
            }

            RecordFilterVariables(spec.WhereClause?.SearchCondition, "filter");

            var results = new List<SourceRef>();
            foreach (var scalar in spec.SelectElements.OfType<SelectScalarExpression>())
            {
                foreach (var source in _collector.Collect(scalar.Expression))
                {
                    results.Add(source with
                    {
                        Condition = source.Condition ?? context.Condition,
                        Transform = context.InConditional ? TransformType.Conditional : source.Transform,
                        BranchExpression = source.BranchExpression ?? context.BranchExpression,
                    });
                }
            }

            return results;
        }
        finally
        {
            _currentScope = previousScope;
        }
    }

    // ------------------------------------------------------------------
    // Variables & system parameters
    // ------------------------------------------------------------------

    private void BindVariable(string variableName, ScalarExpression value)
    {
        var call = FunctionCalls(value).FirstOrDefault(c => SystemParameterRead(c) is not null);
        if (call is null)
        {
            return;
        }

        Bind(variableName, SystemParameterRead(call)!, "declare");
    }

    private void Bind(string variableName, string parameterName, string usage)
    {
        _variableBindings[variableName] = parameterName;
        _builder.Define(new Node
        {
            Id = NodeIds.SystemParameter(parameterName),
            Type = NodeType.SystemParameter,
            Name = parameterName,
            QualifiedName = parameterName,
            Layer = Layer.Config,
            Metadata = new Dictionary<string, string> { ["discoveredIn"] = _ownerKey },
        });
        NoteParameterUsage(parameterName, variableName, usage);
    }

    private void RecordFilterVariables(BooleanExpression? predicate, string usage)
    {
        if (predicate is null)
        {
            return;
        }

        var visitor = new VariableCollector();
        predicate.Accept(visitor);
        foreach (var variable in visitor.Variables)
        {
            if (_variableBindings.TryGetValue(variable, out var parameter))
            {
                NoteParameterUsage(parameter, variable, usage);
            }
        }

        // Inline reads: WHERE X = dbo.AF_GetSystemParameterValue('P', ...)
        foreach (var call in FunctionCalls(predicate))
        {
            var parameter = SystemParameterRead(call);
            if (parameter is not null)
            {
                Bind($"<inline:{parameter}>", parameter, usage);
            }
        }
    }

    private string? FindParameterNameLiteral(BooleanExpression? predicate)
    {
        if (predicate is null)
        {
            return null;
        }

        var visitor = new ComparisonCollector();
        predicate.Accept(visitor);
        foreach (var comparison in visitor.Comparisons)
        {
            if (comparison.FirstExpression is ColumnReferenceExpression column
                && string.Equals(column.MultiPartIdentifier.Identifiers[^1].Value, _options.SystemParameterNameColumn, StringComparison.OrdinalIgnoreCase)
                && comparison.SecondExpression is StringLiteral literal)
            {
                return literal.Value;
            }
        }

        return null;
    }

    private void NoteParameterUsage(string parameterName, string? variable, string usage)
    {
        if (!_parameterUsage.TryGetValue(parameterName, out var entry))
        {
            entry = (variable ?? string.Empty, new SortedSet<string>(StringComparer.Ordinal));
            _parameterUsage[parameterName] = entry;
        }

        if (string.IsNullOrEmpty(entry.Variable) && variable is not null && !variable.StartsWith("<inline", StringComparison.Ordinal))
        {
            entry = (variable, entry.Usages);
            _parameterUsage[parameterName] = entry;
        }

        entry.Usages.Add(usage);
    }

    private void FlushParameterUsage()
    {
        foreach (var (parameter, (variable, usages)) in _parameterUsage.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var metadata = new Dictionary<string, string> { ["usage"] = string.Join("|", usages) };
            if (!string.IsNullOrEmpty(variable))
            {
                metadata["variable"] = variable;
            }

            _builder.Link(_ownerId, RelationType.UsesParameter, NodeIds.SystemParameter(parameter), EvidenceType.SqlParser, _file, 1.0, metadata);
        }
    }

    // ------------------------------------------------------------------
    // Whole-body sweeps
    // ------------------------------------------------------------------

    private void CollectWriteTargets(StatementList body)
    {
        var visitor = new WriteTargetCollector();
        body.Accept(visitor);
        foreach (var name in visitor.Targets)
        {
            _writeTargets.Add(name);
        }
    }

    /// <summary>Tables referenced in places the query analyser does not visit (EXISTS/IN subqueries, UPDATE ... FROM).</summary>
    private void RegisterRemainingTableReads(StatementList body)
    {
        var visitor = new NamedTableCollector();
        body.Accept(visitor);
        foreach (var named in visitor.Tables)
        {
            var name = named.SchemaObject.BaseIdentifier.Value;
            if (SqlText.IsTempTable(name) || _relations.ContainsKey(name) || _writeTargets.Contains(name))
            {
                continue;
            }

            var (schema, tableName) = SqlText.ObjectName(named.SchemaObject, _options.DefaultSchema);
            var tableId = NodeIds.Table(tableName, schema);
            _builder.Define(new Node
            {
                Id = tableId,
                Type = NodeType.Table,
                Name = tableName,
                QualifiedName = $"{schema}.{tableName}",
                Layer = Layer.Data,
                Metadata = new Dictionary<string, string> { ["inferred"] = "referenced from SQL; schema metadata not imported" },
            });
            _builder.Link(_ownerId, RelationType.Reads, tableId, EvidenceType.SqlParser, Evidence(named));
        }
    }

    private void RegisterFunctionCalls(StatementList body)
    {
        foreach (var call in FunctionCalls(body))
        {
            var udf = UserDefinedFunction(call);
            if (udf is null)
            {
                continue;
            }

            var (schema, name) = udf.Value;
            var id = NodeIds.Function(name, schema);
            _builder.Reference(id, NodeType.Function, name, Layer.Data, "Function referenced but its definition was not scanned.", $"{schema}.{name}");
            _builder.Link(_ownerId, RelationType.CallsFunction, id, EvidenceType.SqlParser, Evidence(call));
        }
    }

    private static IEnumerable<FunctionCall> FunctionCalls(TSqlFragment fragment)
    {
        var visitor = new FunctionCallCollector();
        fragment.Accept(visitor);
        return visitor.Calls;
    }

    private string Evidence(TSqlFragment fragment)
    {
        var end = SqlText.EndLine(fragment);
        return end == fragment.StartLine ? $"{_file}:L{fragment.StartLine}" : $"{_file}:L{fragment.StartLine}-L{end}";
    }

    // ------------------------------------------------------------------
    // Small visitors
    // ------------------------------------------------------------------

    private sealed class FunctionCallCollector : TSqlFragmentVisitor
    {
        public List<FunctionCall> Calls { get; } = [];

        public override void Visit(FunctionCall node) => Calls.Add(node);
    }

    private sealed class VariableCollector : TSqlFragmentVisitor
    {
        public HashSet<string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void Visit(VariableReference node) => Variables.Add(node.Name);
    }

    private sealed class ComparisonCollector : TSqlFragmentVisitor
    {
        public List<BooleanComparisonExpression> Comparisons { get; } = [];

        public override void Visit(BooleanComparisonExpression node) => Comparisons.Add(node);
    }

    private sealed class NamedTableCollector : TSqlFragmentVisitor
    {
        public List<NamedTableReference> Tables { get; } = [];

        public override void Visit(NamedTableReference node) => Tables.Add(node);
    }

    private sealed class WriteTargetCollector : TSqlFragmentVisitor
    {
        public HashSet<string> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void Visit(InsertSpecification node) => Add(node.Target);

        public override void Visit(UpdateSpecification node) => Add(node.Target);

        public override void Visit(DeleteSpecification node) => Add(node.Target);

        public override void Visit(MergeSpecification node) => Add(node.Target);

        public override void Visit(CreateTableStatement node) => Targets.Add(node.SchemaObjectName.BaseIdentifier.Value);

        public override void Visit(DropTableStatement node)
        {
            foreach (var o in node.Objects)
            {
                Targets.Add(o.BaseIdentifier.Value);
            }
        }

        private void Add(TableReference? target)
        {
            if (target is NamedTableReference named)
            {
                Targets.Add(named.SchemaObject.BaseIdentifier.Value);
            }
        }
    }
}
