/*
  Control ARCA: crea AUX_MV_CTROL_CPTE (una fila por comprobante que no coincide con ARCA, ligada a
  MV_Transmisiones.Transmision) cuando la base no la tiene (bases que nunca corrieron el legacy).
  Misma estructura que el legacy. Compatible con SQL 2008 (nivel 100).
*/
IF OBJECT_ID(N'dbo.AUX_MV_CTROL_CPTE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AUX_MV_CTROL_CPTE
    (
        id          int IDENTITY(1,1) NOT NULL,
        TC          nvarchar(4)   NULL,
        SUCURSAL    nvarchar(4)   NULL,
        NUMERO      nvarchar(8)   NULL,
        LETRA       nvarchar(1)   NULL,
        IMPORTE     money         NULL,
        CUENTA      nvarchar(15)  NULL,
        NOMBRE      nvarchar(150) NULL,
        FECHA       datetime      NULL,
        MENSAJE     nvarchar(250) NULL,
        TRANSMISION int           NOT NULL,
        CONSTRAINT PK_AUX_MV_CTROL_CPTE PRIMARY KEY CLUSTERED (id)
    );
END
