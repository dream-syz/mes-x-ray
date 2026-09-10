// Sanitised reproduction of the EBBA pick order service. Business flow:
// GetPickOrderDetails -> header SP -> foreach PickOrder -> IsMultiPickOrder? -> GetPickOrderRows -> enrich rows.
using Ebba.Picking.Api.Models;
using Ebba.Picking.Api.Queries;

namespace Ebba.Picking.Api.Services;

public interface IPickOrderService
{
    Task<List<CWPPickOrderModel>> GetPickOrderDetails(StackWagonModel request, CancellationToken cancellationToken);
}

public sealed class PickOrderService : IPickOrderService
{
    private readonly IPickOrderQuery _query;

    public PickOrderService(IPickOrderQuery query)
    {
        _query = query;
    }

    public async Task<List<CWPPickOrderModel>> GetPickOrderDetails(StackWagonModel request, CancellationToken cancellationToken)
    {
        var languageId = request.LanguageId;
        var headers = await _query.GetPickOrderHeaders(request.Facility, request.PickGroup, request.OrderNo, languageId, cancellationToken);

        var result = new List<CWPPickOrderModel>();
        foreach (var pickOrder in headers)
        {
            var model = MapHeader(pickOrder);

            if (pickOrder.IsMultiPickOrder)
            {
                model.PickOrderRows = await GetMultiPickOrderRows(pickOrder, request, cancellationToken);
            }
            else
            {
                model.PickOrderRows = await GetPickOrderRows(pickOrder, request, cancellationToken);
            }

            result.Add(model);
        }

        return result;
    }

    public async Task<List<CWPPickOrderRow>> GetPickOrderRows(PickOrderHeader pickOrder, StackWagonModel request, CancellationToken cancellationToken)
    {
        var rows = await _query.GetPickOrderRows(request.Facility, pickOrder.PickOrderNo, request.LanguageId, cancellationToken);

        foreach (var row in rows)
        {
            row.PickStorageBin = await GetPickStorageBin(pickOrder, row, request, cancellationToken);
            row.DestinationWagon = await GetDestinationWagon(pickOrder, row, request, cancellationToken);
        }

        return rows;
    }

    public async Task<List<CWPPickOrderRow>> GetMultiPickOrderRows(PickOrderHeader pickOrder, StackWagonModel request, CancellationToken cancellationToken)
    {
        // Multi pick order flow is out of scope for the first X-Ray iteration.
        var rows = await _query.GetMultiPickOrderRows(request.Facility, pickOrder.PickOrderNo, request.LanguageId, cancellationToken);
        return rows;
    }

    public async Task<CWPPickStorageBin?> GetPickStorageBin(PickOrderHeader pickOrder, CWPPickOrderRow row, StackWagonModel request, CancellationToken cancellationToken)
    {
        var storageBin = await _query.GetPickStorageBin(request.Facility, pickOrder.PickOrderNo, row.MaterialNumber, request.LanguageId, cancellationToken);
        return storageBin;
    }

    public async Task<List<CWPDestinationWagon>> GetDestinationWagon(PickOrderHeader pickOrder, CWPPickOrderRow row, StackWagonModel request, CancellationToken cancellationToken)
    {
        var wagons = await _query.GetDestinationWagon(request.Facility, pickOrder.PickOrderNo, row.MaterialNumber, cancellationToken);

        foreach (var wagon in wagons)
        {
            wagon.StorageBin = await GetStorageBin(wagon, request, cancellationToken);
        }

        return wagons;
    }

    public async Task<List<CWPStorageBin>> GetStorageBin(CWPDestinationWagon wagon, StackWagonModel request, CancellationToken cancellationToken)
    {
        var bins = await _query.GetStorageBin(request.Facility, wagon.PickGroupId, cancellationToken);
        return bins;
    }

    private static CWPPickOrderModel MapHeader(PickOrderHeader pickOrder)
    {
        var model = new CWPPickOrderModel();
        model.PickOrderNo = pickOrder.PickOrderNo;
        model.PickGroup = pickOrder.PickGroup;
        model.Status = pickOrder.Status;
        model.IsMultiPickOrder = pickOrder.IsMultiPickOrder;
        model.TotalMaterials = pickOrder.TotalMaterials;
        return model;
    }
}
