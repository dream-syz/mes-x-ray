-- Sanitised reproduction of AP_Pick_GetDestinationWagon: destination wagons (pick groups) for one material.
-- PhysicalWagonId is currently a fixed empty string in production; NumberOfBoxes comes from AT_PICK_GROUP.NoOfBins.
CREATE OR ALTER PROCEDURE [dbo].[AP_Pick_GetDestinationWagon]
    @Facility        NVARCHAR(50),
    @PickOrderNo     NVARCHAR(50),
    @MaterialNumber  NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        ''                 AS PhysicalWagonId,
        APG.NoOfBins       AS NumberOfBoxes,
        APG.PickGroupID    AS PickGroupId
    FROM AT_PICK_GROUP APG
    INNER JOIN AT_PICK_PRINT_QUEUE APPQ         ON APPQ.PickGroupID = APG.PickGroupID
    INNER JOIN AT_PICK_PRINT_QUEUE_DETAIL APPQD ON APPQD.PickPrintQueueID = APPQ.PickPrintQueueID
    INNER JOIN PRODUCT P                        ON P.ProductID = APPQD.ProductID
    WHERE APPQ.Facility    = @Facility
      AND APPQ.PickOrderNo = @PickOrderNo
      AND P.ProductNo      = @MaterialNumber
    GROUP BY APG.PickGroupID, APG.NoOfBins;
END
