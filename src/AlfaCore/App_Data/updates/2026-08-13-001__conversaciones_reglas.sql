SET NOCOUNT ON;
GO

-- ============================================================
-- Conversaciones — Reglas / palabras clave (motor de reglas)
--
-- Cada regla vigila el texto de los mensajes ENTRANTES de cualquier
-- canal. Si coincide una palabra clave, aplica sus acciones:
--   * Responder con un texto (auto-respuesta).
--   * Derivar: asignar la conversación a un técnico.
--   * Etiquetar: fijar la prioridad de la conversación.
-- Se evalúan por Orden (menor primero); la primera que coincide y
-- tiene Detener=1 corta el resto (incluidas bienvenida/bot). Apagado
-- por defecto: sin filas cargadas, el motor no hace nada.
-- ============================================================

IF OBJECT_ID(N'dbo.CONV_REGLAS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CONV_REGLAS
    (
        IdRegla int IDENTITY(1, 1) NOT NULL,
        Nombre nvarchar(100) NOT NULL,
        Activa bit NOT NULL CONSTRAINT DF_CONV_REGLAS_Activa DEFAULT (1),
        Orden int NOT NULL CONSTRAINT DF_CONV_REGLAS_Orden DEFAULT (100),
        TipoCoincidencia nvarchar(20) NOT NULL CONSTRAINT DF_CONV_REGLAS_Tipo DEFAULT (N'CONTIENE'),
        Palabras nvarchar(1000) NOT NULL CONSTRAINT DF_CONV_REGLAS_Palabras DEFAULT (N''),
        Canal nvarchar(20) NOT NULL CONSTRAINT DF_CONV_REGLAS_Canal DEFAULT (N''),
        SoloSinAsignar bit NOT NULL CONSTRAINT DF_CONV_REGLAS_SoloSinAsignar DEFAULT (1),
        RespuestaTexto nvarchar(2000) NOT NULL CONSTRAINT DF_CONV_REGLAS_Respuesta DEFAULT (N''),
        AsignarTecnico nvarchar(4) NULL,
        Prioridad nvarchar(15) NULL,
        Detener bit NOT NULL CONSTRAINT DF_CONV_REGLAS_Detener DEFAULT (1),
        FechaAlta datetime NULL,
        UsuarioAlta nvarchar(50) NULL,
        FechaModificacion datetime NULL,
        UsuarioModificacion nvarchar(50) NULL,
        CONSTRAINT PK_CONV_REGLAS PRIMARY KEY CLUSTERED (IdRegla),
        CONSTRAINT CK_CONV_REGLAS_Tipo CHECK (TipoCoincidencia IN (N'CONTIENE', N'IGUAL', N'EMPIEZA')),
        CONSTRAINT CK_CONV_REGLAS_Prioridad CHECK (Prioridad IS NULL OR Prioridad IN (N'BAJA', N'MEDIA', N'ALTA', N'URGENTE'))
    );

    CREATE NONCLUSTERED INDEX IX_CONV_REGLAS_Activa_Orden
        ON dbo.CONV_REGLAS (Activa, Orden) INCLUDE (Canal);
END;
GO
