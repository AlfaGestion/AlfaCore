/*
    sp_web_CpteInsumos -- desglose de IVA por alícuota (AlicIva/AlicIva2/AlicIVA3/AlicIVA4 +
    ImporteIva/ImporteIva2/ImpIVA3/ImpIVA4 en V_MV_Cpte) y corrección de un bug de cálculo.

    Motivo: FacturaDocumentService (recuadro de IVA del comprobante impreso) y la solicitud de CAE a
    ARCA necesitan el neto/IVA discriminado por alícuota -- hasta 4 tasas distintas por comprobante,
    ya contempladas en el esquema (AlicIva/AlicIva2/AlicIVA3/AlicIVA4) pero NUNCA completadas por esta
    rutina: solo acumulaba un total de IVA sin desglose.

    De paso se corrige un bug real encontrado al revisar la rutina: para TC distinto de 'FP'/'NP'
    (o sea, toda Factura de mostrador/POS), el importe con IVA se calculaba SIEMPRE como
    "NETO_GRAVADO * 1.21" -- 21% fijo, ignorando la alícuota real del artículo (@alicIva) y pisando el
    IVA=0 que ya se había calculado para artículos exentos. Esto hacía que cualquier venta de un
    artículo al 10.5%, 27%, 0% o exento en una Factura quedara facturada como si fuera 21%. Se
    reemplaza por el cálculo real: NETO_GRAVADO * (1 + @alicIva/100), sin tocar la rama FP/NP (que ya
    usaba el IVA correcto calculado más arriba en la rutina).

    También se corrigen dos bugs preexistentes del mismo patrón, encontrados al probar el primero
    contra un artículo real con TASAIVA=0 (IVA 0% legítimo, no exento):
    1. La propia rutina trataba "alícuota = 0" igual que "alícuota sin configurar" y la reemplazaba
       por el 21% por defecto (FN_OBTIENE_VALOR_CONFIGURACION 'PIVA').
    2. Las funciones FN_PRECIO_CON_IVA/FN_PRECIO_SIN_IVA (propias de este módulo, creadas en
       2026-06-26-001__ventas_punto_venta_funciones_iva.sql) tenían el mismo problema.
    Ambos hacían que cualquier artículo al 0% (o exento con TASAIVA=0, la combinación más común) se
    facturara como si tuviera 21% de IVA. Ahora el default solo se aplica cuando la alícuota es NULL
    de verdad.

    Idempotente (CREATE OR ALTER). No crea columnas nuevas: AlicIva2/AlicIVA3/AlicIVA4,
    ImporteIva2/ImpIVA3/ImpIVA4 ya existen en V_MV_Cpte (las lee FacturaDocumentService desde hace
    tiempo) -- esta rutina es la primera que las completa para ventas de POS.
*/

SET NOCOUNT ON;
GO

-- FN_PRECIO_CON_IVA/FN_PRECIO_SIN_IVA (creadas en 2026-06-26-001__ventas_punto_venta_funciones_iva.sql,
-- propias de este módulo, no legacy externo intocable) tenían el mismo bug: trataban alícuota=0 igual
-- que alícuota sin configurar y aplicaban 21% por defecto. Sin este fix, sp_web_CpteInsumos no puede
-- calcular bien un artículo al 0% real aunque ya lea su TASAIVA correctamente.
CREATE OR ALTER FUNCTION [dbo].[FN_PRECIO_CON_IVA] (@PRECIO_SIN_IVA MONEY, @ALIC_IVA FLOAT)
RETURNS MONEY AS
BEGIN
    DECLARE @EL_PRECIO_CON_IVA MONEY
    DECLARE @IVA_DEFAULT NVARCHAR(50)
    IF (@ALIC_IVA IS NULL)
        BEGIN
            SELECT @IVA_DEFAULT = VALOR FROM dbo.TA_CONFIGURACION WHERE CLAVE = 'PIVA'
            SET @ALIC_IVA = CONVERT(FLOAT, @IVA_DEFAULT)
        END
    SET @EL_PRECIO_CON_IVA = @PRECIO_SIN_IVA + (@ALIC_IVA * @PRECIO_SIN_IVA / 100)
    RETURN @EL_PRECIO_CON_IVA
END
GO

