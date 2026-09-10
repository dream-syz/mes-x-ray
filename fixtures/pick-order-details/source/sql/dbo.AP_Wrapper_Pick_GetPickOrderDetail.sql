-- Sanitised, simplified reproduction of AP_Wrapper_Pick_GetPickOrderDetail: pick order header rows.
-- Full header lineage is a P1 input; this version covers the fields the POC needs (IsMultiPickOrder, TotalMaterials).
CREATE OR ALTER PROCEDURE [dbo].[AP_Wrapper_Pick_GetPickOrderDetail]
    @Facility    NVARCHAR(50),
    @PickGroup   NVARCHAR(50),
    @OrderNo     NVARCHAR(50) = NULL,
    @LanguageId  INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        APPQ.PickOrderNo                                                        AS PickOrderNo,
        APPQ.PickGroup                                                          AS PickGroup,
        APPQ.Status                                                             AS Status,
        CASE WHEN COUNT(DISTINCT APPQ.SourceOrderNo) > 1 THEN 1 ELSE 0 END      AS IsMultiPickOrder,
        COUNT(DISTINCT APPQD.ProductID)                                         AS TotalMaterials,
        MIN(APPQ.CreatedOn)                                                     AS CreatedOn
    FROM AT_PICK_PRINT_QUEUE APPQ
    INNER JOIN AT_PICK_PRINT_QUEUE_DETAIL APPQD ON APPQD.PickPrintQueueID = APPQ.PickPrintQueueID
    WHERE APPQ.Facility  = @Facility
      AND APPQ.PickGroup = @PickGroup
      AND (@OrderNo IS NULL OR APPQ.PickOrderNo = @OrderNo)
    GROUP BY APPQ.PickOrderNo, APPQ.PickGroup, APPQ.Status;
END
