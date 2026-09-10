-- Sanitised reproduction of AF_Pick_GetProductImageURL: URL of the product image document of a given document class.
CREATE OR ALTER FUNCTION [dbo].[AF_Pick_GetProductImageURL]
(
    @ProductID      INT,
    @DocumentClass  NVARCHAR(50)
)
RETURNS NVARCHAR(1000)
AS
BEGIN
    DECLARE @Url NVARCHAR(1000);

    SELECT TOP (1) @Url = D.Url
    FROM PRODUCT_DOCUMENT PD
    INNER JOIN DOCUMENT D             ON D.DocumentID = PD.DocumentID
    INNER JOIN DOCUMENT_CLASS DC      ON DC.DocumentClassID = D.DocumentClassID
    WHERE PD.ProductID = @ProductID
      AND DC.Name = @DocumentClass
    ORDER BY D.ModifiedOn DESC;

    RETURN @Url;
END