CREATE OR ALTER FUNCTION [dbo].[FN_PRECIO_SIN_IVA] (@PRECIO_CON_IVA MONEY, @ALIC_IVA FLOAT)
RETURNS MONEY AS
BEGIN
    DECLARE @EL_PRECIO_SIN_IVA MONEY
    DECLARE @IVA_DEFAULT NVARCHAR(50)
    IF (@ALIC_IVA IS NULL)
        BEGIN
            SELECT @IVA_DEFAULT = VALOR FROM dbo.TA_CONFIGURACION WHERE CLAVE = 'PIVA'
            SET @ALIC_IVA = CONVERT(FLOAT, @IVA_DEFAULT)
        END
    SET @EL_PRECIO_SIN_IVA = @PRECIO_CON_IVA / (1 + (@ALIC_IVA / 100))
    RETURN @EL_PRECIO_SIN_IVA
END
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_web_CpteInsumos]
    @pIdCpte                        int = null,
    @pIdArticulo                    nvarchar(25),
    @pCantidad                      float,
    @pImporteUnitario               money,
    @pPorcDescuento                 varchar(10),
    @pResultado                     smallint = NULL OUTPUT,
    @pMensaje                       varchar(255) = NULL OUTPUT,
    @pIdVMVCpteInsumosRES           int = NULL OUTPUT
AS
SET NOCOUNT ON
DECLARE @pPorcDescuentoNumero float
DECLARE @tc nvarchar(4)
DECLARE @idComprobante nvarchar(13)
DECLARE @idComplemento int
DECLARE @tmpConfigIVA nvarchar(2)
DECLARE @cfgMaestroArticuloCostoConIva nvarchar(2)
DECLARE @idlista nvarchar(4)
DECLARE @descripcion nvarchar(50)
DECLARE @idUnidad nvarchar(4)
DECLARE @importe money
DECLARE @idMoneda nvarchar(4)
DECLARE @total money
DECLARE @exento bit
DECLARE @clasePrecio nvarchar(4)
DECLARE @alicIva float
DECLARE @importeDto money
DECLARE @importeSinDto money
DECLARE @unidadBase nvarchar(4)
DECLARE @cantKG float
DECLARE @pedidosPreparados nvarchar(2)
DECLARE @tmpConfig nvarchar(50)
DECLARE @condicionIva nvarchar(4)
DECLARE @IdCliente nvarchar(15)
DECLARE @Costo float
DECLARE @totalInsumo money
DECLARE @cotiz float

IF @pPorcDescuento = 'NaN' SET @pPorcDescuento = '0'

SET @pPorcDescuentoNumero = CAST(@pPorcDescuento as float)
IF @pImporteUnitario IS NULL SET @pImporteUnitario = 0
SET @importeSinDto = @pImporteUnitario
SET @importeDto = 0
IF @pPorcDescuentoNumero > 0
BEGIN
    SET @pImporteUnitario = (@pImporteUnitario / (1 - (@pPorcDescuentoNumero / 100)))
    SET @importeDto = (@pImporteUnitario - @importeSinDto) * @pCantidad
END

SELECT @tc = TC,
       @idComprobante = IDCOMPROBANTE,
       @idComplemento = IDCOMPLEMENTO,
       @clasePrecio = CLASEPRECIO,
       @condicionIva = CONDICIONIVA,
       @IdCliente = CUENTA,
       @idlista = IdLista
FROM V_MV_CPTE
WHERE ID = @pIdCpte

SELECT @pedidosPreparados = VALOR FROM TA_CONFIGURACION WHERE CLAVE = 'V_SoloPedidosPreparados'

SET @pIdArticulo = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pIdArticulo)), 25)

SELECT @descripcion = DESCRIPCION,
       @unidadBase = IDUNIDAD,
       @idUnidad = UD_TTE,
       @alicIva = TASAIVA,
       @exento = EXENTO,
       @idMoneda = MONEDA,
       @Costo = Costo
FROM V_MA_ARTICULOS
WHERE LTRIM(IDARTICULO) = LTRIM(@pIdArticulo)

