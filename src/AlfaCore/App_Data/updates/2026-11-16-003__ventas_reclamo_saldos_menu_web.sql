-- Reclamo de saldos a clientes (Ventas > Reclamo de saldos). Reemplaza en la web a RP_SaldosDeClientes (D6008)
-- del legacy: elegir clientes, mensaje configurable con el detalle de la deuda, envío por email y WhatsApp.
--  * ALFACORE_RECLAMO_SALDO_ENVIO: bitácora de cada reclamo (canal, destino, estado) para no reclamar dos veces
--    seguidas y para mostrar "último reclamo" en el listado.
--  * Claves RECLAMO-SALDOS-* en TA_CONFIGURACION (grupo COBRANZAS), solo si faltan. No se pisa nada existente.
--  * Menú/permisos web propios (ALFACORE_MENU_WEB / ALFACORE_TAREAS_WEB); no escribe TA_MENU ni TA_TAREAS.
-- Idempotente. Compatible con compatibility level 100.
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID(N'dbo.ALFACORE_RECLAMO_SALDO_ENVIO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_RECLAMO_SALDO_ENVIO
    (
        IdEnvio       bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ALFACORE_RECLAMO_SALDO_ENVIO PRIMARY KEY,
        FechaHora     datetime NOT NULL CONSTRAINT DF_ALFACORE_RECLAMO_SALDO_ENVIO_Fecha DEFAULT (GETDATE()),
        Usuario       nvarchar(60) NOT NULL CONSTRAINT DF_ALFACORE_RECLAMO_SALDO_ENVIO_Usuario DEFAULT (N''),
        Cuenta        nvarchar(30) NOT NULL,
        Nombre        nvarchar(150) NOT NULL CONSTRAINT DF_ALFACORE_RECLAMO_SALDO_ENVIO_Nombre DEFAULT (N''),
        Canal         nvarchar(10) NOT NULL,            -- EMAIL | WHATSAPP
        Destino       nvarchar(300) NOT NULL CONSTRAINT DF_ALFACORE_RECLAMO_SALDO_ENVIO_Destino DEFAULT (N''),
        EsPrueba      bit NOT NULL CONSTRAINT DF_ALFACORE_RECLAMO_SALDO_ENVIO_Prueba DEFAULT (0),
        Tramo         tinyint NOT NULL CONSTRAINT DF_ALFACORE_RECLAMO_SALDO_ENVIO_Tramo DEFAULT (1),
        Saldo         decimal(18,2) NOT NULL CONSTRAINT DF_ALFACORE_RECLAMO_SALDO_ENVIO_Saldo DEFAULT (0),
        Vencido       decimal(18,2) NOT NULL CONSTRAINT DF_ALFACORE_RECLAMO_SALDO_ENVIO_Vencido DEFAULT (0),
        Comprobantes  int NOT NULL CONSTRAINT DF_ALFACORE_RECLAMO_SALDO_ENVIO_Cptes DEFAULT (0),
        Asunto        nvarchar(300) NULL,
        Estado        nvarchar(12) NOT NULL,            -- ENVIADO | ERROR
        Detalle       nvarchar(500) NULL
    );
    CREATE INDEX IX_ALFACORE_RECLAMO_SALDO_ENVIO_Cuenta ON dbo.ALFACORE_RECLAMO_SALDO_ENVIO (Cuenta, FechaHora DESC);
END
GO

-- Claves de configuración (grupo COBRANZAS). Valores >150 caracteres van a ValorAux.
DECLARE @cfg TABLE (Clave nvarchar(100), Valor nvarchar(150), Aux nvarchar(max), Descripcion nvarchar(50));
INSERT INTO @cfg VALUES
 (N'RECLAMO-SALDOS-DIAS-HASTA1', N'15', NULL, N'Reclamo saldos: tramo 1 hasta N dias'),
 (N'RECLAMO-SALDOS-DIAS-HASTA2', N'45', NULL, N'Reclamo saldos: tramo 2 hasta N dias'),
 (N'RECLAMO-SALDOS-ASUNTO1', N'Recordatorio de saldo pendiente - <NOMBRE>', NULL, N'Reclamo saldos: asunto tramo 1'),
 (N'RECLAMO-SALDOS-ASUNTO2', N'Saldo vencido en su cuenta - <NOMBRE>', NULL, N'Reclamo saldos: asunto tramo 2'),
 (N'RECLAMO-SALDOS-ASUNTO3', N'URGENTE: saldo vencido a regularizar - <NOMBRE>', NULL, N'Reclamo saldos: asunto tramo 3'),
 (N'RECLAMO-SALDOS-MENSAJE1', N'', N'Hola <NOMBRE>,' + NCHAR(13) + NCHAR(10) + NCHAR(13) + NCHAR(10)
    + N'Te recordamos que al <FECHA> tu cuenta registra comprobantes pendientes de pago. Si ya los abonaste, por favor ignorá este mensaje.' + NCHAR(13) + NCHAR(10) + NCHAR(13) + NCHAR(10)
    + N'<DETALLE>' + NCHAR(13) + NCHAR(10) + NCHAR(13) + NCHAR(10) + N'<FORMADEPAGO>' + NCHAR(13) + NCHAR(10) + NCHAR(13) + NCHAR(10) + N'Muchas gracias. Saludos.', N'Reclamo saldos: mensaje tramo 1'),
 (N'RECLAMO-SALDOS-MENSAJE2', N'', N'Hola <NOMBRE>,' + NCHAR(13) + NCHAR(10) + NCHAR(13) + NCHAR(10)
    + N'Tu cuenta tiene <VENCIDO> vencidos, con <DIAS> dias de atraso en el comprobante mas antiguo. Te pedimos regularizar el pago a la brevedad.' + NCHAR(13) + NCHAR(10) + NCHAR(13) + NCHAR(10)
    + N'<DETALLE>' + NCHAR(13) + NCHAR(10) + NCHAR(13) + NCHAR(10) + N'<FORMADEPAGO>' + NCHAR(13) + NCHAR(10) + NCHAR(13) + NCHAR(10) + N'Ante cualquier duda, respondé este mensaje. Saludos.', N'Reclamo saldos: mensaje tramo 2'),
 (N'RECLAMO-SALDOS-MENSAJE3', N'', N'Hola <NOMBRE>,' + NCHAR(13) + NCHAR(10) + NCHAR(13) + NCHAR(10)
    + N'Pese a nuestros avisos anteriores, tu cuenta mantiene <VENCIDO> vencidos hace <DIAS> dias. Necesitamos que regularices el saldo para poder seguir operando con normalidad.' + NCHAR(13) + NCHAR(10) + NCHAR(13) + NCHAR(10)
    + N'<DETALLE>' + NCHAR(13) + NCHAR(10) + NCHAR(13) + NCHAR(10) + N'<FORMADEPAGO>' + NCHAR(13) + NCHAR(10) + NCHAR(13) + NCHAR(10) + N'Si ya abonaste, enviános el comprobante de pago. Saludos.', N'Reclamo saldos: mensaje tramo 3'),
 (N'RECLAMO-SALDOS-FORMA-PAGO', N'', NULL, N'Reclamo saldos: forma de pago (<FORMADEPAGO>)'),
 (N'RECLAMO-SALDOS-SALDO-MINIMO', N'1', NULL, N'Reclamo saldos: saldo minimo a reclamar'),
 (N'RECLAMO-SALDOS-DIAS-ENTRE-ENVIOS', N'7', NULL, N'Reclamo saldos: dias entre reclamos'),
 (N'RECLAMO-SALDOS-WA-PLANTILLA', N'', NULL, N'Reclamo saldos: id plantilla WhatsApp');

INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, ValorAux, DESCRIPCION, FechaHora_Grabacion)
SELECT N'COBRANZAS', c.Clave, c.Valor, c.Aux, c.Descripcion, GETDATE()
FROM @cfg c
WHERE NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION t WHERE UPPER(LTRIM(RTRIM(t.CLAVE))) = c.Clave);
GO

