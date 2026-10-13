/*
    Plantillas WhatsApp para avisar cancelaciones de reservas públicas.
*/

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.CONV_PLANTILLAS', N'U') IS NULL
    RETURN;

DECLARE @Ahora datetime = GETDATE();
DECLARE @UsuarioAccion nvarchar(50);
DECLARE @SistemaAccion nvarchar(50);
DECLARE @WabaId nvarchar(40);

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

IF OBJECT_ID(N'dbo.CONV_WHATSAPP_NUMEROS', N'U') IS NOT NULL
BEGIN
    SELECT TOP (1) @WabaId = NULLIF(LTRIM(RTRIM(WabaId)), N'')
    FROM dbo.CONV_WHATSAPP_NUMEROS
    WHERE ISNULL(Activo, 0) = 1
      AND NULLIF(LTRIM(RTRIM(WabaId)), N'') IS NOT NULL
    ORDER BY IdNumero;
END;

IF NULLIF(@UsuarioAccion, N'') IS NULL OR NULLIF(@SistemaAccion, N'') IS NULL
    RETURN;

IF NOT EXISTS (SELECT 1 FROM dbo.CONV_PLANTILLAS WHERE NombreMeta = N'capacitacion_reserva_cancelada_organizador' AND Idioma = N'es_AR')
BEGIN
    INSERT INTO dbo.CONV_PLANTILLAS
    (
        NombreVisible, NombreMeta, Categoria, Idioma, CuerpoTexto, PieTexto,
        EstadoLocal, EstadoMeta, Activa, WabaId, UsuarioAccion, SistemaAccion,
        FechaHora_Grabacion, FechaHora_Modificacion
    )
    VALUES
    (
        N'Capacitación cancelada - organizador',
        N'capacitacion_reserva_cancelada_organizador',
        N'UTILITY',
        N'es_AR',
        N'Hola {{1}}, se canceló una capacitación agendada.' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) +
        N'Cliente: {{2}}' + CHAR(13) + CHAR(10) +
        N'Fecha y hora: {{3}}' + CHAR(13) + CHAR(10) +
        N'Tipo de capacitación: {{4}}' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) +
        N'Revisá el calendario para ver el estado actualizado.',
        N'AlfaNet | Soluciones Informáticas',
        N'BORRADOR',
        N'DRAFT',
        1,
        @WabaId,
        @UsuarioAccion,
        @SistemaAccion,
        @Ahora,
        @Ahora
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.CONV_PLANTILLAS WHERE NombreMeta = N'capacitacion_reserva_cancelada_cliente' AND Idioma = N'es_AR')
BEGIN
    INSERT INTO dbo.CONV_PLANTILLAS
    (
        NombreVisible, NombreMeta, Categoria, Idioma, CuerpoTexto, PieTexto,
        EstadoLocal, EstadoMeta, Activa, WabaId, UsuarioAccion, SistemaAccion,
        FechaHora_Grabacion, FechaHora_Modificacion
    )
    VALUES
    (
        N'Capacitación cancelada - cliente',
        N'capacitacion_reserva_cancelada_cliente',
        N'UTILITY',
        N'es_AR',
        N'Hola {{1}}, tu capacitación del {{2}} fue cancelada.' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) +
        N'Tipo de capacitación: {{3}}' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) +
        N'Si necesitás coordinar otro horario, podés volver a reservar desde el enlace de capacitaciones.',
        N'AlfaNet | Soluciones Informáticas',
        N'BORRADOR',
        N'DRAFT',
        1,
        @WabaId,
        @UsuarioAccion,
        @SistemaAccion,
        @Ahora,
        @Ahora
    );
END;

IF @WabaId IS NOT NULL
BEGIN
    UPDATE dbo.CONV_PLANTILLAS
       SET WabaId = @WabaId,
           FechaHora_Modificacion = GETDATE()
     WHERE NombreMeta IN (N'capacitacion_reserva_cancelada_organizador', N'capacitacion_reserva_cancelada_cliente')
       AND Idioma = N'es_AR'
       AND NULLIF(LTRIM(RTRIM(ISNULL(WabaId, N''))), N'') IS NULL;
END;

IF OBJECT_ID(N'dbo.CONV_PLANTILLAS_VARIABLES', N'U') IS NOT NULL
BEGIN
    DECLARE @IdOrganizador bigint;
    DECLARE @IdCliente bigint;

    SELECT @IdOrganizador = IdPlantilla
    FROM dbo.CONV_PLANTILLAS
    WHERE NombreMeta = N'capacitacion_reserva_cancelada_organizador' AND Idioma = N'es_AR';

    SELECT @IdCliente = IdPlantilla
    FROM dbo.CONV_PLANTILLAS
    WHERE NombreMeta = N'capacitacion_reserva_cancelada_cliente' AND Idioma = N'es_AR';

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
            (@IdCliente, N'BODY', 3, N'capacitacion.tipo', @Ahora);
    END;
END;
