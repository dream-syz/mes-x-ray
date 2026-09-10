using System.Text.Json;
using MesXray.Domain.Graph;
using MesXray.Domain.Serialization;

namespace MesXray.Graph.Store;

/// <summary>Reads and writes <see cref="GraphSnapshot"/> JSON files (ground truth, scanner output, exports).</summary>
public static class GraphSnapshotFile
{
    public static GraphSnapshot Read(string path)
    {
        using var stream = File.OpenRead(path);
        var snapshot = JsonSerializer.Deserialize<GraphSnapshot>(stream, XRayJson.Options)
            ?? throw new InvalidDataException($"'{path}' does not contain a graph snapshot.");
        Validate(snapshot, path);
        return snapshot;
    }

    public static async Task<GraphSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var snapshot = await JsonSerializer.DeserializeAsync<GraphSnapshot>(stream, XRayJson.Options, cancellationToken)
            ?? throw new InvalidDataException($"'{path}' does not contain a graph snapshot.");
        Validate(snapshot, path);
        return snapshot;
    }

    public static void Write(GraphSnapshot snapshot, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, snapshot, XRayJson.Indented);
    }

    public static string Serialize(GraphSnapshot snapshot) => JsonSerializer.Serialize(snapshot, XRayJson.Indented);

    private static void Validate(GraphSnapshot snapshot, string path)
    {
        if (snapshot.SchemaVersion != GraphSnapshot.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"'{path}' has schema version {snapshot.SchemaVersion}; expected {GraphSnapshot.CurrentSchemaVersion}.");
        }

        var duplicateNode = snapshot.Nodes.GroupBy(n => n.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicateNode is not null)
        {
            throw new InvalidDataException($"'{path}' defines node '{duplicateNode.Key}' more than once.");
        }
    }
}
