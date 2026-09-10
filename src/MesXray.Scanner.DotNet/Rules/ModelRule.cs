using MesXray.Domain.Graph;
using MesXray.Scanner.DotNet.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MesXray.Scanner.DotNet.Rules;

/// <summary>DTO/Model -> MODEL node; each public property -> FIELD node (CONTAINS), nested model types -> OF_TYPE.</summary>
public sealed class ModelRule
{
    private readonly ScanContext _ctx;

    public ModelRule(ScanContext ctx)
    {
        _ctx = ctx;
    }

    public void Apply()
    {
        foreach (var model in _ctx.Types.Models.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            var declaration = _ctx.Types.DeclarationOf(model);
            if (declaration is null)
            {
                continue;
            }

            var modelId = ScanContext.ModelId(model);
            _ctx.Builder.Define(new Node
            {
                Id = modelId,
                Type = NodeType.Model,
                Name = model.Name,
                QualifiedName = ScanContext.QualifiedName(model),
                Layer = Layer.Api,
                Source = _ctx.Location(declaration),
                Metadata = new Dictionary<string, string>
                {
                    ["namespace"] = model.ContainingNamespace.ToDisplayString(),
                    ["properties"] = PublicProperties(model).Count().ToString(),
                },
                Description = LeadingDocComment(declaration),
            });

            foreach (var property in PublicProperties(model))
            {
                var propertyDeclaration = property.DeclaringSyntaxReferences
                    .Select(r => r.GetSyntax())
                    .OfType<PropertyDeclarationSyntax>()
                    .FirstOrDefault();

                var unwrapped = ScanContext.Unwrap(property.Type);
                var isModel = _ctx.Types.IsModel(unwrapped);
                var kind = isModel
                    ? (ScanContext.IsCollection(property.Type) ? "collection" : "model")
                    : (ScanContext.IsCollection(property.Type) ? "collection" : "scalar");

                var fieldId = ScanContext.FieldId(property);
                _ctx.Builder.Define(new Node
                {
                    Id = fieldId,
                    Type = NodeType.Field,
                    Name = property.Name,
                    QualifiedName = ScanContext.QualifiedName(property),
                    Layer = Layer.Api,
                    Source = propertyDeclaration is null ? null : _ctx.Location(propertyDeclaration),
                    Metadata = new Dictionary<string, string>
                    {
                        ["clrType"] = property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        ["kind"] = kind,
                        ["jsonName"] = JsonName(property, propertyDeclaration),
                    },
                });

                var evidence = propertyDeclaration is null ? _ctx.EvidenceRef(declaration) : _ctx.EvidenceRef(propertyDeclaration);
                _ctx.Builder.Link(modelId, RelationType.Contains, fieldId, EvidenceType.Roslyn, evidence);

                if (isModel)
                {
                    _ctx.Builder.Link(fieldId, RelationType.OfType, ScanContext.ModelId(unwrapped), EvidenceType.Roslyn, evidence);
                }
            }
        }
    }

    public static IEnumerable<IPropertySymbol> PublicProperties(INamedTypeSymbol type)
        => type.GetMembers().OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic && !p.IsIndexer)
            .OrderBy(p => p.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0);

    /// <summary>[JsonPropertyName("x")] wins; otherwise System.Text.Json's default camelCase policy.</summary>
    public static string JsonName(IPropertySymbol property, PropertyDeclarationSyntax? declaration)
    {
        var attribute = declaration?.AttributeLists.SelectMany(a => a.Attributes)
            .FirstOrDefault(a => a.Name.ToString() is "JsonPropertyName" or "JsonPropertyNameAttribute" or "JsonProperty" or "JsonPropertyAttribute");
        if (attribute?.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal
            && literal.Token.Value is string explicitName)
        {
            return explicitName;
        }

        return CamelCase(property.Name);
    }

    public static string CamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
        {
            return name;
        }

        // Matches JsonNamingPolicy.CamelCase for the common cases (leading acronyms are lower-cased as a block).
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i > 0 && i + 1 < chars.Length && char.IsLower(chars[i + 1]))
            {
                break;
            }

            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }

    private static string? LeadingDocComment(SyntaxNode node)
    {
        var trivia = node.GetLeadingTrivia().Select(t => t.GetStructure()).OfType<DocumentationCommentTriviaSyntax>().FirstOrDefault();
        if (trivia is null)
        {
            return null;
        }

        var text = string.Concat(trivia.Content.OfType<XmlElementSyntax>()
            .Where(e => e.StartTag.Name.ToString() == "summary")
            .SelectMany(e => e.Content.Select(c => c.ToString())));
        var cleaned = string.Join(' ', text.Split('\n').Select(l => l.Trim().TrimStart('/').Trim()).Where(l => l.Length > 0));
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}
