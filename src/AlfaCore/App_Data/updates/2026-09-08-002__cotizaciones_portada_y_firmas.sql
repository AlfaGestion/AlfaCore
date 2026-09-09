/*
    Cotizaciones - portada de propuesta e imagen de firma por usuario.
    - COT_VERSION.IncluyePortada: tilde por versión ("¿esta cotización lleva portada?"), la imagen
      de portada en sí vive en TA_LOGOS (IDLOGO='COT_PORTADA'), mismo patrón que el logo de empresa
      (ver ConfiguracionGeneralService) -- reutiliza la tabla existente, no crea una nueva.
    - MA_FIRMAS_USUARIO: la firma NO se guarda en el filesystem (a diferencia de la foto de perfil
      en Usuarios, ver UsuariosService.TryResolvePhotoPathAsync) porque el PDF público de una
      cotización (RenderPublicPdfAsync) resuelve la conexión de OTRO tenant por token, cruzando
      idbase -- si la firma viviera en disco local del server, un tenant en otra máquina física
      nunca la encontraría. Como blob en la propia base de cada tenant, viaja con la conexión
      resuelta igual que el logo.
    Idempotente.
*/

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.COT_VERSION') AND name = N'IncluyePortada'
)
BEGIN
    ALTER TABLE dbo.COT_VERSION
        ADD IncluyePortada bit NOT NULL CONSTRAINT DF_COT_VERSION_IncluyePortada DEFAULT (1);
END;
GO

IF OBJECT_ID(N'dbo.MA_FIRMAS_USUARIO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MA_FIRMAS_USUARIO
    (
        Usuario nvarchar(50) NOT NULL,
        Imagen varbinary(max) NOT NULL,
        FechaHoraModificacion datetime NOT NULL CONSTRAINT DF_MA_FIRMAS_USUARIO_FHM DEFAULT (GETDATE()),
        CONSTRAINT PK_MA_FIRMAS_USUARIO PRIMARY KEY CLUSTERED (Usuario)
    );
END;
GO
