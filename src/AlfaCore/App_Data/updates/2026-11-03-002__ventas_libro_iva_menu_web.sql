/*
    2026-11-03 · Libro de IVA Ventas en AlfaCore (/ventas/libro-iva).
    Usa la opción legacy D6009 «Reporte Libro de IVA Ventas» (mismos permisos de TA_TAREAS) y la muestra
    en el menú web de Ventas. Los permisos web (ALFACORE_TAREAS_WEB) se copian de los que cada usuario ya tiene
    en el legacy (TA_TAREAS): D6009 (libro) y D9882 (Fechas de Cierre, para cerrar ventas). No crea opciones
    nuevas en TA_MENU ni toca movimientos.
    Idempotente. Compatibility level 100.
*/
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;
IF OBJECT_ID(N'dbo.TA_MENU',N'U') IS NULL RETURN;
IF NOT EXISTS(SELECT 1 FROM dbo.TA_MENU WHERE Clave=N'D6009') RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
EXEC sys.sp_executesql N'
INSERT INTO dbo.ALFACORE_MENU_WEB(Menu,Clave,RutaWeb,Componente,Icono,HabilitadoWeb,OrdenWeb,EsFavoritoDefault,NombreWeb)
SELECT m.Menu,N''D6009'',N''/ventas/libro-iva'',N''LibroIvaVentas'',N''bi-journal-text'',1,60008,0,N''Libro de IVA Ventas''
FROM dbo.TA_MENU m WHERE m.Clave=N''D6009'' AND NOT EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB w WHERE w.Menu=m.Menu AND w.Clave=m.Clave);
UPDATE dbo.ALFACORE_MENU_WEB SET RutaWeb=N''/ventas/libro-iva'',Componente=N''LibroIvaVentas'',Icono=N''bi-journal-text'',NombreWeb=N''Libro de IVA Ventas'',HabilitadoWeb=1
WHERE Clave=N''D6009'';';
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'PadreClave') IS NOT NULL
    EXEC sys.sp_executesql N'UPDATE dbo.ALFACORE_MENU_WEB SET PadreClave=N''D60''
        WHERE Clave=N''D6009'' AND EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB p WHERE p.Clave=N''D60'');';
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TA_TAREAS',N'U') IS NOT NULL
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT LTRIM(RTRIM(t.USUARIO)), LTRIM(RTRIM(t.SISTEMA)), UPPER(LTRIM(RTRIM(t.TAREA))), GETDATE(), N'LIBRO-IVA-2026-11-03'
    FROM dbo.TA_TAREAS t
    WHERE UPPER(LTRIM(RTRIM(t.TAREA))) IN (N'D6009', N'D9882') AND ISNULL(LTRIM(RTRIM(t.USUARIO)),N'') <> N''
      AND NOT EXISTS (SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
                      WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.USUARIO)))
                        AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.SISTEMA)))
                        AND UPPER(LTRIM(RTRIM(x.Clave))) = UPPER(LTRIM(RTRIM(t.TAREA))));