IF (@alicIva IS NULL)
BEGIN
    -- Bug corregido: antes también entraba acá cuando @alicIva = 0 (artículo con IVA 0% real,
    -- no exento -- ej. algunos productos de la canasta básica), tratando "0%" como "sin configurar"
    -- y facturándolo con el 21% por defecto. Ahora solo se aplica el default cuando la alícuota del
    -- artículo directamente no está cargada (NULL).
    SET @tmpConfig = dbo.FN_OBTIENE_VALOR_CONFIGURACION('PIVA', '21')
    SET @alicIva = CAST(@tmpConfig AS float)
END

IF (@condicionIva = NULL) OR (@condicionIva IS NULL) SET @condicionIva = '   1'
IF (@exento = NULL) OR (@exento IS NULL) SET @exento = 0

SET @tmpConfigIVA = dbo.FN_OBTIENE_VALOR_CONFIGURACION('MaestroArticuloConIVA', 'NO')
SET @cfgMaestroArticuloCostoConIva = dbo.FN_OBTIENE_VALOR_CONFIGURACION('MaestroArticuloCostoConIVA', 'NO')

IF @cfgMaestroArticuloCostoConIva = 'SI' AND (@exento = 0 or @exento = '0' OR @exento is null)
    SET @Costo = dbo.FN_PRECIO_SIN_IVA(@Costo, @alicIva)

IF LTRIM(@idlista) <> ''
BEGIN
    IF @tmpConfigIVA = 'SI'
        IF @exento = 0
        BEGIN
            SET @importe = @pImporteUnitario
            SET @pImporteUnitario = dbo.FN_PRECIO_SIN_IVA(@pImporteUnitario, @alicIva)
        END
        ELSE
        BEGIN
            SET @importe = @pImporteUnitario
        END
    ELSE
    BEGIN
        SET @importe = @pImporteUnitario
    END
END
ELSE
BEGIN
    IF @tmpConfigIVA = 'SI'
        IF @exento = 0
        BEGIN
            SET @pImporteUnitario = dbo.FN_PRECIO_SIN_IVA(@pImporteUnitario, @alicIva)
            SET @importe = dbo.FN_PRECIO_CON_IVA(@pImporteUnitario, @alicIva)
        END
        ELSE
        BEGIN
            SET @importe = @pImporteUnitario
            SET @pImporteUnitario = dbo.FN_PRECIO_SIN_IVA(@pImporteUnitario, @alicIva)
        END
    ELSE
    BEGIN
        IF @clasePrecio = 2
            SET @tmpConfigIVA = dbo.FN_OBTIENE_VALOR_CONFIGURACION('Clase2ConIVA', 'NO')

        IF @tmpConfigIVA = 'SI'
            IF @exento = 0
            BEGIN
                SET @pImporteUnitario = dbo.FN_PRECIO_SIN_IVA(@pImporteUnitario, @alicIva)
                SET @importe = dbo.FN_PRECIO_CON_IVA(@pImporteUnitario, @alicIva)
            END
            ELSE
            BEGIN
                SET @importe = @pImporteUnitario
                SET @pImporteUnitario = dbo.FN_PRECIO_SIN_IVA(@pImporteUnitario, @alicIva)
            END
        ELSE
        BEGIN
            SET @importe = @pImporteUnitario
        END
    END
END

IF @pImporteUnitario IS NULL SET @pImporteUnitario = 1
SET @total = (@importe * @pCantidad) - @importeDto

IF @pCantidad = 0 SET @pCantidad = 1

DECLARE @coeficiente float
DECLARE @cantidadUD float

SELECT @coeficiente = ISNULL(COEFICIENTE, 1)
FROM S_TA_EQUIV
WHERE IDUNIDAD = @idUnidad
  AND IDUNIDAD_EQUIV = @unidadBase
  AND LTRIM(IDARTICULO) = LTRIM(@pIdArticulo)

SET @Costo = @Costo * @coeficiente
IF LTRIM(@idMoneda) <> '1'
BEGIN
    IF LTRIM(@idMoneda) = '2' SET @cotiz = ISNULL((SELECT TOP 1 ISNULL(MONEDA2, 1) FROM TA_COTIZACION ORDER BY FECHA_HORA DESC), 1)
    IF LTRIM(@idMoneda) = '3' SET @cotiz = ISNULL((SELECT TOP 1 ISNULL(MONEDA3, 1) FROM TA_COTIZACION ORDER BY FECHA_HORA DESC), 1)
    IF LTRIM(@idMoneda) = '4' SET @cotiz = ISNULL((SELECT TOP 1 ISNULL(MONEDA4, 1) FROM TA_COTIZACION ORDER BY FECHA_HORA DESC), 1)
    IF LTRIM(@idMoneda) = '5' SET @cotiz = ISNULL((SELECT TOP 1 ISNULL(MONEDA5, 1) FROM TA_COTIZACION ORDER BY FECHA_HORA DESC), 1)
    SET @Costo = @Costo * @cotiz
