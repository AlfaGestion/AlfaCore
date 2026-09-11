/*
    Configuración General - alta en el menú web.
    Equivalente web de FrmAsistenteConfig.frm (solapas Datos empresa / Logo y Estilo). No hay
    estructura de tablas nueva: usa TA_CONFIGURACION, TA_LOGOS y TA_CONDIVA, que ya existen en
    toda base (son tablas legacy del sistema de escritorio).
    Nodo nuevo (sin equivalente propio en TA_MENU: el D9860 del escritorio apunta al Shell viejo,
    no a esta pantalla web), colgado del nodo real "Utilidades" (D98) para que aparezca ahí según
    lo pedido -- D98 ya existe en ALFACORE_MENU_WEB como ShellWorkspacePage en /shell/D98.
    Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.ALFACORE_MENU_WEB
    WHERE Menu = N'ALFA'
      AND Clave = N'DCONFIGGENERAL'
)
BEGIN
    INSERT INTO dbo.ALFACORE_MENU_WEB
    (
        Menu,
        Clave,
        RutaWeb,
        Componente,
        Icono,
        HabilitadoWeb,
        OrdenWeb,
        EsFavoritoDefault,
        Observacion,
        NombreWeb,
        DescripcionWeb,
        PadreClave
    )
    VALUES
    (
        N'ALFA',
        N'DCONFIGGENERAL',
        N'/configuracion-general',
        N'ConfiguracionGeneralEmpresa',
        N'bi-building-gear',
        1,
        98600,
        0,
        N'Datos de la empresa y logo/estilo de impresión de comprobantes.',
        N'Configuración General',
        N'Datos de la empresa (razón social, domicilio, CUIT, condición de IVA, etc.) y logo de impresión de comprobantes.',
        N'D98'
    );
END;
GO

-- Permisos: solo para usuarios que ya tienen filas explícitas en ALFACORE_TAREAS_WEB
-- (usuarios con restricciones activas); usuarios sin ninguna fila tienen acceso irrestricto.
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.ALFACORE_TAREAS_WEB (Usuario, Sistema, Clave, FechaHoraGrabacion, UsuarioGrabacion)
    SELECT DISTINCT t.Usuario, t.Sistema, N'DCONFIGGENERAL', GETDATE(), N'MIGRACION-CONFIGGENERAL'
    FROM dbo.ALFACORE_TAREAS_WEB t
    WHERE ISNULL(LTRIM(RTRIM(t.Clave)), N'') <> N''
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.ALFACORE_TAREAS_WEB x
          WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(t.Usuario)))
            AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(t.Sistema)))
            AND UPPER(LTRIM(RTRIM(x.Clave)))   = N'DCONFIGGENERAL'
      );
END;
GO
