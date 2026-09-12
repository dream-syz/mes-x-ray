using MesXray.Domain.Graph;
using MesXray.Domain.Scanning;
using MesXray.Scanner.Sql.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace MesXray.Scanner.Sql;

/// <summary>
/// T-SQL scanner built on ScriptDom. Parses <c>CREATE PROCEDURE</c> / <c>CREATE FUNCTION</c> scripts and emits SP/UDF
/// nodes, table reads, system parameter usage, function calls and column-level lineage (aliases, CASE branches,
/// aggregates, CTE / temp table / derived table hops).
/// </summary>
public sealed class SqlScanner : IScanner
{
    public const string ScannerName = "sql-scanner";
    public const string ScannerVersion = "0.1.0";

    private readonly SqlScannerOptions _options;

    public SqlScanner(SqlScannerOptions? options = null)
    {
        _options = options ?? SqlScannerOptions.Default;
    }

    public string Name => ScannerName;

    public string Version => ScannerVersion;

    public ScanResult ScanDirectory(string rootPath) => Scan(ScanRequest.FromDirectory(rootPath, "*.sql"));

    public ScanResult Scan(ScanRequest request)
    {
        var builder = new SnapshotBuilder(ScannerName, $"{ScannerName}@{ScannerVersion}");
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);

        foreach (var file in request.Files.OrderBy(f => f, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(request.RootPath, file).Replace('\\', '/');
            using var reader = new StreamReader(file);
            var fragment = parser.Parse(reader, out var errors);

            foreach (var error in errors)
            {
                builder.Report(ScanDiagnosticSeverity.Error, $"Parse error: {error.Message}", relative, error.Line);
            }

            if (fragment is null)
            {
                continue;
            }

            var objects = new ObjectCollector();
            fragment.Accept(objects);
            if (objects.Procedures.Count == 0 && objects.Functions.Count == 0)
            {
                builder.Report(ScanDiagnosticSeverity.Info, "No CREATE PROCEDURE / FUNCTION found.", relative);
            }

            foreach (var procedure in objects.Procedures)
            {
                ScanProcedure(builder, procedure, relative);
            }

            foreach (var function in objects.Functions)
            {
                ScanFunction(builder, function, relative);
            }
        }

        return builder.Build();
    }

    private void ScanProcedure(SnapshotBuilder builder, ProcedureStatementBody procedure, string file)
    {
        var (schema, name) = SqlText.ObjectName(procedure.ProcedureReference.Name, _options.DefaultSchema);
        var ownerKey = NodeIds.SqlOwnerKey(name, schema);
        var ownerId = NodeIds.StoredProcedure(name, schema);

        var analyzer = new SqlObjectAnalyzer(builder, _options, file, ownerId, ownerKey);
        builder.Define(new Node
        {
            Id = ownerId,
            Type = NodeType.StoredProcedure,
            Name = name,
            QualifiedName = ownerKey,
            Layer = Layer.Data,
            Source = new SourceLocation(file, procedure.StartLine, SqlText.EndLine(procedure)),
            Metadata = new Dictionary<string, string>
            {
                ["objectType"] = "procedure",
                ["parameters"] = FormatParameters(procedure.Parameters),
            },
        });

        analyzer.Analyze(procedure.StatementList);

        builder.Define(new Node
        {
            Id = ownerId,
            Type = NodeType.StoredProcedure,
            Name = name,
            Layer = Layer.Data,
            Metadata = new Dictionary<string, string> { ["resultSets"] = analyzer.ResultSets.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        });
    }

    private void ScanFunction(SnapshotBuilder builder, FunctionStatementBody function, string file)
    {
        var (schema, name) = SqlText.ObjectName(function.Name, _options.DefaultSchema);
        var ownerKey = NodeIds.SqlOwnerKey(name, schema);
        var ownerId = NodeIds.Function(name, schema);

        builder.Define(new Node
        {
            Id = ownerId,
            Type = NodeType.Function,
            Name = name,
            QualifiedName = ownerKey,
            Layer = Layer.Data,
            Source = new SourceLocation(file, function.StartLine, SqlText.EndLine(function)),
            Metadata = new Dictionary<string, string>
            {
                ["objectType"] = function.ReturnType is TableValuedFunctionReturnType or SelectFunctionReturnType ? "tableValuedFunction" : "scalarFunction",
                ["parameters"] = FormatParameters(function.Parameters),
                ["returnType"] = function.ReturnType is ScalarFunctionReturnType scalar ? SqlText.Of(scalar.DataType) : "TABLE",
            },
        });

        var analyzer = new SqlObjectAnalyzer(builder, _options, file, ownerId, ownerKey);
        analyzer.Analyze(function.StatementList);

        switch (function.ReturnType)
        {
            case SelectFunctionReturnType inline:
                {
                    // Inline TVF: the SELECT is the result relation.
                    var wrapper = new StatementList();
                    wrapper.Statements.Add(inline.SelectStatement);
                    analyzer.Analyze(wrapper);
                    break;
                }

            case ScalarFunctionReturnType:
                // Scalar UDF: its value is what the RETURN statements yield, so callers can trace through it.
                analyzer.EmitReturnValue();
                break;
        }
    }

    private static string FormatParameters(IList<ProcedureParameter> parameters)
        => string.Join(", ", parameters.Select(p => $"{p.VariableName.Value} {SqlText.Of(p.DataType)}{(p.Modifier == ParameterModifier.Output ? " OUTPUT" : string.Empty)}"));

    private sealed class ObjectCollector : TSqlFragmentVisitor
    {
        public List<ProcedureStatementBody> Procedures { get; } = [];

        public List<FunctionStatementBody> Functions { get; } = [];

        public override void Visit(ProcedureStatementBody node) => Procedures.Add(node);

        public override void Visit(FunctionStatementBody node) => Functions.Add(node);
    }
}
