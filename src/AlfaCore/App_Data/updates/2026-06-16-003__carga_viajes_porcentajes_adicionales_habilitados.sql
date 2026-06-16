/*
    Seed de configuración para Carga de Viajes.
    Habilita la cantidad de porcentajes adicionales visibles/usados en la carga del viaje.
*/

IF OBJECT_ID(N'dbo.TA_CONFIGURACION', N'U') IS NULL
BEGIN
    RETURN;
END;

DECLARE @Clave nvarchar(50) = N'VIAJES-PORCENTAJES-ADICIONALES-HABILITADOS';
DECLARE @Grupo nvarchar(50) = N'VIAJES';
DECLARE @Descripcion nvarchar(150) = N'Cantidad de porcentajes adicionales habilitados en carga de viajes';
DECLARE @Valor nvarchar(150) = N'3';

IF EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = UPPER(@Clave))
BEGIN
    UPDATE dbo.TA_CONFIGURACION
    SET
        GRUPO = @Grupo,
        VALOR = @Valor,
        DESCRIPCION = @Descripcion,
        FechaHora_Modificacion = GETDATE()
    WHERE UPPER(LTRIM(RTRIM(CLAVE))) = UPPER(@Clave);
END
ELSE
BEGIN
    INSERT INTO dbo.TA_CONFIGURACION
    (
        GRUPO,
        CLAVE,
        VALOR,
        DESCRIPCION,
        FechaHora_Grabacion,
        FechaHora_Modificacion
    )
    VALUES
    (
        @Grupo,
        @Clave,
        @Valor,
        @Descripcion,
        GETDATE(),
        GETDATE()
    );
END;
