// Sanitised reproduction of the EBBA picking endpoint used by Web Visual Picking.
// No real hosts, credentials or customer data. Structure and naming follow the production code.
using Ebba.Picking.Api.Models;
using Ebba.Picking.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ebba.Picking.Api.Controllers;

[ApiController]
[Route("cwp/v1/picking")]
public sealed class PickingController : ControllerBase
{
    private readonly IPickOrderService _pickOrderService;

    public PickingController(IPickOrderService pickOrderService)
    {
        _pickOrderService = pickOrderService;
    }

    /// <summary>Pick order details for the Web Visual Picking "Pick Order Details" page.</summary>
    [HttpGet("pickOrder")]
    [ProducesResponseType(typeof(List<CWPPickOrderModel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CWPPickOrderModel>>> GetPickOrder([FromQuery] StackWagonModel request, CancellationToken cancellationToken)
    {
        var result = await _pickOrderService.GetPickOrderDetails(request, cancellationToken);
        return Ok(result);
    }
}
