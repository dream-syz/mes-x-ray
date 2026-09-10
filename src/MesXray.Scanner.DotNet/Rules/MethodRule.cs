using MesXray.Domain.Graph;
using MesXray.Scanner.DotNet.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MesXray.Scanner.DotNet.Rules;

/// <summary>Every method declared on a class -> METHOD node; return type that is a model -> RETURNS edge.</summary>
public sealed class MethodRule
{
    private readonly ScanContext _ctx;

    public MethodRule(ScanContext ctx)
    {
        _ctx = ctx;
    }

    public void Apply()
    {
        foreach (var type in _ctx.Types.Types.Where(t => t.TypeKind == TypeKind.Class).OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var isController = _ctx.Types.IsController(type);
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary))
            {
                var declaration = method.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<MethodDeclarationSyntax>().FirstOrDefault();
                if (declaration is null)
                {
                    continue;
                }

                var id = ScanContext.MethodId(method);
                var metadata = new Dictionary<string, string>
                {
                    ["returnType"] = method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    ["parameters"] = string.Join(", ", method.Parameters.Select(p => $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {p.Name}")),
                    ["accessibility"] = method.DeclaredAccessibility.ToString().ToLowerInvariant(),
                    ["isAsync"] = method.IsAsync ? "true" : "false",
                    ["class"] = type.Name,
                };

                _ctx.Builder.Define(new Node
                {
                    Id = id,
                    Type = NodeType.Method,
                    Name = method.Name,
                    QualifiedName = ScanContext.QualifiedName(method),
                    Layer = isController ? Layer.Api : Layer.Service,
                    Source = _ctx.Location(declaration),
                    Metadata = metadata,
                    Description = DocSummary(declaration),
                });

                var returned = ScanContext.Unwrap(method.ReturnType);
                if (_ctx.Types.IsModel(returned))
                {
                    _ctx.Builder.Link(id, RelationType.Returns, ScanContext.ModelId(returned), EvidenceType.Roslyn, _ctx.EvidenceRef(declaration.ReturnType),
                        metadata: new Dictionary<string, string> { ["declaredType"] = metadata["returnType"] });
                }
            }
        }
    }

    internal static string? DocSummary(MethodDeclarationSyntax declaration)
    {
        var doc = declaration.GetLeadingTrivia().Select(t => t.GetStructure()).OfType<DocumentationCommentTriviaSyntax>().FirstOrDefault();
        var summary = doc?.Content.OfType<XmlElementSyntax>().FirstOrDefault(e => e.StartTag.Name.ToString() == "summary");
        if (summary is null)
        {
            return null;
        }

        var text = string.Concat(summary.Content.Select(c => c.ToString()));
        var cleaned = string.Join(' ', text.Split('\n').Select(l => l.Trim().TrimStart('/').Trim()).Where(l => l.Length > 0));
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}
