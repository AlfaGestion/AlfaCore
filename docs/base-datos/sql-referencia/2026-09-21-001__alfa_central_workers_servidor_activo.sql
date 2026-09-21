SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

/*
    Catálogo genérico de configuración central (clave/valor), análogo a TA_CONFIGURACION pero a
    nivel ALFA_CENTRAL en vez de por base de cliente.

    Primer uso: WORKERS_SERVIDOR_ACTIVO -- cuando AlfaCore se despliega en más de un web service
    (para no depender de un solo servidor), todos los procesos siguen consultando esta misma
    ALFA_CENTRAL para saber qué bases están habilitadas para cada worker en segundo plano (Compra
    IA, Conversaciones, WhatsApp, recordatorios de prueba, facturación). Si cada instancia corriera
    esos workers de forma independiente, procesarían las mismas bases dos veces (respuestas
    duplicadas de WhatsApp, lectura de comprobantes duplicada, cargos de facturación duplicados).

    Esta clave designa qué servidor (identificado por AlfaCore:NombreServidor en su appsettings, o
    por Environment.MachineName si no se configuró) es el único autorizado a correr esos workers.
    Vacía o sin fila = sin restricción, todas las instancias corren los workers (comportamiento
    actual, para no romper instalaciones de un solo servidor). Ver IWorkerAssignmentService.
*/

IF OBJECT_ID(N'dbo.ConfiguracionCentral', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ConfiguracionCentral
    (
        Clave           nvarchar(100)  NOT NULL CONSTRAINT PK_ConfiguracionCentral PRIMARY KEY,
        Valor           nvarchar(400)  NULL,
        ModificadoUtc   datetime       NULL,
        ModificadoPor   nvarchar(100)  NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.ConfiguracionCentral WHERE Clave = N'WORKERS_SERVIDOR_ACTIVO')
    INSERT INTO dbo.ConfiguracionCentral (Clave, Valor) VALUES (N'WORKERS_SERVIDOR_ACTIVO', NULL);
