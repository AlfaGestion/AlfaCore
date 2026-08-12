-- ============================================================
-- Archivos > Tablas — editor genérico de tablas de referencia
-- (ver 2026-08-10-001__tablas_referencia_modulo.sql). No es un
-- módulo licenciable: no se registra MenuKeyRaiz en dbo.Modulos,
-- así que el filtro por módulos pagos no lo toca. Igual respeta
-- el sistema normal de permisos web (ALFACORE_TAREAS_WEB) — el
-- editor en sí ya exige SuperAdmin puertas adentro.
-- Clave web: D0105
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
    COALESCE((SELECT TOP (1) Menu FROM dbo.ALFACORE_MENU_WEB WHERE Clave = N'D01'), N'ALFA'),
    N'D0105',
    N'/archivos/tablas',
    N'TablasReferencia',
    N'bi-table',
    1,
    10500,
    0,
    N'Catálogos chicos de referencia (clasificaciones, categorías, etc.) que usan otras pantallas del sistema.',
    N'Tablas',
    N'D01'
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.ALFACORE_MENU_WEB x WHERE x.Clave = N'D0105'
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
        N'D0105',
        GETDATE(),
        N'MIGRACION-TABLAS-REFERENCIA'
    FROM dbo.ALFACORE_TAREAS_WEB tw
    WHERE UPPER(LTRIM(RTRIM(tw.Clave))) IN (N'D01', N'D0101')
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.ALFACORE_TAREAS_WEB x
          WHERE UPPER(LTRIM(RTRIM(x.Usuario))) = UPPER(LTRIM(RTRIM(tw.Usuario)))
            AND UPPER(LTRIM(RTRIM(x.Sistema))) = UPPER(LTRIM(RTRIM(tw.Sistema)))
            AND UPPER(LTRIM(RTRIM(x.Clave)))   = N'D0105'
      );
END;
GO
