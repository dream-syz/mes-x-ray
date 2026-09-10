namespace MesXray.Domain.Graph;

/// <summary>Where a node is defined: a source file (C# or SQL) and a 1-based line range.</summary>
public sealed record SourceLocation(string Path, int StartLine, int EndLine)
{
    public override string ToString() => StartLine == EndLine
        ? $"{Path}:L{StartLine}"
        : $"{Path}:L{StartLine}-L{EndLine}";
}
