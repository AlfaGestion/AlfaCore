-- ============================================================
-- Punto de venta - stored procedures base de grabacion
-- Crea o actualiza los SPs requeridos por el flujo POS:
--   sp_web_Alta_Comprobante
--   sp_web_CpteInsumos
--   sp_web_CreaCobPorFactura
--   sp_web_creaLineaAsiento
--   sp_web_CreaAplicacionCobranzaFactura
-- Referencias:
--   docs/1  Grabacion del comprobante.txt
--   docs/2 Cobranza y aplicacion.txt
-- ============================================================

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_web_Alta_Comprobante]
    @pCliente                         nvarchar(15) = null,
    @pVendedor                        nvarchar(4) = null,
    @pFecha                           datetime = null,
    @pObservaciones                   nvarchar(250) = null,
    @pLat                             nvarchar(15) = null,
    @pLng                             nvarchar(15) = null,
    @pTC                              nvarchar(4) = null,
    @pSucursal                        nvarchar(4) = null,
    @pNumero                          nvarchar(8) = null,
    @pLetra                           nvarchar(1) = null,
    @pResultado                       smallint = NULL OUTPUT,
    @pMensaje                         varchar(255) = NULL OUTPUT,
    @pIdComprobanteRES                int = NULL OUTPUT
AS
DECLARE @IdComprobante nvarchar(13)
DECLARE @nombre nvarchar(50)
DECLARE @domicilio nvarchar(50)
DECLARE @calle nvarchar(50)
DECLARE @numero nvarchar(50)
DECLARE @piso nvarchar(50)
DECLARE @departamento nvarchar(50)
DECLARE @telefono nvarchar(100)
DECLARE @localidad nvarchar(50)
DECLARE @idProvincia nvarchar(4)
DECLARE @codigoPostal nvarchar(10)
DECLARE @documentoTipo nvarchar(4)
DECLARE @documentoNumero nvarchar(15)
DECLARE @condicionIva nvarchar(50)
DECLARE @idCondCpraVta nvarchar(4)
DECLARE @idLista nvarchar(4)
DECLARE @vendedorCliente nvarchar(4)
DECLARE @clasePrecio int

SET NOCOUNT ON

IF @pCliente = ''
    SET @pCliente = (SELECT VALOR FROM TA_CONFIGURACION WHERE CLAVE = 'CUENTACONSUMIDORFINAL')

SELECT @nombre = ISNULL(RAZON_SOCIAL, ''),
       @calle = ISNULL(CALLE, '.'),
       @numero = ISNULL(NUMERO, '.'),
       @piso = ISNULL(PISO, ''),
       @departamento = ISNULL(DEPARTAMENTO, ''),
       @telefono = TELEFONO,
       @localidad = LOCALIDAD,
       @idProvincia = PROVINCIA,
       @codigoPostal = CPOSTAL,
       @documentoTipo = DOCUMENTO_TIPO,
       @documentoNumero = NUMERO_DOCUMENTO,
       @condicionIva = IVA,
       @idCondCpraVta = IDCOND_CPRA_VTA,
       @clasePrecio = CASE WHEN ISNUMERIC(CLASE) = 1 THEN CAST(CLASE AS int) ELSE NULL END,
       @idLista = IDLISTA,
       @vendedorCliente = IDVENDEDOR
FROM VT_CLIENTES
WHERE CODIGO = @pCliente

SET @domicilio = @calle + ' ' + @numero
IF (@piso <> '') SET @domicilio = @domicilio + ' ' + LTRIM(RTRIM(@piso)) + ' Piso '
IF (@departamento <> '') SET @domicilio = @domicilio + ' Dpto: ' + LTRIM(RTRIM(@departamento))
IF @condicionIva IS NULL SET @condicionIva = '   1'
IF @documentoTipo = 'None' SET @documentoTipo = '   1'

