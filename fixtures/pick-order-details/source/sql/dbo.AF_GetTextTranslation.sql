-- Sanitised reproduction of AF_GetTextTranslation: translated text for a TextID in a language, falling back to default.
CREATE OR ALTER FUNCTION [dbo].[AF_GetTextTranslation]
(
    @TextID      INT,
    @LanguageId  INT,
    @Length      NVARCHAR(20)
)
RETURNS NVARCHAR(500)
AS
BEGIN
    DECLARE @Result NVARCHAR(500);

    SELECT TOP (1) @Result = CASE @Length
                                 WHEN 'Short'  THEN TT.ShortText
                                 WHEN 'Medium' THEN TT.MediumText
                                 ELSE TT.LongText
                             END
    FROM TEXT_TRANSLATION TT
    WHERE TT.TextID = @TextID
      AND TT.LanguageID = @LanguageId;

    IF @Result IS NULL
    BEGIN
        SELECT TOP (1) @Result = T.DefaultText
        FROM TEXT T
        WHERE T.TextID = @TextID;
    END

    RETURN @Result;
END
