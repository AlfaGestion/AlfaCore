-- Reparación manual y acotada de CB generadas por AlfaCore en ALFANET2007.
-- No colocar en App_Data/updates: requiere revisión y ejecución explícita.
-- Primero ejecutar con @Confirmar=0 (ensayo con ROLLBACK). Respaldar las filas antes.
-- Luego @Confirmar=1 confirma exactamente el mismo alcance, revalidado bajo bloqueo.
-- Relevamiento: SERVER-ALFACORE, 26 cabeceras / 58 asientos / 27 aplicaciones,
-- fechas bancarias 14/09/2026 a 16/10/2026. No se deshabilitan triggers.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 15000;
DECLARE @Confirmar bit=0;
IF DB_NAME() NOT IN (N'ALFANET2007',N'ALFANET2007_PRUEBAS')
BEGIN RAISERROR(N'Base no autorizada para esta reparación.',16,1); RETURN; END;
IF @@TRANCOUNT<>0
BEGIN RAISERROR(N'Ejecutar sin otra transacción abierta.',16,1); RETURN; END;

BEGIN TRY
    BEGIN TRANSACTION;
    SELECT f.* INTO #Cabeceras
    FROM dbo.V_MV_CPTE f WITH(UPDLOCK,HOLDLOCK)
    WHERE f.TC=N'CB' AND f.IDCOMPLEMENTO=0 AND f.FECHA>='20260914' AND f.FECHA<'20261017'
      AND EXISTS(SELECT 1 FROM dbo.ALFACORE_COBRANZA_OPERACION o WHERE o.CobranzaId=f.ID AND o.Comprobante=f.IDCOMPROBANTE)
      AND (DATALENGTH(f.TC)<>DATALENGTH(RTRIM(f.TC)) OR DATALENGTH(f.CUENTA)<>DATALENGTH(RTRIM(f.CUENTA))
        OR EXISTS(SELECT 1 FROM dbo.MV_ASIENTOS a WHERE a.TC=f.TC AND a.SUCURSAL=f.SUCURSAL AND a.NUMERO=f.NUMERO AND a.LETRA=f.LETRA
            AND (DATALENGTH(a.TC)<>DATALENGTH(RTRIM(a.TC)) OR DATALENGTH(a.CUENTA)<>DATALENGTH(RTRIM(a.CUENTA))))
        OR EXISTS(SELECT 1 FROM dbo.MV_APLICACION a WHERE a.TC=f.TC AND a.IDCOMPROBANTE=f.IDCOMPROBANTE
            AND (DATALENGTH(a.TC)<>DATALENGTH(RTRIM(a.TC)) OR DATALENGTH(a.CUENTA)<>DATALENGTH(RTRIM(a.CUENTA))
                OR DATALENGTH(a.TC_PRINT)<>DATALENGTH(RTRIM(a.TC_PRINT)))));
    DECLARE @Cabeceras int=(SELECT COUNT(*) FROM #Cabeceras),@Asientos int,@Aplicaciones int;
    IF @Cabeceras=0
    BEGIN ROLLBACK; SELECT N'SIN_CAMBIOS' Resultado,N'No quedan CB del alcance con relleno.' Detalle; RETURN; END;
    SELECT a.* INTO #Asientos FROM dbo.MV_ASIENTOS a WITH(UPDLOCK,HOLDLOCK)
    WHERE EXISTS(SELECT 1 FROM #Cabeceras f WHERE a.TC=f.TC AND a.SUCURSAL=f.SUCURSAL AND a.NUMERO=f.NUMERO AND a.LETRA=f.LETRA);
    SELECT a.* INTO #Aplicaciones FROM dbo.MV_APLICACION a WITH(UPDLOCK,HOLDLOCK)
    WHERE EXISTS(SELECT 1 FROM #Cabeceras f WHERE a.TC=f.TC AND a.IDCOMPROBANTE=f.IDCOMPROBANTE);
    SELECT @Asientos=COUNT(*) FROM #Asientos;
    SELECT @Aplicaciones=COUNT(*) FROM #Aplicaciones;
    IF @Cabeceras<>26 OR @Asientos<>58 OR @Aplicaciones<>27
        RAISERROR(N'El alcance cambió: se esperaban 26 cabeceras, 58 asientos y 27 aplicaciones. Relevar antes de continuar.',16,1);

    UPDATE f SET TC=RTRIM(f.TC),CUENTA=RTRIM(f.CUENTA)
    FROM dbo.V_MV_CPTE f JOIN #Cabeceras s ON s.ID=f.ID;

    -- El trigger legacy de conciliaciones consulta INSERTED/DELETED por cuenta.
    -- Actualizar de a una fila evita subconsultas ambiguas al repetir una cuenta.
    DECLARE @Periodo nvarchar(10),@Mes int,@Asiento int,@Tipo nvarchar(20),@Secuencia int,@Cuenta nvarchar(15),@Tc nvarchar(4);
    DECLARE reparacion CURSOR LOCAL FAST_FORWARD FOR
        SELECT PERIODO,MES_OPERATIVO,[NUMERO ASIENTO],TIPO_REG,SECUENCIA,CUENTA,TC FROM #Asientos
        ORDER BY PERIODO,MES_OPERATIVO,[NUMERO ASIENTO],TIPO_REG,SECUENCIA,CUENTA;
    OPEN reparacion;
    FETCH NEXT FROM reparacion INTO @Periodo,@Mes,@Asiento,@Tipo,@Secuencia,@Cuenta,@Tc;
    WHILE @@FETCH_STATUS=0
    BEGIN
        UPDATE dbo.MV_ASIENTOS SET TC=RTRIM(TC),CUENTA=RTRIM(CUENTA)
        WHERE PERIODO=@Periodo AND MES_OPERATIVO=@Mes AND [NUMERO ASIENTO]=@Asiento AND TIPO_REG=@Tipo
            AND SECUENCIA=@Secuencia AND CUENTA=@Cuenta AND TC=@Tc;
        IF @@ROWCOUNT<>1 RAISERROR(N'La clave del asiento no identificó exactamente una fila.',16,1);
        FETCH NEXT FROM reparacion INTO @Periodo,@Mes,@Asiento,@Tipo,@Secuencia,@Cuenta,@Tc;
    END;
    CLOSE reparacion;
    DEALLOCATE reparacion;

    UPDATE a SET TC=RTRIM(a.TC),CUENTA=RTRIM(a.CUENTA),TC_PRINT=RTRIM(a.TC_PRINT)
    FROM dbo.MV_APLICACION a JOIN #Aplicaciones s ON s.ID=a.ID;

    IF (SELECT COUNT(*) FROM dbo.V_MV_CPTE f JOIN #Cabeceras s ON s.ID=f.ID)<>@Cabeceras
        RAISERROR(N'Cambió la cantidad de cabeceras.',16,1);
    IF (SELECT COUNT(*) FROM dbo.MV_APLICACION a JOIN #Aplicaciones s ON s.ID=a.ID)<>@Aplicaciones
        RAISERROR(N'Cambió la cantidad de aplicaciones.',16,1);
    IF EXISTS(SELECT 1 FROM dbo.V_MV_CPTE f JOIN #Cabeceras s ON s.ID=f.ID
        WHERE f.FECHA<>s.FECHA OR f.IMPORTE<>s.IMPORTE OR f.IDCOMPROBANTE<>s.IDCOMPROBANTE
            OR f.TC<>s.TC OR f.CUENTA<>s.CUENTA OR DATALENGTH(f.TC)<>DATALENGTH(RTRIM(f.TC))
            OR DATALENGTH(f.CUENTA)<>DATALENGTH(RTRIM(f.CUENTA))))
        RAISERROR(N'Falló la verificación de las cabeceras.',16,1);
    IF (SELECT COUNT(*) FROM dbo.MV_ASIENTOS a JOIN #Asientos s ON a.PERIODO=s.PERIODO AND a.MES_OPERATIVO=s.MES_OPERATIVO
        AND a.[NUMERO ASIENTO]=s.[NUMERO ASIENTO] AND a.TIPO_REG=s.TIPO_REG AND a.SECUENCIA=s.SECUENCIA AND a.CUENTA=s.CUENTA)<>58
        RAISERROR(N'Cambió la cantidad de renglones de asiento.',16,1);
    IF EXISTS(SELECT 1 FROM dbo.MV_ASIENTOS a JOIN #Asientos s ON a.PERIODO=s.PERIODO AND a.MES_OPERATIVO=s.MES_OPERATIVO
        AND a.[NUMERO ASIENTO]=s.[NUMERO ASIENTO] AND a.TIPO_REG=s.TIPO_REG AND a.SECUENCIA=s.SECUENCIA AND a.CUENTA=s.CUENTA
        WHERE a.FECHA<>s.FECHA OR a.IMPORTE<>s.IMPORTE OR a.[DEBE-HABER]<>s.[DEBE-HABER]
            OR a.TC<>s.TC OR a.SUCURSAL<>s.SUCURSAL OR a.NUMERO<>s.NUMERO OR a.LETRA<>s.LETRA
            OR DATALENGTH(a.TC)<>DATALENGTH(RTRIM(a.TC)) OR DATALENGTH(a.CUENTA)<>DATALENGTH(RTRIM(a.CUENTA))))
        RAISERROR(N'Falló la verificación de los asientos.',16,1);
    IF EXISTS(SELECT 1 FROM dbo.MV_APLICACION a JOIN #Aplicaciones s ON a.ID=s.ID
        WHERE a.IMPORTE<>s.IMPORTE OR a.IDCOMPROBANTE<>s.IDCOMPROBANTE OR a.IDCOMPROBANTE_ORIGEN<>s.IDCOMPROBANTE_ORIGEN
            OR a.TC<>s.TC OR a.CUENTA<>s.CUENTA OR a.TC_PRINT<>s.TC_PRINT
            OR DATALENGTH(a.TC)<>DATALENGTH(RTRIM(a.TC)) OR DATALENGTH(a.CUENTA)<>DATALENGTH(RTRIM(a.CUENTA))
            OR DATALENGTH(a.TC_PRINT)<>DATALENGTH(RTRIM(a.TC_PRINT))))
        RAISERROR(N'Falló la verificación de las aplicaciones.',16,1);

    IF @Confirmar=1 COMMIT TRANSACTION; ELSE ROLLBACK TRANSACTION;
    SELECT CASE WHEN @Confirmar=1 THEN N'CONFIRMADO' ELSE N'ENSAYO_REVERTIDO' END Resultado,
        @Cabeceras Cabeceras,@Asientos Asientos,@Aplicaciones Aplicaciones;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    DECLARE @Error nvarchar(2048)=ERROR_MESSAGE();
    RAISERROR(@Error,16,1);
END CATCH;
