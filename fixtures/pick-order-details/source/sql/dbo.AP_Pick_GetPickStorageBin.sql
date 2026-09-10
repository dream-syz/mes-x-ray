-- Sanitised reproduction of AP_Pick_GetPickStorageBin: storage bins and quantities for one material of a pick order.
-- Key behaviours preserved from production:
--   * Distinct warehouse locations of the material's inventory (child containers only, ChildContainerCalssID).
--   * WL_Description_Replacement swaps the raw Location for its translated description.
--   * FIFO ordering by the earliest inventory timestamp, then STRING_AGG into one comma separated StorageBin.
--   * OnHandQuantity / AllocatedQuantity are aggregates of the print queue detail - different semantics from AvailableQuantity.
CREATE OR ALTER PROCEDURE [dbo].[AP_Pick_GetPickStorageBin]
    @Facility        NVARCHAR(50),
    @PickOrderNo     NVARCHAR(50),
    @MaterialNumber  NVARCHAR(50),
    @LanguageId      INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ChildContainerClassID       INT = dbo.AF_GetSystemParameterValue('ChildContainerCalssID', @Facility);
    DECLARE @WL_Description_Replacement  BIT = dbo.AF_GetSystemParameterValue('WL_Description_Replacement', @Facility);

    CREATE TABLE #LocationDistinct
    (
        WarehouseLocationID INT,
        StorageBin          NVARCHAR(200),
        FirstInventoryOn    DATETIME
    );

    INSERT INTO #LocationDistinct (WarehouseLocationID, StorageBin, FirstInventoryOn)
    SELECT
        WL.WarehouseLocationID,
        CASE WHEN @WL_Description_Replacement = 1
             THEN dbo.AF_GetTextTranslation(WL.TextID, @LanguageId, 'Medium')
             ELSE WL.Location
        END                    AS StorageBin,
        MIN(I.CreatedOn)       AS FirstInventoryOn
    FROM INVENTORY2 I
    INNER JOIN CONTAINER C             ON C.ContainerID = I.ContainerID AND C.ContainerClassID = @ChildContainerClassID
    INNER JOIN WAREHOUSE_LOCATION WL   ON WL.WarehouseLocationID = C.WarehouseLocationID
    INNER JOIN AT_PICK_PRODUCT APP     ON APP.ProductID = I.ProductID AND APP.Facility = @Facility
    INNER JOIN PRODUCT P               ON P.ProductID = I.ProductID
    WHERE P.ProductNo = @MaterialNumber
    GROUP BY WL.WarehouseLocationID, WL.Location, WL.TextID;

    -- FIFO: earliest inventory first.
    SELECT
        LD.StorageBin,
        LD.FirstInventoryOn,
        ROW_NUMBER() OVER (ORDER BY LD.FirstInventoryOn) AS FifoRank
    INTO #LocationList
    FROM #LocationDistinct LD;

    SELECT
        Bins.StorageBin                                          AS StorageBin,
        SUM(APPQD.PickedQuantity)                                AS OnHandQuantity,
        SUM(APPQD.Quantity - ISNULL(APPQD.PickedQuantity, 0))    AS AllocatedQuantity
    FROM AT_PICK_PRINT_QUEUE APPQ
    INNER JOIN AT_PICK_PRINT_QUEUE_DETAIL APPQD ON APPQD.PickPrintQueueID = APPQ.PickPrintQueueID
    INNER JOIN PRODUCT P                        ON P.ProductID = APPQD.ProductID
    OUTER APPLY
    (
        SELECT STRING_AGG(LL.StorageBin, ',') WITHIN GROUP (ORDER BY LL.FifoRank) AS StorageBin
        FROM #LocationList LL
    ) AS Bins
    WHERE APPQ.Facility    = @Facility
      AND APPQ.PickOrderNo = @PickOrderNo
      AND P.ProductNo      = @MaterialNumber
    GROUP BY Bins.StorageBin;

    DROP TABLE #LocationList;
    DROP TABLE #LocationDistinct;
END
