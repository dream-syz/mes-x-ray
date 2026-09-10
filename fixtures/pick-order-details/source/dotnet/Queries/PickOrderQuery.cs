// Sanitised reproduction of the EBBA data-access ("Query") layer: Dapper calls to stored procedures.
using System.Data;
using Dapper;
using Ebba.Picking.Api.Infrastructure;
using Ebba.Picking.Api.Models;

namespace Ebba.Picking.Api.Queries;

public interface IPickOrderQuery
{
    Task<List<PickOrderHeader>> GetPickOrderHeaders(string facility, string pickGroup, string? orderNo, int languageId, CancellationToken cancellationToken);

    Task<List<CWPPickOrderRow>> GetPickOrderRows(string facility, string pickOrderNo, int languageId, CancellationToken cancellationToken);

    Task<List<CWPPickOrderRow>> GetMultiPickOrderRows(string facility, string pickOrderNo, int languageId, CancellationToken cancellationToken);

    Task<CWPPickStorageBin?> GetPickStorageBin(string facility, string pickOrderNo, string materialNumber, int languageId, CancellationToken cancellationToken);

    Task<List<CWPDestinationWagon>> GetDestinationWagon(string facility, string pickOrderNo, string materialNumber, CancellationToken cancellationToken);

    Task<List<CWPStorageBin>> GetStorageBin(string facility, string? pickGroupId, CancellationToken cancellationToken);
}

public sealed class PickOrderQuery : IPickOrderQuery
{
    private readonly IDbConnectionFactory _connections;
    private readonly PickingOptions _options;

    public PickOrderQuery(IDbConnectionFactory connections, PickingOptions options)
    {
        _connections = connections;
        _options = options;
    }

    public async Task<List<PickOrderHeader>> GetPickOrderHeaders(string facility, string pickGroup, string? orderNo, int languageId, CancellationToken cancellationToken)
    {
        using var connection = _connections.Create();
        var rows = await connection.QueryAsync<PickOrderHeader>(
            "[dbo].[AP_Wrapper_Pick_GetPickOrderDetail]",
            new { Facility = facility, PickGroup = pickGroup, OrderNo = orderNo, LanguageId = languageId },
            commandType: CommandType.StoredProcedure);
        return rows.ToList();
    }

    public async Task<List<CWPPickOrderRow>> GetPickOrderRows(string facility, string pickOrderNo, int languageId, CancellationToken cancellationToken)
    {
        using var connection = _connections.Create();
        var rows = await connection.QueryAsync<CWPPickOrderRow>(
            "[dbo].[AP_Pick_GetPickOrderRows]",
            new { Facility = facility, PickOrderNo = pickOrderNo, LanguageId = languageId },
            commandType: CommandType.StoredProcedure);
        return rows.ToList();
    }

    public async Task<List<CWPPickOrderRow>> GetMultiPickOrderRows(string facility, string pickOrderNo, int languageId, CancellationToken cancellationToken)
    {
        using var connection = _connections.Create();
        var rows = await connection.QueryAsync<CWPPickOrderRow>(
            "[dbo].[AP_Pick_GetMultiPickOrderRows]",
            new { Facility = facility, PickOrderNo = pickOrderNo, LanguageId = languageId },
            commandType: CommandType.StoredProcedure);
        return rows.ToList();
    }

    public async Task<CWPPickStorageBin?> GetPickStorageBin(string facility, string pickOrderNo, string materialNumber, int languageId, CancellationToken cancellationToken)
    {
        using var connection = _connections.Create();
        return await connection.QueryFirstOrDefaultAsync<CWPPickStorageBin>(
            "[dbo].[AP_Pick_GetPickStorageBin]",
            new { Facility = facility, PickOrderNo = pickOrderNo, MaterialNumber = materialNumber, LanguageId = languageId },
            commandType: CommandType.StoredProcedure);
    }

    public async Task<List<CWPDestinationWagon>> GetDestinationWagon(string facility, string pickOrderNo, string materialNumber, CancellationToken cancellationToken)
    {
        using var connection = _connections.Create();
        var rows = await connection.QueryAsync<CWPDestinationWagon>(
            "[dbo].[AP_Pick_GetDestinationWagon]",
            new { Facility = facility, PickOrderNo = pickOrderNo, MaterialNumber = materialNumber },
            commandType: CommandType.StoredProcedure);
        return rows.ToList();
    }

    public async Task<List<CWPStorageBin>> GetStorageBin(string facility, string? pickGroupId, CancellationToken cancellationToken)
    {
        // The procedure name is configured per site in the legacy system, so it cannot be resolved statically.
        using var connection = _connections.Create();
        var rows = await connection.QueryAsync<CWPStorageBin>(
            _options.StorageBinProcedure,
            new { Facility = facility, PickGroupId = pickGroupId },
            commandType: CommandType.StoredProcedure);
        return rows.ToList();
    }
}