IF @pNumero IS NULL OR LTRIM(RTRIM(@pNumero)) = ''
BEGIN
    DECLARE @ultimoNumero int

    SELECT @ultimoNumero = MAX(CAST(NUMERO AS int))
    FROM V_MV_CPTE
    WHERE ISNUMERIC(NUMERO) = 1
      AND TC = @pTC
      AND SUCURSAL = @pSucursal
      AND LETRA = @pLetra

    SET @ultimoNumero = ISNULL(@ultimoNumero, 0) + 1
    SET @pNumero = RIGHT('00000000' + CAST(@ultimoNumero AS nvarchar), 8)
END

SET @IdComprobante = @pSucursal + @pNumero + @pLetra

IF @clasePrecio IS NULL SET @clasePrecio = 1
IF @pVendedor = '' OR @pVendedor IS NULL
    SET @pVendedor = @vendedorCliente

SET @pVendedor = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pVendedor)), 4)
SET @idLista = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@idLista)), 4)

SET DATEFORMAT YMD
SET NOCOUNT ON

BEGIN TRANSACTION
BEGIN TRY
    INSERT INTO V_MV_CPTE
    (TC, IDCOMPROBANTE, IDCOMPLEMENTO,
     FECHA, FECHAESTFIN, FECHAESTINICIO, CUENTA,
     NOMBRE, DOMICILIO, TELEFONO,
     LOCALIDAD, IDPROVINCIA, CODIGOPOSTAL,
     DOCUMENTOTIPO, DOCUMENTONUMERO, CONDICIONIVA,
     IDCOND_CPRA_VTA, COMENTARIOS, IMPORTE, IMPORTE_S_IVA,
     MONEDA, IDVENDEDOR, CLASEPRECIO, UNEGOCIO_DESTINO, IdLista, IDMOTIVOCPRAVTA, IDDEPOSITO, AlicIva, ImporteIva, NEtoGravado, NetoNoGravado, Unegocio,
     FechaHora_Grabacion, SUCURSAL, NUMERO, LETRA)
    VALUES
    (
        @pTC, @IdComprobante, 0,
        @pFecha, @pFecha, @pFecha, @pCliente,
        @Nombre, @Domicilio, ISNULL(@Telefono, ''),
        @Localidad, @idProvincia, LTRIM(@codigoPostal),
        @documentoTipo, LTRIM(@documentoNumero), @condicionIva,
        @idCondCpraVta, @pObservaciones, 0, 0,
        '   1', @pVendedor, @clasePrecio, '   1', @idLista, '   1', '   1', 21, 0, 0, 0, '   1',
        GETDATE(), @pSucursal, @pNumero, @pLetra
    )

    COMMIT TRANSACTION

    SET @pResultado = 11
    SET @pMensaje = 'El pedido se ha dado de alta con exito'
    SET @pIdComprobanteRES = SCOPE_IDENTITY()

    INSERT INTO S_TA_UBICACIONES_VENDEDOR (lat, long, idvendedor, fechahora, idcomprobante)
    VALUES (@pLat, @pLng, @pVendedor, GETDATE(), @IdComprobante)
END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION
    SET @pIdComprobanteRES = NULL
    SET @pResultado = ERROR_NUMBER()
    SET @pMensaje = ERROR_MESSAGE()
END CATCH
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

IF ((@alicIva = NULL) OR (@alicIva IS NULL) OR (@alicIva = 0))
BEGIN
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
        ELSE
        BEGIN
            SET @IMPORTE_C_IVA = @NETO_GRAVADO * 1.21
            SET @IMPORTE_IVA = @IMPORTE_C_IVA - @NETO_GRAVADO
        END

        UPDATE V_MV_Cpte
        SET IMPORTE = ISNULL(IMPORTE, 0) + @IMPORTE_C_IVA,
            IMPORTE_S_IVA = ISNULL(IMPORTE_S_IVA, 0) + (@pImporteUnitario * @pCantidad),
            ImporteInsumos = ISNULL(ImporteInsumos, 0) + (@totalInsumo),
            ImporteIva = ISNULL(ImporteIva, 0) + @IMPORTE_IVA,
            NetoGravado = ISNULL(NetoGravado, 0) + @NETO_GRAVADO,
            NetoNoGravado = 0
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

