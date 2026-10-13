/*
    Recordatorios automáticos de Calendario y plantillas base para reservas públicas
    de capacitaciones online.
*/

IF OBJECT_ID(N'dbo.CONV_PLANTILLAS', N'U') IS NULL
    RETURN;

DECLARE @Ahora datetime = GETDATE();
DECLARE @UsuarioAccion nvarchar(50);
DECLARE @SistemaAccion nvarchar(50);

IF OBJECT_ID(N'dbo.TA_USUARIOS', N'U') IS NOT NULL
BEGIN
    SELECT TOP (1)
        @UsuarioAccion = LTRIM(RTRIM(NOMBRE)),
        @SistemaAccion = LTRIM(RTRIM(SISTEMA))
    FROM dbo.TA_USUARIOS
    WHERE NULLIF(LTRIM(RTRIM(NOMBRE)), N'') IS NOT NULL
      AND NULLIF(LTRIM(RTRIM(SISTEMA)), N'') IS NOT NULL
    ORDER BY
        CASE
            WHEN UPPER(LTRIM(RTRIM(NOMBRE))) IN (N'ALFACORE', N'ADMIN', N'ADMINISTRADOR') THEN 0
            ELSE 1
        END,
        NOMBRE;
END;

IF NULLIF(@UsuarioAccion, N'') IS NULL OR NULLIF(@SistemaAccion, N'') IS NULL
    RETURN;

IF NOT EXISTS (SELECT 1 FROM dbo.CONV_PLANTILLAS WHERE NombreMeta = N'capacitacion_reserva_organizador' AND Idioma = N'es_AR')
BEGIN
    INSERT INTO dbo.CONV_PLANTILLAS
    (
        NombreVisible, NombreMeta, Categoria, Idioma, CuerpoTexto, PieTexto,
        EstadoLocal, EstadoMeta, Activa, UsuarioAccion, SistemaAccion,
        FechaHora_Grabacion, FechaHora_Modificacion
    )
    VALUES
    (
        N'Nueva capacitación agendada',
        N'capacitacion_reserva_organizador',
        N'UTILITY',
        N'es_AR',
        N'Hola {{1}}, tenés una nueva capacitación agendada.' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) +
        N'Cliente: {{2}}' + CHAR(13) + CHAR(10) +
        N'Fecha y hora: {{3}}' + CHAR(13) + CHAR(10) +
        N'Tipo de capacitación: {{4}}' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) +
        N'Revisá el calendario y preparate con anticipación.',
        N'AlfaNet | Soluciones Informáticas',
        N'BORRADOR',
        N'DRAFT',
        1,
        @UsuarioAccion,
        @SistemaAccion,
        @Ahora,
        @Ahora
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.CONV_PLANTILLAS WHERE NombreMeta = N'capacitacion_reserva_cliente' AND Idioma = N'es_AR')
BEGIN
    INSERT INTO dbo.CONV_PLANTILLAS
    (
        NombreVisible, NombreMeta, Categoria, Idioma, CuerpoTexto, PieTexto,
        EstadoLocal, EstadoMeta, Activa, UsuarioAccion, SistemaAccion,
        FechaHora_Grabacion, FechaHora_Modificacion
    )
    VALUES
    (
        N'Confirmación de capacitación online',
        N'capacitacion_reserva_cliente',
        N'UTILITY',
        N'es_AR',
        N'Hola {{1}}, tu capacitación ya fue reservada.' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) +
        N'Fecha y hora: {{2}}' + CHAR(13) + CHAR(10) +
        N'Responsable: {{3}}' + CHAR(13) + CHAR(10) +
        N'Tipo de capacitación: {{4}}' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) +
        N'Nos pondremos en contacto el día de la fecha para coordinar la reunión online.',
        N'AlfaNet | Soluciones Informáticas',
        N'BORRADOR',
        N'DRAFT',
        1,
        @UsuarioAccion,
        @SistemaAccion,
        @Ahora,
        @Ahora
    );
END;

IF OBJECT_ID(N'dbo.CONV_PLANTILLAS_VARIABLES', N'U') IS NOT NULL
BEGIN
    DECLARE @IdOrganizador bigint;
    DECLARE @IdCliente bigint;

    SELECT @IdOrganizador = IdPlantilla
    FROM dbo.CONV_PLANTILLAS
    WHERE NombreMeta = N'capacitacion_reserva_organizador' AND Idioma = N'es_AR';

    SELECT @IdCliente = IdPlantilla
    FROM dbo.CONV_PLANTILLAS
    WHERE NombreMeta = N'capacitacion_reserva_cliente' AND Idioma = N'es_AR';

    IF @IdOrganizador IS NOT NULL
    BEGIN
        DELETE FROM dbo.CONV_PLANTILLAS_VARIABLES
        WHERE IdPlantilla = @IdOrganizador AND Componente = N'BODY';

        INSERT INTO dbo.CONV_PLANTILLAS_VARIABLES (IdPlantilla, Componente, Posicion, VariableKey, FechaHora_Grabacion)
        VALUES
            (@IdOrganizador, N'BODY', 1, N'capacitacion.tecnico', @Ahora),
            (@IdOrganizador, N'BODY', 2, N'capacitacion.cliente', @Ahora),
            (@IdOrganizador, N'BODY', 3, N'capacitacion.fechaHora', @Ahora),
            (@IdOrganizador, N'BODY', 4, N'capacitacion.tipo', @Ahora);
    END;

    IF @IdCliente IS NOT NULL
    BEGIN
        DELETE FROM dbo.CONV_PLANTILLAS_VARIABLES
        WHERE IdPlantilla = @IdCliente AND Componente = N'BODY';

        INSERT INTO dbo.CONV_PLANTILLAS_VARIABLES (IdPlantilla, Componente, Posicion, VariableKey, FechaHora_Grabacion)
        VALUES
            (@IdCliente, N'BODY', 1, N'capacitacion.cliente', @Ahora),
            (@IdCliente, N'BODY', 2, N'capacitacion.fechaHora', @Ahora),
            (@IdCliente, N'BODY', 3, N'capacitacion.tecnico', @Ahora),
            (@IdCliente, N'BODY', 4, N'capacitacion.tipo', @Ahora);
    END;
END;
