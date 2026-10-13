/*
    Las novedades de AlfaCore (notas de versión) dejaron de copiarse a la base de cada empresa: viven
    en el código (NovedadesSistemaCatalogo + App_Data/novedades-sistema/) y se leen en /novedades.
    2026-10-13-005 llegó a cargar "AlfaCore 1.0" como comunicado en alguna base antes de ese cambio;
    acá se borra esa copia (y sus lecturas) para que no aparezca dos veces. Solo toca la fila que
    cargó el sistema. Idempotente.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.ALFACORE_NOVEDADES', N'U') IS NULL
    RETURN;
GO

IF OBJECT_ID(N'dbo.ALFACORE_NOVEDADES_LECTURAS', N'U') IS NOT NULL
    DELETE l
    FROM dbo.ALFACORE_NOVEDADES_LECTURAS l
    INNER JOIN dbo.ALFACORE_NOVEDADES n ON n.IdNovedad = l.IdNovedad
    WHERE n.Version = N'AlfaCore 1.0'
      AND n.UsuarioAlta = N'SYSTEM';
GO

DELETE FROM dbo.ALFACORE_NOVEDADES
WHERE Version = N'AlfaCore 1.0'
  AND UsuarioAlta = N'SYSTEM';
GO
