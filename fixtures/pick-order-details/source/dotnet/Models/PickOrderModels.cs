// Sanitised reproduction of the EBBA picking DTOs. Property names match the production contract so that the
// serialized JSON (camelCase) is identical to what Web Visual Picking receives.
namespace Ebba.Picking.Api.Models;

/// <summary>Request context sent by Web VP (facility/pick group come from the authenticated user context).</summary>
public sealed class StackWagonModel
{
    public string Facility { get; set; } = string.Empty;

    public string PickGroup { get; set; } = string.Empty;

    public string? OrderNo { get; set; }

    public int LanguageId { get; set; }
}

/// <summary>Header row returned by AP_Wrapper_Pick_GetPickOrderDetail.</summary>
public sealed class PickOrderHeader
{
    public string PickOrderNo { get; set; } = string.Empty;

    public string PickGroup { get; set; } = string.Empty;

    public string? Status { get; set; }

    public bool IsMultiPickOrder { get; set; }

    public int TotalMaterials { get; set; }

    public DateTime? CreatedOn { get; set; }
}

/// <summary>Response root for the Pick Order Details page.</summary>
public sealed class CWPPickOrderModel
{
    public string PickOrderNo { get; set; } = string.Empty;

    public string PickGroup { get; set; } = string.Empty;

    public string? Status { get; set; }

    public bool IsMultiPickOrder { get; set; }

    public int TotalMaterials { get; set; }

    public List<CWPPickOrderRow> PickOrderRows { get; set; } = new();
}

/// <summary>One material line; base fields come from AP_Pick_GetPickOrderRows, nested objects are enriched in code.</summary>
public sealed class CWPPickOrderRow
{
    public string MaterialNumber { get; set; } = string.Empty;

    public string? MaterialDescription { get; set; }

    public string? MaterialImageUrl { get; set; }

    public decimal PickQuantity { get; set; }

    public decimal AvailableQuantity { get; set; }

    public string? Unit { get; set; }

    public CWPPickStorageBin? PickStorageBin { get; set; }

    public List<CWPDestinationWagon> DestinationWagon { get; set; } = new();
}

/// <summary>Result of AP_Pick_GetPickStorageBin.</summary>
public sealed class CWPPickStorageBin
{
    public string? StorageBin { get; set; }

    public decimal OnHandQuantity { get; set; }

    public decimal AllocatedQuantity { get; set; }
}

/// <summary>Result of AP_Pick_GetDestinationWagon.</summary>
public sealed class CWPDestinationWagon
{
    public string PhysicalWagonId { get; set; } = string.Empty;

    public int NumberOfBoxes { get; set; }

    public string? PickGroupId { get; set; }

    public List<CWPStorageBin> StorageBin { get; set; } = new();
}

/// <summary>Storage bin of a destination wagon (source procedure pending).</summary>
public sealed class CWPStorageBin
{
    public string? Location { get; set; }

    public decimal Quantity { get; set; }
}
