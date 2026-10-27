/*
  Centro de avisos de la barra superior (campana).
  Los avisos se calculan al momento (calendario, reservas públicas, novedades); acá solo se guarda
  qué aviso marcó como leído cada usuario. La clave identifica el aviso (ej. "cal:123:aviso",
  "res:45"). Idempotente.
*/
IF OBJECT_ID(N'dbo.ALFACORE_AVISOS_LEIDOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_AVISOS_LEIDOS
    (
        Usuario nvarchar(80) NOT NULL,
        Clave nvarchar(120) NOT NULL,
        FechaHoraLeido datetime NOT NULL CONSTRAINT DF_ALFACORE_AVISOS_LEIDOS_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_ALFACORE_AVISOS_LEIDOS PRIMARY KEY CLUSTERED (Usuario, Clave)
    );
END;

IF OBJECT_ID(N'dbo.ALFACORE_AVISOS_LEIDOS', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ALFACORE_AVISOS_LEIDOS') AND name = N'IX_ALFACORE_AVISOS_LEIDOS_Fecha')
BEGIN
    CREATE NONCLUSTERED INDEX IX_ALFACORE_AVISOS_LEIDOS_Fecha ON dbo.ALFACORE_AVISOS_LEIDOS (FechaHoraLeido);
END;