CREATE OR ALTER PROCEDURE [dbo].[sp_web_CreaAplicacionCobranzaFactura]
    @pIdCobranza                     int,
    @pIdFactura                      int,
    @pResultado                      smallint = NULL OUTPUT,
    @pMensaje                        varchar(255) = NULL OUTPUT
AS
DECLARE @TCFactura nvarchar(4)
DECLARE @SucFactura nvarchar(4)
DECLARE @NumFactura nvarchar(8)
DECLARE @LetFactura nvarchar(1)
DECLARE @ImporteFactura real
DECLARE @Cliente nvarchar(13)
DECLARE @unegocio nvarchar(4)
DECLARE @TcCobranza nvarchar(4)
DECLARE @SucCobranza nvarchar(4)
DECLARE @NumCobranza nvarchar(8)
DECLARE @LetCobranza nvarchar(1)

SET NOCOUNT ON

SELECT @TCFactura = TC,
       @Cliente = Cuenta,
       @SucFactura = SUCURSAL,
       @NumFactura = NUMERO,
       @LetFactura = LETRA,
       @unegocio = UNEGOCIO,
       @ImporteFactura = IMPORTE
FROM V_MV_CPTE
WHERE ID = @pIdFactura

SELECT @TcCobranza = TC,
       @SucCobranza = SUCURSAL,
       @NumCobranza = NUMERO,
       @LetCobranza = LETRA
FROM V_MV_CPTE
WHERE ID = @pIdCobranza

IF (@unegocio IS NULL) OR (@unegocio = '')
BEGIN
    SELECT @unegocio = VALOR FROM TA_CONFIGURACION WHERE CLAVE = 'UNEGOCIO'
    SET @unegocio = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@unegocio)), 4)
END

IF (@unegocio IS NULL) OR (@unegocio = '') SET @unegocio = '   1'

BEGIN TRANSACTION
BEGIN TRY
    INSERT INTO MV_APLICACION
    (CUENTA, TC, SUCURSAL, NUMERO, LETRA, IDCOMPROBANTE, TCO_ORIGEN, SUCURSAL_ORIGEN, NUMERO_ORIGEN, LETRA_ORIGEN,
     IDComprobante_Origen, IDCOMPLEMENTO_ORIGEN, IMPORTE, Transmision, Recepcion, UNegocio, Secuencia)
    VALUES
    (@Cliente, @TcCobranza, @SucCobranza, @NumCobranza, @LetCobranza, @SucCobranza + @NumCobranza + @LetCobranza,
     @TCFactura, @SucFactura, @NumFactura, @LetFactura, @SucFactura + @NumFactura + @LetFactura, 0, @ImporteFactura, 0, 0, @unegocio, 0)

    IF @@ERROR <> 0 OR @@ROWCOUNT <> 1
    BEGIN
        ROLLBACK TRANSACTION
        SET @pResultado = 21
        SET @pMensaje = 'No pudo crearse la aplicacion'
        RETURN
    END
    ELSE
    BEGIN
        COMMIT TRANSACTION
        SET @pResultado = 11
        SET @pMensaje = 'Aplicacion creada correctamente'
    END
END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION
    SET @pResultado = ERROR_NUMBER()
    SET @pMensaje = ERROR_MESSAGE()
END CATCH
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_web_CreaCobPorFactura]
    @pIdCpte                         int,
    @pResultado                      smallint = NULL OUTPUT,
    @pMensaje                        varchar(255) = NULL OUTPUT,
    @pIdComprobanteRES               int = NULL OUTPUT
AS
DECLARE @IdComprobanteCobranza nvarchar(13)
DECLARE @nombre nvarchar(50)
DECLARE @domicilio nvarchar(50)
DECLARE @calle nvarchar(50)
DECLARE @numero nvarchar(50)
DECLARE @piso nvarchar(50)
DECLARE @departamento nvarchar(50)
DECLARE @telefono nvarchar(100)
DECLARE @localidad nvarchar(50)
DECLARE @idProvincia nvarchar(4)
DECLARE @codigoPostal nvarchar(10)
DECLARE @documentoTipo nvarchar(4)
DECLARE @documentoNumero nvarchar(15)
DECLARE @condicionIva nvarchar(50)
DECLARE @idCondCpraVta nvarchar(4)
DECLARE @idLista nvarchar(4)
DECLARE @unegocio nvarchar(4)
DECLARE @clasePrecio int

