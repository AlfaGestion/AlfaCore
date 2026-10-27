/*
    Informes → "Novedades" pasa a llamarse "Comunicados": ahí cada empresa escribe los avisos para su
    equipo. Las novedades de AlfaCore (notas de versión) se leen en /novedades (campana → "Ver todas
    las novedades") y no se editan desde ningún menú. Solo cambia el nombre visible; la ruta sigue
    siendo /informes/novedades. No se toca TA_MENU. Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB', N'U') IS NULL
    RETURN;
GO

IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB', N'NombreWeb') IS NOT NULL
    UPDATE dbo.ALFACORE_MENU_WEB
    SET NombreWeb = N'Comunicados'
    WHERE Menu = N'ALFA'
      AND RutaWeb = N'/informes/novedades'
      AND ISNULL(NombreWeb, N'') IN (N'', N'Novedades');
GO
