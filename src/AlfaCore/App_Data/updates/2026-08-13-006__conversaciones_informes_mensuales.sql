SET NOCOUNT ON;
GO

-- ============================================================
-- Conversaciones — Informes mensuales de atención por cliente
--
-- Un informe por mes (cabecera) con un detalle por cliente (o por contacto
-- si no tiene cliente asociado). Se genera una vez y queda guardado.
-- Fase 1: totales (conversaciones, días, mensajes, minutos de partes de
-- horas total/facturable) + datos del cliente (clasificación, categoría,
-- estado de abono). Fase 2 (después): resumen IA por fila + envío.
--
-- Sin FOREIGN KEY físicas (mismo criterio que el resto del módulo);
-- la relación cabecera-detalle se valida en la aplicación.
-- ============================================================

IF OBJECT_ID(N'dbo.CONV_INFORME_MENSUAL', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_INFORME_MENSUAL
    (
        IdInforme int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CONV_INFORME_MENSUAL PRIMARY KEY,
        Anio int NOT NULL,
        Mes int NOT NULL,
        FechaGeneracion datetime NOT NULL CONSTRAINT DF_CONV_INFORME_MENSUAL_Fecha DEFAULT (GETDATE()),
        UsuarioGeneracion nvarchar(50) NULL,
        Estado nvarchar(20) NOT NULL CONSTRAINT DF_CONV_INFORME_MENSUAL_Estado DEFAULT (N'GENERADO')
    );

    -- Un solo informe por período (regenerar reemplaza).
    CREATE UNIQUE INDEX UX_CONV_INFORME_MENSUAL_Periodo
        ON dbo.CONV_INFORME_MENSUAL (Anio, Mes);
END;
GO

IF OBJECT_ID(N'dbo.CONV_INFORME_MENSUAL_DET', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_INFORME_MENSUAL_DET
    (
        IdDetalle int IDENTITY(1,1) NOT NULL CONSTRAINT PK_CONV_INFORME_MENSUAL_DET PRIMARY KEY,
        IdInforme int NOT NULL,

        -- CLIENTE | CONTACTO | SININDENT (sin cliente ni contacto identificado)
        TipoFila nvarchar(12) NOT NULL CONSTRAINT DF_CONV_INFORME_MENSUAL_DET_Tipo DEFAULT (N'CLIENTE'),
        ClienteCodigo nvarchar(20) NULL,
        IdContacto int NULL,
        NombreMostrar nvarchar(200) NOT NULL CONSTRAINT DF_CONV_INFORME_MENSUAL_DET_Nombre DEFAULT (N''),

        ClasificacionCodigo nvarchar(10) NULL,
        ClasificacionDesc nvarchar(100) NULL,
        CategoriaDesc nvarchar(100) NULL,
        -- Abonado | Suspendido | SinAbono
        EstadoAbono nvarchar(20) NOT NULL CONSTRAINT DF_CONV_INFORME_MENSUAL_DET_Abono DEFAULT (N'SinAbono'),

        CantConversaciones int NOT NULL CONSTRAINT DF_CONV_INFORME_MENSUAL_DET_Conv DEFAULT (0),
        CantDias int NOT NULL CONSTRAINT DF_CONV_INFORME_MENSUAL_DET_Dias DEFAULT (0),
        CantMensajes int NOT NULL CONSTRAINT DF_CONV_INFORME_MENSUAL_DET_Msj DEFAULT (0),
        CantMensajesEntrantes int NOT NULL CONSTRAINT DF_CONV_INFORME_MENSUAL_DET_MsjIn DEFAULT (0),
        CantMensajesSalientes int NOT NULL CONSTRAINT DF_CONV_INFORME_MENSUAL_DET_MsjOut DEFAULT (0),
        MinutosTotales int NOT NULL CONSTRAINT DF_CONV_INFORME_MENSUAL_DET_Min DEFAULT (0),
        MinutosFacturables int NOT NULL CONSTRAINT DF_CONV_INFORME_MENSUAL_DET_MinFact DEFAULT (0),

        -- Fase 2: resumen IA (borrador) + editado por el operador + estado de envío.
        ResumenBorrador nvarchar(max) NULL,
        ResumenEditado nvarchar(max) NULL,
        ResumenGeneradoIA bit NOT NULL CONSTRAINT DF_CONV_INFORME_MENSUAL_DET_ResIA DEFAULT (0),
        EstadoEnvio nvarchar(20) NOT NULL CONSTRAINT DF_CONV_INFORME_MENSUAL_DET_Envio DEFAULT (N'PENDIENTE'),
        FechaEnvio datetime NULL,
        CanalEnvio nvarchar(20) NULL
    );

    CREATE INDEX IX_CONV_INFORME_MENSUAL_DET_Informe
        ON dbo.CONV_INFORME_MENSUAL_DET (IdInforme);
END;
GO
