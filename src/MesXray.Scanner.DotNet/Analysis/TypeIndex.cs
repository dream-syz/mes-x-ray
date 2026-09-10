using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MesXray.Scanner.DotNet.Analysis;

/// <summary>Everything the rules need to know about the types declared in the scanned source.</summary>
public sealed class TypeIndex
{
    private readonly Dictionary<INamedTypeSymbol, TypeDeclarationSyntax> _declarations;
    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> _implementations;
    private readonly HashSet<INamedTypeSymbol> _models;
    private readonly HashSet<INamedTypeSymbol> _controllers;

    private TypeIndex(
        Dictionary<INamedTypeSymbol, TypeDeclarationSyntax> declarations,
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> implementations,
        HashSet<INamedTypeSymbol> models,
        HashSet<INamedTypeSymbol> controllers)
    {
        _declarations = declarations;
        _implementations = implementations;
        _models = models;
        _controllers = controllers;
    }

    public IEnumerable<INamedTypeSymbol> Types => _declarations.Keys;

    public IEnumerable<INamedTypeSymbol> Models => _models;

    public static TypeIndex Build(Compilation compilation)
    {
        var declarations = new Dictionary<INamedTypeSymbol, TypeDeclarationSyntax>(SymbolEqualityComparer.Default);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is INamedTypeSymbol symbol && !declarations.ContainsKey(symbol))
                {
                    declarations[symbol] = declaration;
                }
            }
        }

        var implementations = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        foreach (var type in declarations.Keys.Where(t => t.TypeKind == TypeKind.Class))
        {
            foreach (var iface in type.AllInterfaces)
            {
                if (!declarations.ContainsKey(iface))
                {
                    continue;
                }

                if (!implementations.TryGetValue(iface, out var list))
                {
                    list = [];
                    implementations[iface] = list;
                }

                list.Add(type);
            }
        }

        var models = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var controllers = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var (type, declaration) in declarations)
        {
            if (IsController(type, declaration))
            {
                controllers.Add(type);
            }
            else if (IsDto(type, declaration))
            {
                models.Add(type);
            }
        }

        return new TypeIndex(declarations, implementations, models, controllers);
    }

    public bool IsDeclaredInSource(ISymbol? symbol)
        => symbol?.ContainingType is INamedTypeSymbol type
            ? _declarations.ContainsKey(type.OriginalDefinition)
            : symbol is INamedTypeSymbol named && _declarations.ContainsKey(named.OriginalDefinition);

    public bool IsModel(ITypeSymbol? type)
        => type is INamedTypeSymbol named && _models.Contains(named.OriginalDefinition);

    public bool IsController(INamedTypeSymbol type) => _controllers.Contains(type);

    public IReadOnlyList<INamedTypeSymbol> ImplementationsOf(INamedTypeSymbol iface)
        => _implementations.TryGetValue(iface, out var list) ? list : [];

    public TypeDeclarationSyntax? DeclarationOf(INamedTypeSymbol type)
        => _declarations.GetValueOrDefault(type.OriginalDefinition);

    /// <summary>Controllers: [ApiController] / [Route] attribute, ControllerBase/Controller base type or *Controller name.</summary>
    private static bool IsController(INamedTypeSymbol type, TypeDeclarationSyntax declaration)
    {
        if (type.TypeKind != TypeKind.Class)
        {
            return false;
        }

        var attributeNames = declaration.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString());
        if (attributeNames.Any(n => n is "ApiController" or "ApiControllerAttribute" or "Route" or "RouteAttribute"))
        {
            return true;
        }

        var bases = declaration.BaseList?.Types.Select(t => t.Type.ToString()) ?? [];
        return bases.Any(b => b is "ControllerBase" or "Controller") || type.Name.EndsWith("Controller", StringComparison.Ordinal);
    }

    /// <summary>DTO heuristic: a class with public properties and no methods other than constructors.</summary>
    private static bool IsDto(INamedTypeSymbol type, TypeDeclarationSyntax declaration)
    {
        if (type.TypeKind != TypeKind.Class || type.IsStatic)
        {
            return false;
        }

        var hasProperties = declaration.Members.OfType<PropertyDeclarationSyntax>()
            .Any(p => p.Modifiers.Any(SyntaxKind.PublicKeyword));
        var hasMethods = declaration.Members.OfType<MethodDeclarationSyntax>().Any();
        return hasProperties && !hasMethods;
    }
}
