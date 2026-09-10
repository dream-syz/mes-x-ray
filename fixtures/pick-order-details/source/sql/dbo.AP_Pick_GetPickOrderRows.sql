-- Sanitised reproduction of AP_Pick_GetPickOrderRows: base material rows for a pick order.
-- Key behaviours preserved from production:
--   * WMS_Enabled decides how AvailableQuantity is computed (CASE -> UDF or SUM * 10).
--   * PickOrder_Type filters the print queue.
--   * PickDocumentClassForProductImage drives the product image URL.
--   * Per-location results (MainResults) are aggregated per material (MaterialTotals).
CREATE OR ALTER PROCEDURE [dbo].[AP_Pick_GetPickOrderRows]
    @Facility     NVARCHAR(50),
    @PickOrderNo  NVARCHAR(50),
    @LanguageId   INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @WMS_Enabled    BIT          = dbo.AF_GetSystemParameterValue('WMS_Enabled', @Facility);
    DECLARE @PickOrder_Type NVARCHAR(50) = dbo.AF_GetSystemParameterValue('PickOrder_Type', @Facility);
    DECLARE @ImageDocClass  NVARCHAR(50) = dbo.AF_GetSystemParameterValue('PickDocumentClassForProductImage', @Facility);

    ;WITH MainResults AS
    (
        SELECT
            APP.ProductID,
            APP.WarehouseLocationID,
            SUM(APPQD.Quantity) AS PickQuantity,
            CASE @WMS_Enabled
                WHEN 0 THEN ISNULL(SUM(APPQD.Quantity) * 10, 1000)
                WHEN 1 THEN ISNULL(dbo.AF_Pick_GetAvailableQuantity(@Facility, APP.WarehouseLocationID, APP.ProductID), 0)
            END AS AvailableQuantity
        FROM AT_PICK_PRINT_QUEUE APPQ
        INNER JOIN AT_PICK_PRINT_QUEUE_DETAIL APPQD ON APPQD.PickPrintQueueID = APPQ.PickPrintQueueID
        INNER JOIN AT_PICK_PRODUCT APP            ON APP.ProductID = APPQD.ProductID AND APP.Facility = APPQ.Facility
        INNER JOIN WAREHOUSE_LOCATION WL          ON WL.WarehouseLocationID = APP.WarehouseLocationID
        LEFT  JOIN REPLENISH_STRATEGY RS          ON RS.ReplenishStrategyID = APP.ReplenishStrategyID
        LEFT  JOIN CONTENT CT                     ON CT.ProductID = APP.ProductID AND CT.WarehouseLocationID = APP.WarehouseLocationID
        WHERE APPQ.Facility      = @Facility
          AND APPQ.PickOrderNo   = @PickOrderNo
          AND APPQ.PickOrderType = @PickOrder_Type
        GROUP BY APP.ProductID, APP.WarehouseLocationID
    ),
    MaterialTotals AS
    (
        SELECT
            MR.ProductID,
            SUM(MR.PickQuantity)      AS TotalPickQuantity,
            SUM(MR.AvailableQuantity) AS TotalAvailableQuantity
        FROM MainResults MR
        GROUP BY MR.ProductID
    )
    SELECT
        P.ProductNo                                                   AS MaterialNumber,
        dbo.AF_GetTextTranslation(P.TextID, @LanguageId, 'Medium')    AS MaterialDescription,
        dbo.AF_Pick_GetProductImageURL(P.ProductID, @ImageDocClass)   AS MaterialImageUrl,
        MT.TotalPickQuantity                                          AS PickQuantity,
        MT.TotalAvailableQuantity                                     AS AvailableQuantity,
        PA.AliasUnit                                                  AS Unit
    FROM MaterialTotals MT
    INNER JOIN PRODUCT P        ON P.ProductID = MT.ProductID
    LEFT  JOIN PRODUCT_ALIAS PA ON PA.ProductID = P.ProductID AND PA.IsDefault = 1
    ORDER BY P.ProductNo;
END
