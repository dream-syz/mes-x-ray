using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MesXray.Scanner.DotNet.Analysis;

/// <summary>
/// Builds a <see cref="CSharpCompilation"/> for a set of source files using the host runtime's trusted platform
/// assemblies as references. Third-party packages (Dapper, ASP.NET Core) are usually not resolvable, so every rule
/// must degrade gracefully to syntax-based matching when a symbol cannot be bound.
/// </summary>
public static class CompilationFactory
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> PlatformReferences = new(LoadPlatformReferences);

    public static CSharpCompilation Create(string assemblyName, IEnumerable<SyntaxTree> trees)
    {
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
        return CSharpCompilation.Create(assemblyName, trees, PlatformReferences.Value, options);
    }

    public static SyntaxTree Parse(string text, string path)
        => CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest), path);

    private static IReadOnlyList<MetadataReference> LoadPlatformReferences()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
        return tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    }
}
