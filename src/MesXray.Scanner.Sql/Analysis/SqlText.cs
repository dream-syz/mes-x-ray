using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace MesXray.Scanner.Sql.Analysis;

/// <summary>Text helpers: original source text of a fragment, whitespace normalisation, identifiers.</summary>
public static partial class SqlText
{
    /// <summary>Original text of a fragment (from the token stream), whitespace-normalised.</summary>
    public static string Of(TSqlFragment fragment)
    {
        if (fragment.ScriptTokenStream is null || fragment.FirstTokenIndex < 0 || fragment.LastTokenIndex < fragment.FirstTokenIndex)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (var i = fragment.FirstTokenIndex; i <= fragment.LastTokenIndex; i++)
        {
            var token = fragment.ScriptTokenStream[i];
            if (token.TokenType is TSqlTokenType.MultilineComment or TSqlTokenType.SingleLineComment)
            {
                sb.Append(' ');
                continue;
            }

            sb.Append(token.Text);
        }

        return Normalize(sb.ToString());
    }

    public static string Normalize(string text) => Whitespace().Replace(text, " ").Trim();

    public static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..Math.Max(0, max - 3)] + "...";

    public static int EndLine(TSqlFragment fragment)
    {
        if (fragment.ScriptTokenStream is null || fragment.LastTokenIndex < 0)
        {
            return fragment.StartLine;
        }

        return fragment.ScriptTokenStream[fragment.LastTokenIndex].Line;
    }

    public static string Name(Identifier? identifier) => identifier?.Value ?? string.Empty;

    public static (string Schema, string Name) ObjectName(SchemaObjectName name, string defaultSchema)
        => (name.SchemaIdentifier?.Value ?? defaultSchema, name.BaseIdentifier.Value);

    public static bool IsTempTable(string name) => name.StartsWith('#');

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
