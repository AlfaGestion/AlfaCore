/*
    2026-11-06 · Ver órdenes de pago (/compras/pagos/ordenes): lista las OPG y PGFP ya cargadas, con ver (trazabilidad, asiento e historial),
    reimpresión y mail. Opción web propia colgada de Compras (D50) con la clave D50-OPG-VER-WEB.
    Permisos: a los usuarios con permisos web explícitos se les da la opción nueva si ya tienen D509012 (Orden de Pago); los usuarios sin
    filas en ALFACORE_TAREAS_WEB ya ven todo el menú web. No escribe TA_MENU ni TA_TAREAS. Idempotente. Compatibility level 80/100.
*/
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'PadreClave') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD PadreClave nvarchar(50) NULL;

EXEC sys.sp_executesql N'
IF NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu = N''ALFA'' AND Clave = N''D50-OPG-VER-WEB'')
    INSERT INTO dbo.ALFACORE_MENU_WEB (Menu, Clave, RutaWeb, Componente, Icono, HabilitadoWeb, OrdenWeb, EsFavoritoDefault, NombreWeb)
    VALUES (N''ALFA'', N''D50-OPG-VER-WEB'', N''/compras/pagos/ordenes'', N''OrdenPagoVisor'', N''bi-card-list'', 1, 50405, 0, N''Ver órdenes de pago'');
UPDATE dbo.ALFACORE_MENU_WEB
SET RutaWeb = N''/compras/pagos/ordenes'', Componente = N''OrdenPagoVisor'', Icono = N''bi-card-list'', NombreWeb = N''Ver órdenes de pago'', HabilitadoWeb = 1,
    PadreClave = CASE WHEN EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Clave = N''D5090'') THEN N''D5090'' ELSE N''D50'' END
WHERE Clave = N''D50-OPG-VER-WEB'' AND EXISTS (SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Clave IN (N''D5090'', N''D50''));';

-- Permisos: quien ya tiene Orden de Pago (D509012) con permisos web explícitos recibe también esta opción
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(w.Usuario)), LTRIM(RTRIM(w.Sistema)), N'D50-OPG-VER-WEB', GETDATE(), N'SYSTEM'
    FROM dbo.ALFACORE_TAREAS_WEB w
    WHERE UPPER(LTRIM(RTRIM(ISNULL(w.Clave, N'')))) = N'D509012'
      AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
                      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(w.Usuario)))
                        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(w.Sistema)))
                        AND UPPER(LTRIM(RTRIM(x.Clave))) = N'D50-OPG-VER-WEB');
