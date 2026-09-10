using MesXray.Domain.Graph;

namespace MesXray.Graph.Store;

/// <summary>
/// Creates <see cref="NodeStatus.Unknown"/> placeholder nodes for ids that are referenced by an edge but never
/// defined. This keeps the graph referentially complete while making the gap explicit ("Need More Evidence").
/// </summary>
public static class NodePlaceholders
{
    public static Node For(string nodeId, string? reason = null)
    {
        var (type, layer) = Infer(nodeId);
        var metadata = new Dictionary<string, string>
        {
            ["reason"] = reason ?? "Referenced but its definition was not scanned.",
        };

        return new Node
        {
            Id = nodeId,
            Type = type,
            Name = DisplayName(nodeId),
            QualifiedName = nodeId[(nodeId.IndexOf(':', StringComparison.Ordinal) + 1)..],
            Layer = layer,
            Status = NodeStatus.Unknown,
            Metadata = metadata,
        };
    }

    public static (NodeType Type, Layer Layer) Infer(string nodeId) => NodeIds.Prefix(nodeId) switch
    {
        "page" => (NodeType.Page, Layer.Web),
        "json" => (NodeType.JsonField, Layer.Web),
        "api" => (NodeType.Api, Layer.Api),
        "method" => (NodeType.Method, Layer.Service),
        "model" => (NodeType.Model, Layer.Api),
        "field" => (NodeType.Field, Layer.Api),
        "sp" => (NodeType.StoredProcedure, Layer.Data),
        "spcol" => (NodeType.ResultColumn, Layer.Data),
        "udf" => (NodeType.Function, Layer.Data),
        "table" => (NodeType.Table, Layer.Data),
        "column" => (NodeType.Column, Layer.Data),
        "cte" or "tmp" => (NodeType.Intermediate, Layer.Data),
        "ctecol" or "tmpcol" => (NodeType.IntermediateColumn, Layer.Data),
        "expr" => (NodeType.Expression, Layer.Data),
        "param" => (NodeType.SystemParameter, Layer.Config),
        "branch" => (NodeType.Branch, Layer.Service),
        _ => (NodeType.Method, Layer.Service),
    };

    private static string DisplayName(string nodeId)
    {
        var prefix = NodeIds.Prefix(nodeId);
        var body = nodeId[(prefix.Length + 1)..];
        return prefix switch
        {
            "api" or "page" => body,
            "sp" or "udf" or "table" => body[(body.IndexOf('.') + 1)..],
            _ => NodeIds.LeafName(nodeId),
        };
    }
}