END

SET @cantKG = @pCantidad * @coeficiente
SET @cantidadUD = @cantKG / @pCantidad

IF @tc = 'NP'
    SET @totalInsumo = @total
ELSE
    SET @totalInsumo = @total

SET DATEFORMAT YMD
SET NOCOUNT ON
BEGIN TRANSACTION
BEGIN TRY
    INSERT INTO V_MV_CPTEINSUMOS
    (TC, IDCOMPROBANTE, IDCOMPLEMENTO, CLASEPRECIO,
     IDARTICULO, DESCRIPCION, IDUNIDAD, ALICIVA, EXENTO,
     CANTIDADUD, CANTIDAD,
     IMPORTE_S_IVA,
     IMPORTE,
     PORCDTO,
     IMPORTEDTO,
     TOTAL,
     TOTALFINAL,
     COSTO,
     IdLista, IDUNIDADBASE, EQUIV_UDBASE, CANT_BL, CANT_KG)
    VALUES
    (
        @tc, @idComprobante, 0, @clasePrecio,
        @pIdArticulo, @descripcion, @idUnidad, @alicIva, @exento,
        @cantidadUD, @pCantidad,
        @pImporteUnitario,
        @importe,
        @pPorcDescuento,
        @importeDto,
        @totalInsumo,
        @total,
        @Costo,
        @idlista, @unidadBase, @pCantidad, 1, @cantKG
    )

    IF @@error <> 0
    BEGIN
        ROLLBACK TRANSACTION
        SET @pIdVMVCpteInsumosRES = NULL
        SET @pResultado = 21
        SET @pMensaje = 'No pudo darse de alta el pedido'
        RETURN
    END
    ELSE
    BEGIN
        COMMIT TRANSACTION

        DECLARE @IMPORTE_C_IVA float, @IMPORTE_IVA float, @NETO_GRAVADO float

        SET @tmpConfigIVA = dbo.FN_OBTIENE_VALOR_CONFIGURACION('MaestroArticuloConIVA', 'NO')
        IF @tmpConfigIVA = 'SI'
            IF @exento = 1
            BEGIN
                SET @NETO_GRAVADO = dbo.FN_PRECIO_SIN_IVA(@importe, @alicIva) * @pCantidad
                SET @IMPORTE_IVA = 0
            END
            ELSE
            BEGIN
                SET @NETO_GRAVADO = @pImporteUnitario * @pCantidad
                SET @IMPORTE_IVA = (@importe * @pCantidad) - @NETO_GRAVADO
            END
        ELSE
        BEGIN
            IF @clasePrecio = 2
                SET @tmpConfigIVA = dbo.FN_OBTIENE_VALOR_CONFIGURACION('Clase2ConIVA', 'NO')

            IF @tmpConfigIVA = 'SI'
                IF @exento = 1
                BEGIN
                    SET @NETO_GRAVADO = dbo.FN_PRECIO_SIN_IVA(@importe, @alicIva) * @pCantidad
                    SET @IMPORTE_IVA = 0
                END
                ELSE
                BEGIN
                    SET @NETO_GRAVADO = @pImporteUnitario * @pCantidad
                    SET @IMPORTE_IVA = (@importe * @pCantidad) - @NETO_GRAVADO
                END
            ELSE
                SET @NETO_GRAVADO = @importe * @pCantidad
        END

        IF @TC = 'FP' OR @tc = 'NP'
            SET @IMPORTE_C_IVA = @NETO_GRAVADO + @IMPORTE_IVA
        ELSE IF @exento = 1
        BEGIN
            -- Artículo exento: no se le vuelve a aplicar alícuota (bug corregido -- esta rama
            -- pisaba el IVA=0 ya calculado arriba con un 21% fijo).
            SET @IMPORTE_C_IVA = @NETO_GRAVADO
            SET @IMPORTE_IVA = 0
        END
        ELSE
        BEGIN
            -- Usa la alícuota real del artículo (bug corregido -- antes era 21% fijo sin importar
            -- @alicIva, así que un artículo al 10.5%/27%/0% se facturaba mal en toda Factura de POS).
            SET @IMPORTE_C_IVA = @NETO_GRAVADO * (1 + (@alicIva / 100))
            SET @IMPORTE_IVA = @IMPORTE_C_IVA - @NETO_GRAVADO
        END

        -- Desglose por alícuota (hasta 4 tasas distintas por comprobante, mismo esquema que ya lee
        -- FacturaDocumentService): se reusa el primer slot vacío o que ya tenga esta misma tasa.
        DECLARE @AlicIva1Actual float, @AlicIva2Actual float, @AlicIva3Actual float, @AlicIva4Actual float
        DECLARE @slotUsar int = NULL

        SELECT @AlicIva1Actual = AlicIva, @AlicIva2Actual = AlicIva2,
               @AlicIva3Actual = AlicIVA3, @AlicIva4Actual = AlicIVA4
        FROM V_MV_Cpte WHERE ID = @pIdCpte

        IF @exento = 0 AND @IMPORTE_IVA <> 0
        BEGIN
            IF ISNULL(@AlicIva1Actual, 0) = 0 OR @AlicIva1Actual = @alicIva SET @slotUsar = 1
            ELSE IF ISNULL(@AlicIva2Actual, 0) = 0 OR @AlicIva2Actual = @alicIva SET @slotUsar = 2
            ELSE IF ISNULL(@AlicIva3Actual, 0) = 0 OR @AlicIva3Actual = @alicIva SET @slotUsar = 3
            ELSE IF ISNULL(@AlicIva4Actual, 0) = 0 OR @AlicIva4Actual = @alicIva SET @slotUsar = 4
            -- Una 5ta alícuota distinta en el mismo comprobante no debería darse en la práctica (AFIP
            -- define hasta 4 tasas vigentes relevantes para este esquema); si ocurriera, el importe ya
            -- queda sumado en IMPORTE/NetoGravado pero sin entrar al desglose por alícuota.
        END

        UPDATE V_MV_Cpte
        SET IMPORTE = ISNULL(IMPORTE, 0) + @IMPORTE_C_IVA,
            IMPORTE_S_IVA = ISNULL(IMPORTE_S_IVA, 0) + (@pImporteUnitario * @pCantidad),
            ImporteInsumos = ISNULL(ImporteInsumos, 0) + (@totalInsumo),
            NetoGravado = ISNULL(NetoGravado, 0) + @NETO_GRAVADO,
            NetoNoGravado = 0,
            AlicIva = CASE WHEN @slotUsar = 1 THEN @alicIva ELSE AlicIva END,
            ImporteIva = ISNULL(ImporteIva, 0) + CASE WHEN @slotUsar = 1 THEN @IMPORTE_IVA ELSE 0 END,
            AlicIva2 = CASE WHEN @slotUsar = 2 THEN @alicIva ELSE AlicIva2 END,
            ImporteIva2 = ISNULL(ImporteIva2, 0) + CASE WHEN @slotUsar = 2 THEN @IMPORTE_IVA ELSE 0 END,
            AlicIVA3 = CASE WHEN @slotUsar = 3 THEN @alicIva ELSE AlicIVA3 END,
            ImpIVA3 = ISNULL(ImpIVA3, 0) + CASE WHEN @slotUsar = 3 THEN @IMPORTE_IVA ELSE 0 END,
            AlicIVA4 = CASE WHEN @slotUsar = 4 THEN @alicIva ELSE AlicIVA4 END,
            ImpIVA4 = ISNULL(ImpIVA4, 0) + CASE WHEN @slotUsar = 4 THEN @IMPORTE_IVA ELSE 0 END
        WHERE ID = @pIdCpte

        SET @pResultado = 11
        SET @pMensaje = 'El pedido se ha dado de alta con exito'
        SET @pIdVMVCpteInsumosRES = @@IDENTITY
    END
END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION
    ROLLBACK TRANSACTION
    SET @pIdVMVCpteInsumosRES = NULL
    SET @pResultado = ERROR_NUMBER()
    SET @pMensaje = ERROR_MESSAGE()
END CATCH
GO
