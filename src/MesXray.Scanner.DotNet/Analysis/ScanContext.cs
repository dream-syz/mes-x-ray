using MesXray.Domain.Graph;
using MesXray.Domain.Scanning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MesXray.Scanner.DotNet.Analysis;

/// <summary>Shared state and id/evidence helpers for all .NET scanner rules.</summary>
public sealed class ScanContext
{
    public ScanContext(Compilation compilation, TypeIndex types, SnapshotBuilder builder, string rootPath, SiteSettings? siteSettings = null)
    {
        Compilation = compilation;
        Types = types;
        Builder = builder;
        RootPath = Path.GetFullPath(rootPath);
        SiteSettings = siteSettings ?? SiteSettings.Empty;
    }

    public Compilation Compilation { get; }

    public TypeIndex Types { get; }

    public SnapshotBuilder Builder { get; }

    public string RootPath { get; }

    /// <summary>Configured values of the site, for names the code only knows at runtime.</summary>
    public SiteSettings SiteSettings { get; }

    // ----- ids -----

    public static string MethodId(IMethodSymbol method) => NodeIds.Method(method.ContainingType.Name, method.Name);

    public static string ModelId(ITypeSymbol type) => NodeIds.Model(type.Name);

    public static string FieldId(IPropertySymbol property) => NodeIds.Field(property.ContainingType.Name, property.Name);

    public static string QualifiedName(ISymbol symbol)
        => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    // ----- evidence -----

    public string RelativePath(SyntaxNode node)
    {
        var path = node.SyntaxTree.FilePath;
        if (string.IsNullOrEmpty(path))
        {
            return "<in-memory>";
        }

        var full = Path.GetFullPath(path);
        var relative = full.StartsWith(RootPath, StringComparison.Ordinal) ? Path.GetRelativePath(RootPath, full) : full;
        return relative.Replace('\\', '/');
    }

    public SourceLocation Location(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return new SourceLocation(RelativePath(node), span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1);
    }

    public string EvidenceRef(SyntaxNode node) => Location(node).ToString();

    public int LineOf(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    // ----- semantic helpers -----

    public SemanticModel SemanticModelFor(SyntaxNode node) => Compilation.GetSemanticModel(node.SyntaxTree);

    /// <summary>Resolves an invocation to a method symbol, falling back to the first candidate when binding failed.</summary>
    public IMethodSymbol? ResolveMethod(InvocationExpressionSyntax invocation)
    {
        var info = SemanticModelFor(invocation).GetSymbolInfo(invocation);
        return info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }

    public IPropertySymbol? ResolveProperty(ExpressionSyntax expression)
    {
        var info = SemanticModelFor(expression).GetSymbolInfo(expression);
        return info.Symbol as IPropertySymbol ?? info.CandidateSymbols.OfType<IPropertySymbol>().FirstOrDefault();
    }

    /// <summary>Task&lt;T&gt;, ActionResult&lt;T&gt;, IEnumerable&lt;T&gt;, List&lt;T&gt;, T[] and T? all unwrap to T.</summary>
    public static ITypeSymbol Unwrap(ITypeSymbol type)
    {
        while (true)
        {
            switch (type)
            {
                case IArrayTypeSymbol array:
                    type = array.ElementType;
                    continue;
                case INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } generic
                    when IsWrapper(generic.OriginalDefinition.Name):
                    type = generic.TypeArguments[0];
                    continue;
                case INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } generic
                    when generic.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T:
                    type = generic.TypeArguments[0];
                    continue;
                default:
                    return type;
            }
        }
    }

    public static bool IsCollection(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol => true,
        INamedTypeSymbol { IsGenericType: true } named => named.OriginalDefinition.Name is "List" or "IList" or "IEnumerable" or "ICollection"
            or "IReadOnlyList" or "IReadOnlyCollection" or "HashSet" or "ISet",
        _ => false,
    };

    private static bool IsWrapper(string name) => name is "Task" or "ValueTask" or "ActionResult" or "Ok" or "OkObjectResult"
        or "List" or "IList" or "IEnumerable" or "ICollection" or "IReadOnlyList" or "IReadOnlyCollection" or "HashSet" or "ISet"
        or "Nullable";
}
