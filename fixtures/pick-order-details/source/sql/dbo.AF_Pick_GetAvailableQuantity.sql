-- Sanitised copy of AF_Pick_GetAvailableQuantity (the P0 input of design §16, provided 2026-09-12).
-- The body is kept as written in production (ALTER FUNCTION -> CREATE OR ALTER for the fixture); only credentials
-- and hosts would have been removed, and this object contains none. Key behaviours the scanner must reproduce:
--   * Pick_UseParentLocation and ChildContainerCalssID are read through typed system parameter functions.
--   * LVP delivery method: the function returns the constant 1000 regardless of inventory.
--   * LVS_MPA / SLFR_MPA / LVS_LINE / SLFR_LINE delivery methods (and the ECU product group) count QuantityAllocated
--     instead of QuantityOnHand.
--   * Inventory of the parent warehouse location is added when Pick_UseParentLocation = 1; child containers
--     (ChildContainerCalssID) are excluded; only InventoryStatus = 1 rows count.
--   * The result is ISNULL(SUM(QuantityOnHand), 0) over the InventoryData CTE.
CREATE OR ALTER FUNCTION [dbo].[AF_Pick_GetAvailableQuantity]
(
    @Facility VARCHAR(20),
    @WarehouseLocationID INT,
    @ProductID INT
)
RETURNS INT
AS
BEGIN
    -- System parameters
    DECLARE @UseParentLocation BIT = (
        SELECT *
        FROM [dbo].[AF_GetSystemParameterValueListString]('Pick_UseParentLocation')[bit]
    );

    DECLARE @ChildContainerCalssID INT = (
        SELECT [dbo].[AF_GetSystemParameterValueint]('ChildContainerCalssID')
    );

    DECLARE @Result INT = 0;
    DECLARE @UseQuantityAllocated BIT = 0;

    -------------------------------------------------------------------------
    -- 1. Check if the product delivery method is LVP
    --    If true, always return 1000 regardless of inventory.
    -------------------------------------------------------------------------
    IF EXISTS (
        SELECT 1
        FROM DET2_ILG_ProductDeliveryMethod DIP
        WHERE DIP.Product = (SELECT ProductNo FROM PRODUCT WHERE ID = @ProductID)
          AND DIP.DeliveryMethod = 'LVP'
          AND Facility = @Facility
          AND (
                (DIP.ValidFrom IS NULL OR GETDATE() >= DIP.ValidFrom)
             AND (DIP.ValidTo   IS NULL OR GETDATE() <= DIP.ValidTo)
              )
    )
    BEGIN
        RETURN 1000;
    END;

    -------------------------------------------------------------------------
    -- 2. Check if the product has LVS_MPA/SLFR_MPA/LVS_LINE/SLFR_LINE delivery methods
    --    If true, use QuantityAllocated instead of QuantityOnHand.
    -------------------------------------------------------------------------
    IF EXISTS (
        SELECT 1
        FROM DET2_ILG_ProductDeliveryMethod DIP
        WHERE DIP.Product = (SELECT ProductNo FROM PRODUCT WHERE ID = @ProductID)
          AND DIP.DeliveryMethod IN ('LVS_MPA','SLFR_MPA','LVS_LINE','SLFR_LINE')
          AND Facility = @Facility
          AND (
                (DIP.ValidFrom IS NULL OR GETDATE() >= DIP.ValidFrom)
             AND (DIP.ValidTo   IS NULL OR GETDATE() <= DIP.ValidTo)
              )
    )
    BEGIN
        SET @UseQuantityAllocated = 1;
    END;

    -------------------------------------------------------------------------
    -- 3. Calculate available quantity with CTE
    -------------------------------------------------------------------------
    ;WITH InventoryData AS (
        ---------------------------------------------------------------------
        -- Inventory at the specified warehouse location
        ---------------------------------------------------------------------
        SELECT
            CASE
                WHEN @UseQuantityAllocated = 1 THEN ISNULL(I.QuantityAllocated, 0)
                WHEN PG.Group_ = 'ECU' THEN ISNULL(I.QuantityAllocated, 0)
                ELSE ISNULL(I.QuantityOnHand, 0)
            END AS QuantityOnHand
        FROM INVENTORY2 I
        INNER JOIN PRODUCT_GROUP PG
                ON I.ProductID = PG.ProductID AND PG.GroupType = 2
        LEFT JOIN CONTAINER C
               ON C.Container = I.Container
        WHERE I.ProductID = @ProductID
          AND I.WarehouseLocationID = @WarehouseLocationID
          AND I.InventoryStatus = 1
          AND (
                   (PG.Group_ = 'ECU' AND ISNULL(I.QuantityAllocated, 0) > 0)
               OR  (PG.Group_ <> 'ECU' AND (
                        (@UseQuantityAllocated = 1 AND ISNULL(I.QuantityAllocated, 0) > 0)
                    OR  (@UseQuantityAllocated = 0 AND ISNULL(I.QuantityOnHand, 0) > 0)
                    ))
              )
          AND ISNULL(C.ContainerClassID, 0) <> @ChildContainerCalssID

        UNION ALL

        ---------------------------------------------------------------------
        -- Inventory from parent warehouse location (if enabled)
        ---------------------------------------------------------------------
        SELECT
            CASE
                WHEN @UseQuantityAllocated = 1 THEN ISNULL(I.QuantityAllocated, 0)
                WHEN PG.Group_ = 'ECU' THEN ISNULL(I.QuantityAllocated, 0)
                ELSE ISNULL(I.QuantityOnHand, 0)
            END AS QuantityOnHand
        FROM INVENTORY2 I
        INNER JOIN WAREHOUSE_LOCATION_RELATION WLR
                ON I.WarehouseLocationID = WLR.ParentLocationID
        INNER JOIN INVENTORY2 I2
                ON I2.WarehouseLocationID = WLR.ChildLocationID
        INNER JOIN PRODUCT_GROUP PG
                ON I.ProductID = PG.ProductID AND PG.GroupType = 2
        LEFT JOIN CONTAINER C
               ON C.Container = I.Container
        WHERE @UseParentLocation = 1
          AND I.ProductID = I2.ProductID
          AND I2.ProductID = @ProductID
          AND WLR.ChildLocationID = @WarehouseLocationID
          AND I.InventoryStatus = 1
          AND (
                   (PG.Group_ = 'ECU' AND ISNULL(I.QuantityAllocated, 0) > 0)
               OR  (PG.Group_ <> 'ECU' AND (
                        (@UseQuantityAllocated = 1 AND ISNULL(I.QuantityAllocated, 0) > 0)
                    OR  (@UseQuantityAllocated = 0 AND ISNULL(I.QuantityOnHand, 0) > 0)
                    ))
              )
          AND ISNULL(C.ContainerClassID, 0) <> @ChildContainerCalssID
    )

    -------------------------------------------------------------------------
    -- 4. Sum all quantities
    -------------------------------------------------------------------------
    SELECT @Result = ISNULL(SUM(QuantityOnHand), 0)
    FROM InventoryData;

    RETURN @Result;
END
