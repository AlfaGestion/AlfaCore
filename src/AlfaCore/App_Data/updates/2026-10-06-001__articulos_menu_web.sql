-- ============================================================
-- Archivos > Maestros > Artículos -- nuevo módulo web (MVP), ver plan en
-- C:\Users\albert\.claude\plans\fluttering-drifting-moonbeam.md ("Módulo Archivos > Maestros >
-- Artículos"). Mismo patrón de alta de menú que Clientes (D010105) / Proveedores (D010110), bajo el
-- mismo padre "Maestros" (D0101).
-- Clave web: D010115
-- ============================================================

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
BEGIN
    RETURN;
END;
GO

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
    PadreClave
)
SELECT
    COALESCE((SELECT TOP (1) Menu FROM dbo.ALFACORE_MENU_WEB WHERE Clave = N'D0101'), N'ALFA'),
    N'D010115',
    N'/articulos',
    N'Articulos',
    N'bi-box-seam',
    1,
    10115,
    1,
    N'ABM web de artículos (MVP: datos básicos + un precio de venta, ver Ma_art.frm/FrmArtAlta.frm en la carpeta de migración legacy para el alcance completo pendiente).',
    N'Artículos',
    N'D0101'
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.ALFACORE_MENU_WEB x WHERE x.Clave = N'D010115'
);
GO

-- Hereda el permiso de quien ya tenga acceso a Archivos o a Maestros.
IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.ALFACORE_TAREAS_WEB
    (
        Usuario,
        Sistema,
        Clave,
        FechaHoraGrabacion,
        UsuarioGrabacion
    )
    SELECT DISTINCT
        tw.Usuario,
        tw.Sistema,
        N'D010115',
        GETDATE(),
        N'MIGRACION-ARTICULOS'
    FROM dbo.ALFACORE_TAREAS_WEB tw
    WHERE UPPER(LTRIM(RTRIM(tw.Clave))) IN (N'D01', N'D0101')
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.ALFACORE_TAREAS_WEB x
          WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(tw.Usuario)))
            AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(tw.Sistema)))
            AND UPPER(LTRIM(RTRIM(x.Clave)))   = N'D010115'
      );
END;
GO

-- Claves de TA_CONFIGURACION que usa el maestro de Artículos (MVP) -- en una instalación con el
-- sistema de escritorio ya configurado estas claves casi seguro ya existen con valores reales; acá
-- sólo se agregan si faltan (instalación nueva), nunca se pisa un valor existente.
IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NOT NULL
BEGIN
    ;WITH ClavesArticulos AS
    (
        SELECT * FROM (VALUES
            (N'PIVA', N'21', N'Alícuota de IVA por defecto cuando un artículo no tiene tasa propia.'),
            (N'ClasePrecioVenta', N'1', N'Clase de precio (1-8) que Artículos usa como precio de venta.'),
            (N'MaestroArticuloConIVA', N'NO', N'Si el precio de venta se carga con IVA incluido.'),
            (N'Clase2ConIVA', N'NO', N'Si la clase de precio 2 se carga con IVA incluido.'),
            (N'RETAIL', N'NO', N'Modo retail (afecta edición directa de precio y costo como precio de lista de proveedor).'),
            (N'TIPOEAN', N'', N'Plantilla de dígitos variables del código de barras para productos pesables.'),
            (N'CodigoBarraAutomatico', N'NO', N'Autogenera el código de barras a partir del id interno del artículo.'),
            (N'RutaImagenes', N'Imagenes\ImagenesWeb\', N'Carpeta base de imágenes de artículos.')
        ) AS v(Clave, Valor, Descripcion)
    )
    INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
    SELECT N'ARTICULOS', c.Clave, c.Valor, c.Descripcion, GETDATE()
    FROM ClavesArticulos c
    WHERE NOT EXISTS
    (
        SELECT 1 FROM dbo.TA_CONFIGURACION x WHERE UPPER(LTRIM(RTRIM(x.CLAVE))) = UPPER(c.Clave)
    );
END;
GO
