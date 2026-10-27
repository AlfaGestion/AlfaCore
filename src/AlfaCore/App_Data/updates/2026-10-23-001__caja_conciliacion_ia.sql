SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.BK_Conciliacion',N'U') IS NULL
    THROW 50001,'Instalá primero los vínculos de conciliación.',1;
IF OBJECT_ID(N'dbo.BK_ConciliacionIaAnalisis',N'U') IS NULL
    CREATE TABLE dbo.BK_ConciliacionIaAnalisis(
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        Usuario nvarchar(100) NOT NULL,Sesion uniqueidentifier NOT NULL,
        Cuenta nvarchar(15) NOT NULL,Modelo nvarchar(100) NOT NULL,
        ResultadoJson nvarchar(max) NOT NULL,
        FechaUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
IF NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-IA-MAX-FILAS')
    INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION)
    VALUES('CAJA','CONCILIACION-IA-MAX-FILAS','300',N'Máximo por lista para análisis IA (50 a 1000)');
IF NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-IA-MODELO')
    INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION)
    VALUES('CAJA','CONCILIACION-IA-MODELO','',N'Modelo IA; vacío usa el modelo del servidor');