DECLARE @TCFactura nvarchar(4)
DECLARE @SucFactura nvarchar(4)
DECLARE @NumeroFactura nvarchar(8)
DECLARE @LetraFactura nvarchar(1)
DECLARE @ImporteFactura real

DECLARE @TcCobranza nvarchar(4)
DECLARE @pCliente nvarchar(15) = null
DECLARE @pVendedor nvarchar(4) = null
DECLARE @pFecha datetime = null
DECLARE @pObservaciones nvarchar(250) = null

SET NOCOUNT ON

SELECT @TCFactura = TC,
       @pCliente = Cuenta,
       @pVendedor = IdVendedor,
       @unegocio = UNEGOCIO,
       @SucFactura = SUCURSAL,
       @NumeroFactura = NUMERO,
       @LetraFactura = LETRA,
       @ImporteFactura = IMPORTE
FROM V_MV_CPTE
WHERE ID = @pIdCpte

IF @TCFactura = 'FP'
    SET @TcCobranza = 'CBFP'
ELSE
    SET @TcCobranza = 'CBCT'

SET @pFecha = CONVERT(varchar, GETDATE(), 103)

IF @pCliente = ''
    SET @pCliente = (SELECT VALOR FROM TA_CONFIGURACION WHERE CLAVE = 'CUENTACONSUMIDORFINAL')

SELECT @nombre = ISNULL(RAZON_SOCIAL, ''),
       @calle = ISNULL(CALLE, '.'),
       @numero = ISNULL(NUMERO, '.'),
       @piso = ISNULL(PISO, ''),
       @departamento = ISNULL(DEPARTAMENTO, ''),
       @telefono = TELEFONO,
       @localidad = LOCALIDAD,
       @idProvincia = PROVINCIA,
       @codigoPostal = CPOSTAL,
       @documentoTipo = DOCUMENTO_TIPO,
       @documentoNumero = NUMERO_DOCUMENTO,
       @condicionIva = IVA,
       @idCondCpraVta = IDCOND_CPRA_VTA,
       @clasePrecio = CLASE,
       @idLista = IDLISTA
FROM VT_CLIENTES
WHERE CODIGO = @pCliente

SET @domicilio = @calle + ' ' + @numero
IF (@piso <> '') SET @domicilio = @domicilio + ' ' + LTRIM(RTRIM(@piso)) + ' Piso '
IF (@departamento <> '') SET @domicilio = @domicilio + ' Dpto: ' + LTRIM(RTRIM(@departamento))
IF (@condicionIva = NULL) SET @condicionIva = '   1'

SET @IdComprobanteCobranza = dbo.FN_OBTIENE_PROXIMO_NUMERO_CPTE(@TcCobranza, '9999', 'X')

IF (@clasePrecio IS NULL) OR (@clasePrecio = NULL) SET @clasePrecio = 1
SET @pVendedor = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pVendedor)), 4)
SET @idLista = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@idLista)), 4)

SET DATEFORMAT YMD
SET NOCOUNT ON