-- Menú web (misma clave que el legacy: D6008). Permiso inicial: quienes ya la tienen en el legacy.
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;
IF COL_LENGTH(N'dbo.ALFACORE_MENU_WEB',N'NombreWeb') IS NULL
    ALTER TABLE dbo.ALFACORE_MENU_WEB ADD NombreWeb nvarchar(150) NULL;
GO
IF OBJECT_ID(N'dbo.ALFACORE_MENU_WEB',N'U') IS NULL RETURN;

EXEC sys.sp_executesql N'
INSERT INTO dbo.ALFACORE_MENU_WEB(Menu,Clave,PadreClave,NombreWeb,RutaWeb,Componente,Icono,HabilitadoWeb,OrdenWeb,EsFavoritoDefault)
SELECT N''ALFA'',N''D6008'',N''D60'',N''Reclamo de saldos'',N''/ventas/reclamo-saldos'',N''ReclamoSaldos'',N''bi-envelope-exclamation'',1,60008,0
WHERE NOT EXISTS(SELECT 1 FROM dbo.ALFACORE_MENU_WEB WHERE Menu=N''ALFA'' AND Clave=N''D6008'');
UPDATE dbo.ALFACORE_MENU_WEB
SET PadreClave=N''D60'',NombreWeb=N''Reclamo de saldos'',RutaWeb=N''/ventas/reclamo-saldos'',Componente=N''ReclamoSaldos'',
    Icono=N''bi-envelope-exclamation'',HabilitadoWeb=1,OrdenWeb=60008,
    DescripcionWeb=N''Reclamar saldos pendientes a clientes por email y WhatsApp, con el detalle de la deuda.''
WHERE Menu=N''ALFA'' AND Clave=N''D6008'';';

IF OBJECT_ID(N'dbo.ALFACORE_TAREAS_WEB',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TA_TAREAS',N'U') IS NOT NULL
    EXEC sys.sp_executesql N'
    INSERT INTO dbo.ALFACORE_TAREAS_WEB(Usuario,Sistema,Clave,FechaHoraGrabacion,UsuarioGrabacion)
    SELECT DISTINCT t.USUARIO,t.SISTEMA,N''D6008'',GETDATE(),N''MIGRACION-RECLAMO-SALDOS''
    FROM dbo.TA_TAREAS t WHERE UPPER(LTRIM(RTRIM(t.TAREA)))=N''D6008''
    AND NOT EXISTS(SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB x
        WHERE UPPER(LTRIM(RTRIM(x.Usuario)))=UPPER(LTRIM(RTRIM(t.USUARIO)))
          AND UPPER(LTRIM(RTRIM(x.Sistema)))=UPPER(LTRIM(RTRIM(t.SISTEMA)))
          AND UPPER(LTRIM(RTRIM(x.Clave)))=N''D6008'');';
GO
