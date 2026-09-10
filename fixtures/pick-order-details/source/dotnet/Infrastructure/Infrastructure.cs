// Minimal infrastructure types referenced by the fixture. Connection strings come from configuration, never code.
using System.Data;

namespace Ebba.Picking.Api.Infrastructure;

public interface IDbConnectionFactory
{
    IDbConnection Create();
}

public sealed class PickingOptions
{
    /// <summary>Site-specific stored procedure used to resolve storage bins for a wagon.</summary>
    public string StorageBinProcedure { get; set; } = string.Empty;
}
