/*
Modelo inicial para el control centralizado de backups de clientes.

Esta tabla vive en ALFA_CENTRAL (no en la base de cada cliente), junto a dbo.bases.
La alimenta el endpoint POST /api/vb6/backup-status, que reemplaza al linked server
que ModBackup.bas (EM_Backup.vbp) usaba antes para escribir directo en la base de
control de cada cliente.

IdBase queda como vinculo logico a dbo.bases(id) (resuelto en el servicio C# por
IdCliente + DbServidor + DbNombre), sin FOREIGN KEY dura: no se confirmo el tipo
exacto de dbo.bases.id contra este script, asi que se prefiere no asumirlo. Si se
confirma el tipo, se puede agregar la FK en un script de actualizacion aparte.

Ejecutar manualmente contra ALFA_CENTRAL (no forma parte de App_Data/updates, que
se aplica por cliente contra ConnectionStrings:AlfaGestion).
*/

SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    IF OBJECT_ID(N'dbo.ALFACORE_BACKUPS_CONTROL', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ALFACORE_BACKUPS_CONTROL
        (
            IdControl bigint IDENTITY(1,1) NOT NULL,
            IdCliente nvarchar(50) NOT NULL,
            IdBase int NULL,
            DbServidor nvarchar(150) NOT NULL,
            DbNombre nvarchar(150) NOT NULL,
            TipoBackup nvarchar(20) NOT NULL,
            Resultado nvarchar(20) NOT NULL,
            Mensaje nvarchar(500) NULL,
            HostCliente nvarchar(150) NULL,
            UsuarioSql nvarchar(150) NULL,
            InstanciaSql nvarchar(150) NULL,
            VersionSql nvarchar(100) NULL,
            SistemaOperativo nvarchar(100) NULL,
            TamanioBaseMB decimal(18,2) NULL,
            EspacioLibreDiscoGB decimal(18,2) NULL,
            EspacioTotalDiscoGB decimal(18,2) NULL,
            EspacioLibreDiscoBckGB decimal(18,2) NULL,
            EspacioTotalDiscoBckGB decimal(18,2) NULL,
            FechaHoraBackup datetime NOT NULL,
            FechaHoraRecepcion datetime NOT NULL CONSTRAINT DF_ALFACORE_BACKUPS_CONTROL_Recepcion DEFAULT (GETDATE()),
            CONSTRAINT PK_ALFACORE_BACKUPS_CONTROL PRIMARY KEY CLUSTERED (IdControl ASC),
            CONSTRAINT CK_ALFACORE_BACKUPS_CONTROL_Resultado CHECK (Resultado IN (N'OK', N'ERROR', N'ADVERTENCIA')),
            CONSTRAINT CK_ALFACORE_BACKUPS_CONTROL_Tipo CHECK (TipoBackup IN (N'COMPLETO', N'DIFERENCIAL'))
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALFACORE_BACKUPS_CONTROL_CLIENTE' AND object_id = OBJECT_ID(N'dbo.ALFACORE_BACKUPS_CONTROL'))
        CREATE INDEX IX_ALFACORE_BACKUPS_CONTROL_CLIENTE ON dbo.ALFACORE_BACKUPS_CONTROL (IdCliente, FechaHoraRecepcion DESC);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALFACORE_BACKUPS_CONTROL_RESULTADO' AND object_id = OBJECT_ID(N'dbo.ALFACORE_BACKUPS_CONTROL'))
        CREATE INDEX IX_ALFACORE_BACKUPS_CONTROL_RESULTADO ON dbo.ALFACORE_BACKUPS_CONTROL (Resultado, FechaHoraRecepcion DESC);

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH;
