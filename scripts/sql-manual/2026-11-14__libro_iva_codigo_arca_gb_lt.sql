/*
  Libro IVA Compras: código ARCA de GB (gastos bancarios) y LT (liquidación de tarjetas).

  Problema: GB y LT salen con código 99 ("otros comprobantes que no cumplen RG 1415"), que no discrimina IVA.
  Sus asientos traen IVA discriminado, así que el TXT de ARCA se rechaza con
  «El comprobante no debe discriminar IVA». Corresponde 39 = "Otros comprobantes A que cumplen con RG 1415".

  Qué hace: pone V_TA_CPTE.CpteDGIX = '39' para GB y LT (letra X, la de los comprobantes contables).
  Los asientos tienen CodCpteDGI vacío y el libro toma el código de esta tabla, así que corrige también los meses ya cargados.

  Ojo: es un dato de configuración de ESTA base. También cambia lo que exporta el legacy (hoy 99 → 39).
  NO es un script de actualización automática (App_Data/updates): se corre a mano, base por base, después de revisar.

  Uso:
    1) Correr tal cual: solo muestra cómo está hoy (@Aplicar = 0).
    2) Si está bien, cambiar @Aplicar a 1 y volver a correr.
  sqlcmd: usar  -f i:65001  (UTF-8).
  Idempotente: correrlo de nuevo no cambia nada.
*/
SET NOCOUNT ON;
DECLARE @Aplicar bit = 0;          -- 0 = solo mostrar, 1 = aplicar
DECLARE @Codigo nvarchar(2) = N'39';

SELECT 'ANTES' AS Momento, LTRIM(RTRIM(CODIGO)) AS Comprobante, LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) AS Descripcion,
       LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) AS Unidad, CpteDGIX AS CodigoArcaX
FROM dbo.V_TA_CPTE
WHERE UPPER(LTRIM(RTRIM(CODIGO))) IN (N'GB', N'LT')
ORDER BY CODIGO, UNEGOCIO;

IF @Aplicar = 1
BEGIN
    BEGIN TRAN;
    UPDATE dbo.V_TA_CPTE SET CpteDGIX = @Codigo
    WHERE UPPER(LTRIM(RTRIM(CODIGO))) IN (N'GB', N'LT') AND ISNULL(LTRIM(RTRIM(CpteDGIX)), N'') <> @Codigo;
    PRINT CONCAT(N'Filas actualizadas: ', @@ROWCOUNT);
    COMMIT;

    SELECT 'DESPUES' AS Momento, LTRIM(RTRIM(CODIGO)) AS Comprobante, LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) AS Unidad, CpteDGIX AS CodigoArcaX
    FROM dbo.V_TA_CPTE
    WHERE UPPER(LTRIM(RTRIM(CODIGO))) IN (N'GB', N'LT')
    ORDER BY CODIGO, UNEGOCIO;
END
ELSE PRINT N'Solo vista previa. Para aplicar: @Aplicar = 1.';
