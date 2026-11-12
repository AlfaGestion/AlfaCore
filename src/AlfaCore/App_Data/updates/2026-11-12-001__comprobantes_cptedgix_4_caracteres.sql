/* Comprobantes: los codigos de comprobante ARCA por letra (CpteDGIA, CpteDGIB, CpteDGIC, CpteDGIE, CpteDGIM, CpteDGIR,
   CpteDGIX) pasan de nvarchar(2) a nvarchar(4) en V_TA_Cpte y TA_COMPROBANTES.
   El legacy los crea como nvarchar(2) (FrmUpdate / VerCampoDB, que solo agrega columnas y nunca cambia el tipo), y los
   codigos ARCA de 3 digitos (ej. ' 039') no entraban: 2026-11-11-008 fallaba con "Los datos de cadena o binarios se truncarian".
   - Amplia cada columna solo si es mas chica (no la achica nunca). Conserva NULL.
   - Completa ' 039' en el comprobante GB (Gasto bancario, letra X) si quedo sin codigo.
   Idempotente. Compatible con compatibility level 80/100. */
SET NOCOUNT ON;

DECLARE @tabla sysname, @columna sysname, @sql nvarchar(400);
DECLARE crsDGI CURSOR LOCAL FAST_FORWARD FOR
    SELECT t.name, c.name
      FROM sys.columns c
      JOIN sys.tables t ON t.object_id = c.object_id
     WHERE t.name IN (N'V_TA_Cpte', N'TA_COMPROBANTES')
       AND c.name IN (N'CpteDGIA', N'CpteDGIB', N'CpteDGIC', N'CpteDGIE', N'CpteDGIM', N'CpteDGIR', N'CpteDGIX')
       AND c.max_length > 0 AND c.max_length < 8;
OPEN crsDGI;
FETCH NEXT FROM crsDGI INTO @tabla, @columna;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'ALTER TABLE dbo.' + QUOTENAME(@tabla) + N' ALTER COLUMN ' + QUOTENAME(@columna) + N' nvarchar(4) NULL;';
    EXEC (@sql);
    FETCH NEXT FROM crsDGI INTO @tabla, @columna;
END
CLOSE crsDGI;
DEALLOCATE crsDGI;
GO

IF COL_LENGTH('dbo.V_TA_Cpte','CpteDGIX') >= 8
    UPDATE dbo.V_TA_Cpte SET CpteDGIX = N' 039'
     WHERE UPPER(LTRIM(RTRIM(CODIGO))) = N'GB' AND LTRIM(RTRIM(ISNULL(CpteDGIX,N''))) = N'';

IF COL_LENGTH('dbo.TA_COMPROBANTES','CpteDGIX') >= 8
    UPDATE dbo.TA_COMPROBANTES SET CpteDGIX = N' 039'
     WHERE UPPER(LTRIM(RTRIM(CODIGO))) = N'GB' AND LTRIM(RTRIM(ISNULL(CpteDGIX,N''))) = N'';
