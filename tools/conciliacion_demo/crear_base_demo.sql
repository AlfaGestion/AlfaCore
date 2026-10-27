-- Ejecutar conectado a master. Crea una base NUEVA; no reemplaza una existente.
SET NOCOUNT ON;
IF DB_NAME()<>N'master' OR CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))<>N'SERVER-ALFAVB6'
 THROW 51000,'Ejecutar únicamente en master de SERVER-ALFAVB6.',1;
IF DB_ID(N'ALFANET_DEMO') IS NOT NULL
 THROW 51000,'ALFANET_DEMO ya existe. No se reemplaza desde el script de creación.',1;
IF NOT EXISTS(SELECT 1 FROM sys.databases WHERE name=N'ALFANET' AND state_desc='ONLINE')
 THROW 51000,'La base origen ALFANET no está disponible.',1;
IF (SELECT COUNT(*) FROM sys.master_files WHERE database_id=DB_ID(N'ALFANET'))<>2
 THROW 51000,'La base origen cambió su distribución de archivos. Revisar antes de copiar.',1;
DECLARE @carpeta nvarchar(4000),@copia nvarchar(4000),@datos nvarchar(4000),@log nvarchar(4000),@nombreDatos nvarchar(128),@nombreLog nvarchar(128);
EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE',N'Software\Microsoft\MSSQLServer\MSSQLServer',N'BackupDirectory',@carpeta OUTPUT;
SELECT @nombreDatos=name FROM sys.master_files WHERE database_id=DB_ID(N'ALFANET') AND type=0;
SELECT @nombreLog=name FROM sys.master_files WHERE database_id=DB_ID(N'ALFANET') AND type=1;
SET @datos=CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultDataPath'))+N'ALFANET_DEMO.mdf';
SET @log=CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultLogPath'))+N'ALFANET_DEMO_log.ldf';
IF @carpeta IS NULL OR @datos IS NULL OR @log IS NULL OR @nombreDatos IS NULL OR @nombreLog IS NULL
 THROW 51000,'Faltan rutas o archivos esperados en el servidor.',1;
IF EXISTS(SELECT 1 FROM sys.master_files WHERE physical_name IN(@datos,@log))
 THROW 51000,'Uno de los archivos destino ya pertenece a una base. Se canceló la creación.',1;
DECLARE @existe int;
EXEC master.dbo.xp_fileexist @datos,@existe OUTPUT;
IF @existe=1 THROW 51000,'Ya existe el archivo físico de datos destino; no se sobrescribe.',1;
EXEC master.dbo.xp_fileexist @log,@existe OUTPUT;
IF @existe=1 THROW 51000,'Ya existe el archivo físico de log destino; no se sobrescribe.',1;
SET @copia=@carpeta+N'\ALFANET_PARA_DEMO_'+REPLACE(REPLACE(REPLACE(CONVERT(nvarchar(23),GETDATE(),126),'-',''),':',''),'.','')+N'.bak';
BACKUP DATABASE [ALFANET] TO DISK=@copia WITH COPY_ONLY,CHECKSUM;
RESTORE VERIFYONLY FROM DISK=@copia WITH CHECKSUM;
RESTORE DATABASE [ALFANET_DEMO] FROM DISK=@copia
 WITH MOVE @nombreDatos TO @datos,MOVE @nombreLog TO @log,RECOVERY,CHECKSUM;
ALTER DATABASE [ALFANET_DEMO] SET MULTI_USER;
SELECT N'ALFANET_DEMO' base_demo,@copia respaldo_origen,@datos archivo_datos,@log archivo_log FOR JSON PATH;
