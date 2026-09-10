-- Sanitised reproduction of AF_GetSystemParameterValue: reads a facility-scoped system parameter, falling back to
-- the global value. Every call site of this function is treated by the SQL scanner as a SystemParameter usage.
CREATE OR ALTER FUNCTION [dbo].[AF_GetSystemParameterValue]
(
    @Name      NVARCHAR(100),
    @Facility  NVARCHAR(50)
)
RETURNS NVARCHAR(500)
AS
BEGIN
    DECLARE @Value NVARCHAR(500);

    SELECT TOP (1) @Value = SP.Value
    FROM SYSTEM_PARAMETER SP
    WHERE SP.Name = @Name
      AND (SP.Facility = @Facility OR SP.Facility IS NULL)
    ORDER BY CASE WHEN SP.Facility = @Facility THEN 0 ELSE 1 END;

    RETURN @Value;
END