BEGIN TRANSACTION
BEGIN TRY
    INSERT INTO V_MV_CPTE
    (TC, IDCOMPROBANTE, IDCOMPLEMENTO,
     FECHA, FECHAESTFIN, FECHAESTINICIO, CUENTA,
     NOMBRE, DOMICILIO, TELEFONO,
     LOCALIDAD, IDPROVINCIA, CODIGOPOSTAL,
     DOCUMENTOTIPO, DOCUMENTONUMERO, CONDICIONIVA,
     IDCOND_CPRA_VTA, COMENTARIOS, IMPORTE, IMPORTE_S_IVA,
     MONEDA, IDVENDEDOR, CLASEPRECIO, UNEGOCIO_DESTINO, IdLista, IDMOTIVOCPRAVTA, IDDEPOSITO, AlicIva, ImporteIva, NEtoGravado, NetoNoGravado, Unegocio,
     FechaHora_Grabacion)
    VALUES
    (
        @TcCobranza, @IdComprobanteCobranza, 0,
        @pFecha, @pFecha, @pFecha, @pCliente,
        @Nombre, @Domicilio, ISNULL(@Telefono, ''),
        @Localidad, @idProvincia, LTRIM(@codigoPostal),
        @documentoTipo, LTRIM(@documentoNumero), @condicionIva,
        @idCondCpraVta, @pObservaciones, @ImporteFactura, 0,
        '   1', @pVendedor, @clasePrecio, '   1', @idLista, '   1', '   1', 21, 0, 0, 0, '   1', GETDATE()
    )

    IF @@ERROR <> 0 OR @@ROWCOUNT <> 1
    BEGIN
        ROLLBACK TRANSACTION
        SET @pIdComprobanteRES = NULL
        SET @pResultado = 21
        SET @pMensaje = 'No pudo crearse la cobranza'
        RETURN
    END
    ELSE
    BEGIN
        COMMIT TRANSACTION
        SET @pResultado = 11
        SET @pMensaje = 'Cobranza creada correctamente'
        SET @pIdComprobanteRES = @@IDENTITY
    END
END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION
    SET @pIdComprobanteRES = NULL
    SET @pResultado = ERROR_NUMBER()
    SET @pMensaje = ERROR_MESSAGE()
END CATCH
GO

CREATE OR ALTER PROCEDURE [dbo].[sp_web_creaLineaAsiento]
    @pIdCobranza                     int = null,
    @pImporte                        real = 0,
    @pCuentaMp                       varchar(100),
    @pPrimero                        int,
    @pChequeNro                      varchar(50) = null,
    @pResultado                      smallint = NULL OUTPUT,
    @pMensaje                        varchar(255) = NULL OUTPUT
AS
DECLARE @FechaHoraGrabacion datetime
DECLARE @unegocio nvarchar(4)
DECLARE @USUARIO nvarchar(50)
DECLARE @Tc nvarchar(4)
DECLARE @IdComprobante nvarchar(13)
DECLARE @pCliente nvarchar(13)
DECLARE @pVendedor nvarchar(4)
DECLARE @pFecha datetime
DECLARE @secuencia int
DECLARE @SucCpte nvarchar(4)
DECLARE @NumCpte nvarchar(8)
DECLARE @LetCpte nvarchar(1)
DECLARE @TotalCpte real
DECLARE @idCaja nvarchar(4)

SET NOCOUNT ON

SELECT @Tc = TC,
       @pCliente = Cuenta,
       @pVendedor = IdVendedor,
       @unegocio = UNEGOCIO,
       @IdComprobante = IDCOMPROBANTE,
       @SucCpte = SUCURSAL,
       @NumCpte = NUMERO,
       @LetCpte = LETRA,
       @TotalCpte = ISNULL(IMPORTE, 0),
       @pFecha = FECHA
FROM V_MV_CPTE
WHERE ID = @pIdCobranza

SELECT @idCaja = ISNULL(idCaja, '1'),
       @USUARIO = NOMBRE
FROM TA_USUARIOS
WHERE LTRIM(idvendedor) = LTRIM(@pVendedor)

IF @idCaja IS NULL OR @idCaja = '' SET @idCaja = '1'
IF @USUARIO IS NULL SET @USUARIO = SYSTEM_USER

SET @pVendedor = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@pVendedor)), 4)
SET @idCaja = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@idCaja)), 4)

IF (@unegocio IS NULL) OR (@unegocio = '')
BEGIN
    SELECT @unegocio = VALOR FROM TA_CONFIGURACION WHERE CLAVE = 'UNEGOCIO'
    SET @unegocio = dbo.FN_FMT_LEERCODIGO(LTRIM(RTRIM(@unegocio)), 4)
