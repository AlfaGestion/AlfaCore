/*
   Asiento contable de la factura del POS.

   La cobranza (CBCT/CBFP) tiene su propio asiento y no debe utilizarse para
   contabilizar la factura. Las cuentas se resuelven desde la configuracion
   existente: cuenta del artículo, V_TA_Input_Ventas y CUENTAVENTASDEFAULTINS.
*/
IF OBJECT_ID(N'[dbo].[sp_web_CreaAsientoFactura]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[sp_web_CreaAsientoFactura];
GO

CREATE PROCEDURE [dbo].[sp_web_CreaAsientoFactura]
    @pIdCpte       int,
    @pResultado    smallint = NULL OUTPUT,
    @pMensaje      varchar(255) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Tc nvarchar(4), @Sucursal nvarchar(4), @Numero nvarchar(8), @Letra nvarchar(1);
    DECLARE @Cliente nvarchar(15), @Nombre nvarchar(50), @Fecha datetime, @Importe money;
    DECLARE @CondIva nvarchar(4), @Motivo nvarchar(4), @UNegocio nvarchar(4);
    DECLARE @Periodo nvarchar(6), @TipoReg nvarchar(4), @Mes tinyint, @NumeroAsiento int;
    DECLARE @Usuario nvarchar(50), @Vendedor nvarchar(4), @CuentaVaria nvarchar(15);
    DECLARE @CuentaIva21 nvarchar(15), @CuentaIva105 nvarchar(15), @CuentaCliente nvarchar(15);
    DECLARE @EsDebito bit = 1, @FechaHora datetime = GETDATE();

    SELECT TOP (1)
        @Tc = LTRIM(RTRIM(TC)), @Sucursal = LTRIM(RTRIM(SUCURSAL)),
        @Numero = LTRIM(RTRIM(NUMERO)), @Letra = LTRIM(RTRIM(LETRA)),
        @Cliente = LTRIM(RTRIM(CUENTA)), @Nombre = NOMBRE,
        @Fecha = TRY_CONVERT(datetime, FECHA, 103), @Importe = ISNULL(IMPORTE, 0),
        @CondIva = LTRIM(RTRIM(CONDICIONIVA)),
        @Motivo = LTRIM(RTRIM(IDMOTIVOCPRAVTA)),
        @UNegocio = UNEGOCIO, @Vendedor = IDVENDEDOR
    FROM dbo.V_MV_CPTE
    WHERE ID = @pIdCpte;

    IF @Tc IS NULL
    BEGIN
        SET @pResultado = 21;
        SET @pMensaje = 'No se encontró la factura para generar el asiento.';
        RETURN;
    END;

    -- Este procedimiento se invoca para comprobantes que afectan ventas.
    IF @Tc NOT IN ('FC', 'FP', 'TK', 'TKFC', 'ND', 'NC', 'NCFP')
    BEGIN
        SET @pResultado = 11;
        SET @pMensaje = 'El comprobante no requiere asiento de ventas.';
        RETURN;
    END;

    IF @Tc IN ('NC', 'NCFP') SET @EsDebito = 0;
    SET @Fecha = ISNULL(@Fecha, CONVERT(datetime, CONVERT(date, GETDATE())));
    SET @Mes = MONTH(@Fecha);

    -- El tipo de registro de ventas puede parametrizarse. VT es el valor
    -- histórico del sistema para ventas y solo actúa como compatibilidad.
    SELECT TOP (1) @TipoReg = LTRIM(RTRIM(COALESCE(NULLIF(VALOR, ''), CONVERT(nvarchar(50), ValorAux))))
    FROM dbo.TA_CONFIGURACION
    WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN ('TIPOREGVENTAS', 'TIPO_REG_VENTAS', 'CONTABILIDAD_TIPO_REG_VENTAS')
    ORDER BY CASE UPPER(LTRIM(RTRIM(CLAVE))) WHEN 'TIPOREGVENTAS' THEN 0 ELSE 1 END;
    SET @TipoReg = NULLIF(@TipoReg, '');
    IF @TipoReg IS NULL SET @TipoReg = 'VT';

    SELECT TOP (1) @Periodo = LTRIM(RTRIM(PERIODO))
    FROM dbo.MV_EJERCICIOS
    WHERE TRY_CONVERT(datetime, [FECHA DESDE], 103) <= @Fecha
      AND TRY_CONVERT(datetime, [FECHA HASTA], 103) >= @Fecha;
    IF @Periodo IS NULL
    BEGIN
        SET @pResultado = 21;
        SET @pMensaje = 'No existe un ejercicio contable abierto para la fecha de la factura.';
        RETURN;
    END;

    SELECT TOP (1)
        @CuentaVaria = NULLIF(LTRIM(RTRIM(VALOR)), '')
    FROM dbo.TA_CONFIGURACION
    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = N'CUENTAVENTASDEFAULTINS';

    SELECT TOP (1)
        @CuentaIva21 = NULLIF(LTRIM(RTRIM(DFI_IVARI)), ''),
        @CuentaIva105 = NULLIF(LTRIM(RTRIM(DFI_IVAREC)), '')
    FROM dbo.TA_CUENTASIVA;

    SELECT TOP (1) @CuentaCliente = CODIGO
    FROM dbo.MA_CUENTAS
    WHERE LTRIM(RTRIM(CODIGO)) = @Cliente;
    IF @CuentaCliente IS NULL
    BEGIN
        SET @pResultado = 21;
        SET @pMensaje = 'La cuenta del cliente de la factura no existe en MA_CUENTAS.';
        RETURN;
    END;

    IF @CuentaVaria IS NULL
    BEGIN
        SET @pResultado = 21;
        SET @pMensaje = 'No está configurada la cuenta de ventas/otros conceptos en CUENTAVENTASDEFAULTINS.';
        RETURN;
    END;

    SELECT TOP (1) @CuentaVaria = CODIGO
    FROM dbo.MA_CUENTAS
    WHERE LTRIM(RTRIM(CODIGO)) = @CuentaVaria;
    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(CODIGO)) = @CuentaVaria
    ) SET @CuentaVaria = NULL;
    IF @CuentaVaria IS NULL
    BEGIN
        SET @pResultado = 21;
        SET @pMensaje = 'La cuenta configurada para ventas no existe en MA_CUENTAS.';
        RETURN;
    END;

    IF @CuentaIva21 IS NOT NULL AND NOT EXISTS
    (
        SELECT 1 FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(CODIGO)) = @CuentaIva21
    ) SET @CuentaIva21 = NULL;
    IF @CuentaIva105 IS NOT NULL AND NOT EXISTS
    (
        SELECT 1 FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(CODIGO)) = @CuentaIva105
    ) SET @CuentaIva105 = NULL;

    SELECT TOP (1) @CuentaIva21 = CODIGO
    FROM dbo.MA_CUENTAS
    WHERE @CuentaIva21 IS NOT NULL AND LTRIM(RTRIM(CODIGO)) = @CuentaIva21;
    SELECT TOP (1) @CuentaIva105 = CODIGO
    FROM dbo.MA_CUENTAS
    WHERE @CuentaIva105 IS NOT NULL AND LTRIM(RTRIM(CODIGO)) = @CuentaIva105;

    IF EXISTS (
        SELECT 1 FROM dbo.MV_ASIENTOS
        WHERE TC = @Tc AND SUCURSAL = @Sucursal AND NUMERO = @Numero AND LETRA = @Letra
          AND TIPO_REG = @TipoReg
    )
    BEGIN
        SET @pResultado = 11;
        SET @pMensaje = 'El asiento de la factura ya existe.';
        RETURN;
    END;

    CREATE TABLE #Lineas
    (
        Cuenta nvarchar(15) NOT NULL,
        DebeHaber nvarchar(1) NOT NULL,
        Importe money NOT NULL,
        Aliciva money NULL
    );

    -- La cuenta del cliente recibe el total de la factura.
    INSERT INTO #Lineas (Cuenta, DebeHaber, Importe, Aliciva)
    VALUES (@CuentaCliente, CASE WHEN @EsDebito = 1 THEN 'D' ELSE 'H' END, @Importe, NULL);

    -- Primero la cuenta contable del articulo; luego la imputacion del motivo
    -- de venta; finalmente la cuenta varia configurada en CUENTAVENTASDEFAULTINS.
    INSERT INTO #Lineas (Cuenta, DebeHaber, Importe, Aliciva)
    SELECT
        COALESCE(NULLIF(LTRIM(RTRIM(a.CuentaContable)), ''),
                 NULLIF(LTRIM(RTRIM(iv.Repuestos)), ''), @CuentaVaria),
        CASE WHEN @EsDebito = 1 THEN 'H' ELSE 'D' END,
        SUM(ISNULL(i.IMPORTE_S_IVA, 0)),
        NULL
    FROM dbo.V_MV_CPTEINSUMOS i
    LEFT JOIN dbo.V_MA_ARTICULOS a ON LTRIM(RTRIM(a.IDARTICULO)) = LTRIM(RTRIM(i.IDARTICULO))
    LEFT JOIN dbo.V_TA_Input_Ventas iv
      ON LTRIM(RTRIM(iv.IdMotivoVta)) = ISNULL(@Motivo, '1')
     AND LTRIM(RTRIM(iv.CondIva)) = ISNULL(@CondIva, '1')
    WHERE i.TC = @Tc AND i.IDCOMPROBANTE = @Sucursal + @Numero + @Letra AND i.IDCOMPLEMENTO = 0
    GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(a.CuentaContable)), ''),
                      NULLIF(LTRIM(RTRIM(iv.Repuestos)), ''), @CuentaVaria);

    -- Otros conceptos (incluido el recargo de tarjeta) se contabilizan por su
    -- neto en la cuenta varia configurada, no como un articulo inexistente.
    INSERT INTO #Lineas (Cuenta, DebeHaber, Importe, Aliciva)
    SELECT @CuentaVaria,
           CASE WHEN @EsDebito = 1 THEN 'H' ELSE 'D' END,
           SUM(ISNULL(o.IMPORTE_S_IVA, 0)), NULL
    FROM dbo.V_MV_CPTE_OBSERV o
    WHERE o.TC = @Tc AND o.IDCOMPROBANTE = @Sucursal + @Numero + @Letra
      AND o.IDCOMPLEMENTO = 0 AND ISNULL(o.IMPORTE_S_IVA, 0) <> 0
    HAVING SUM(ISNULL(o.IMPORTE_S_IVA, 0)) <> 0;

    -- IVA separado por alicuota, usando la configuracion oficial de IVA.
    INSERT INTO #Lineas (Cuenta, DebeHaber, Importe, Aliciva)
    SELECT CASE WHEN x.Aliciva <= 11 THEN @CuentaIva105 ELSE @CuentaIva21 END,
           CASE WHEN @EsDebito = 1 THEN 'H' ELSE 'D' END,
           SUM(x.Iva), x.Aliciva
    FROM
    (
        SELECT CAST(ISNULL(i.AlicIVA, 0) AS money) AS Aliciva,
               ISNULL(i.IMPORTE, 0) - ISNULL(i.IMPORTE_S_IVA, 0) AS Iva
        FROM dbo.V_MV_CPTEINSUMOS i
        WHERE i.TC = @Tc AND i.IDCOMPROBANTE = @Sucursal + @Numero + @Letra AND i.IDCOMPLEMENTO = 0
        UNION ALL
        SELECT CAST(21 AS money), ISNULL(o.IMPORTE, 0) - ISNULL(o.IMPORTE_S_IVA, 0)
        FROM dbo.V_MV_CPTE_OBSERV o
        WHERE o.TC = @Tc AND o.IDCOMPROBANTE = @Sucursal + @Numero + @Letra
          AND o.IDCOMPLEMENTO = 0 AND ISNULL(o.IMPORTE, 0) <> ISNULL(o.IMPORTE_S_IVA, 0)
    ) x
    WHERE x.Iva <> 0 AND CASE WHEN x.Aliciva <= 11 THEN @CuentaIva105 ELSE @CuentaIva21 END IS NOT NULL
    GROUP BY CASE WHEN x.Aliciva <= 11 THEN @CuentaIva105 ELSE @CuentaIva21 END, x.Aliciva;

    IF EXISTS (SELECT 1 FROM #Lineas WHERE Importe < 0)
    BEGIN
        UPDATE #Lineas SET Importe = ABS(Importe), DebeHaber = CASE WHEN DebeHaber = 'D' THEN 'H' ELSE 'D' END
        WHERE Importe < 0;
    END;

    DECLARE @TotalDebe money = ISNULL((SELECT SUM(Importe) FROM #Lineas WHERE DebeHaber = 'D'), 0);
    DECLARE @TotalHaber money = ISNULL((SELECT SUM(Importe) FROM #Lineas WHERE DebeHaber = 'H'), 0);
    DECLARE @Diferencia money = @TotalDebe - @TotalHaber;
    IF ABS(@Diferencia) >= 0.01
    BEGIN
        INSERT INTO #Lineas (Cuenta, DebeHaber, Importe, Aliciva)
        VALUES (@CuentaVaria, CASE WHEN @Diferencia > 0 THEN 'H' ELSE 'D' END, ABS(@Diferencia), NULL);
    END;

    SELECT TOP (1) @Usuario = NOMBRE
    FROM dbo.TA_USUARIOS
    WHERE LTRIM(RTRIM(IDVENDEDOR)) = LTRIM(RTRIM(@Vendedor));
    SET @Usuario = ISNULL(NULLIF(@Usuario, ''), SYSTEM_USER);

    DECLARE @RecursoBloqueo nvarchar(255) = N'ALFACORE-ASIENTO-VENTAS-' + @Periodo + N'-' + @TipoReg;

    BEGIN TRANSACTION;
    BEGIN TRY
        EXEC sys.sp_getapplock
            @Resource = @RecursoBloqueo,
            @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 10000;

        IF EXISTS (
            SELECT 1 FROM dbo.MV_ASIENTOS
            WHERE TC = @Tc AND SUCURSAL = @Sucursal AND NUMERO = @Numero AND LETRA = @Letra
              AND TIPO_REG = @TipoReg
        )
        BEGIN
            COMMIT TRANSACTION;
            SET @pResultado = 11;
            SET @pMensaje = 'El asiento de la factura ya existe.';
            RETURN;
        END;

        SELECT @NumeroAsiento = ISNULL(MAX([NUMERO ASIENTO]), 0) + 1
        FROM dbo.MV_ASIENTOS WITH (UPDLOCK, HOLDLOCK)
        WHERE MES_OPERATIVO = @Mes AND TIPO_REG = @TipoReg AND PERIODO = @Periodo;

        INSERT INTO dbo.MV_ASIENTOS
        (CUENTA, SECUENCIA, MES_OPERATIVO, [NUMERO ASIENTO], FECHA, DETALLE,
         TC, SUCURSAL, NUMERO, LETRA, [DEBE-HABER], IMPORTE, MONEDA,
         COTIZACION, PERIODO, CABIMPORTE, TIPO_REG, CONTABILIZADO,
         FECHAHORA_GRABACION, FechaSubdiario, USUARIO_LOGEADO, EsResumen,
         ES_SALDO_APERTURA, CABCUENTA, CABNOMBRE, LIVA_TIPO, LIVA_TOTAL, UNEGOCIO)
        SELECT Cuenta, ROW_NUMBER() OVER (ORDER BY CASE WHEN DebeHaber = CASE WHEN @EsDebito = 1 THEN 'D' ELSE 'H' END THEN 0 ELSE 1 END, Cuenta),
               @Mes, @NumeroAsiento, @Fecha, N'Factura Web POS', @Tc, @Sucursal,
               @Numero, @Letra, DebeHaber, Importe, N'   1', 1, @Periodo,
               @Importe, @TipoReg, 0, @FechaHora, @Fecha, @Usuario, 0, 0,
               @CuentaCliente, @Nombre, N'VENTAS', CASE WHEN DebeHaber = CASE WHEN @EsDebito = 1 THEN 'D' ELSE 'H' END THEN @Importe ELSE 0 END,
               @UNegocio
        FROM #Lineas
        WHERE Importe <> 0;

        IF @@ROWCOUNT = 0
        BEGIN
            ROLLBACK TRANSACTION;
            SET @pResultado = 21;
            SET @pMensaje = 'No se pudo generar ninguna línea para el asiento de la factura.';
            RETURN;
        END;

        COMMIT TRANSACTION;
        SET @pResultado = 11;
        SET @pMensaje = 'Asiento de factura creado correctamente.';
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET @pResultado = ERROR_NUMBER();
        SET @pMensaje = ERROR_MESSAGE();
    END CATCH;
END;
GO