END

IF (@unegocio IS NULL) OR (@unegocio = '') SET @unegocio = '   1'

SET @FechaHoraGrabacion = GETDATE()
SET @pFecha = CONVERT(varchar, @FechaHoraGrabacion, 103)

DECLARE @NroAsiento int
DECLARE @Periodo nvarchar(6)
DECLARE @CuentaMP nvarchar(15)
DECLARE @Mes_operativo int
DECLARE @DEBEHABER nvarchar(1)

SET @CuentaMP = @pCuentaMp
IF @CuentaMP = '' SET @CuentaMP = (SELECT VALOR FROM TA_CONFIGURACION WHERE CLAVE = 'CUENTA_CAJA')
IF @CuentaMP = '' SET @CuentaMP = '111010001'

SET @secuencia = 1

IF @pPrimero = 1
BEGIN
    SET @secuencia = 1
    SET @Mes_operativo = MONTH(@pFecha)

    SET @Periodo = (SELECT TOP 1 periodo FROM MV_EJERCICIOS WHERE [FECHA DESDE] <= @pFecha AND [FECHA HASTA] >= @pFecha)
    IF (@Periodo IS NULL) SET @Periodo = '0'
    SET @DEBEHABER = 'H'

    SET @CuentaMP = @pCliente
    SET @pImporte = @TotalCpte

    SET @NroAsiento = (
        SELECT MAX([NUMERO ASIENTO]) + 1
        FROM MV_ASIENTOS
        WHERE MES_OPERATIVO = @Mes_operativo
          AND RTRIM(LTRIM(TIPO_REG)) = 'CB'
          AND RTRIM(LTRIM(PERIODO)) = @Periodo
    )
    IF @NroAsiento IS NULL OR @NroAsiento = '' SET @NroAsiento = 1
END
ELSE
BEGIN
    SET @DEBEHABER = 'D'
    SELECT TOP 1
        @secuencia = SECUENCIA + 1,
        @Mes_operativo = MES_OPERATIVO,
        @Periodo = PERIODO,
        @NroAsiento = [NUMERO ASIENTO]
    FROM MV_ASIENTOS
    WHERE TC = @Tc
      AND SUCURSAL = SUBSTRING(@IdComprobante, 1, 4)
      AND NUMERO = SUBSTRING(@IdComprobante, 5, 8)
      AND LETRA = RIGHT(@IdComprobante, 1)
    ORDER BY SECUENCIA DESC
END
SET NOCOUNT ON

BEGIN TRANSACTION

INSERT INTO MV_ASIENTOS
(CUENTA, SECUENCIA, MES_OPERATIVO, [NUMERO ASIENTO], FECHA, DETALLE, TC, SUCURSAL,
 NUMERO, LETRA, [DEBE-HABER], IMPORTE, MONEDA, COTIZACION, PERIODO, CABIMPORTE, TIPO_REG, CONTABILIZADO,
 FECHAHORA_GRABACION, FechaSubdiario, USUARIO_LOGEADO, IDCAJAS, NroComprobanteBancario, UNEGOCIO)
VALUES
(@CuentaMP, @secuencia, @Mes_operativo, @NroAsiento, @pFecha, 'Cobranza Web', @Tc, SUBSTRING(@IdComprobante, 1, 4),
 SUBSTRING(@IdComprobante, 5, 8), RIGHT(@IdComprobante, 1), @DEBEHABER, @pImporte, '   1', 1, @Periodo, @TotalCpte,
 'CB', 0, @pFecha, @FechaHoraGrabacion, @USUARIO, @idCaja, @pChequeNro, @unegocio)

IF @@ERROR <> 0 OR @@ROWCOUNT <> 1
BEGIN
    ROLLBACK TRANSACTION
    SET @pResultado = 21
    SET @pMensaje = 'No pudo darse de alta el registro'
    RETURN
END

COMMIT TRANSACTION
SET @pResultado = 11
SET @pMensaje = 'El registro se ha dado de alta con exito'
GO
