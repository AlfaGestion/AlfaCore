/*
  Registro de cambios del maestro de articulos (descripcion, rubro, marca, proveedor, IVA, unidades, suspendido, etc.).
  Los costos y precios ya quedan en V_MV_PreciosHis (triggers FMT_*): aca se registra todo lo demas.

  - ALFACORE_ARTICULOS_AUDITORIA: una fila por dato cambiado (Accion M), por alta (A) o por baja (B).
  - ALFACORE_TR_ARTICULOS_AUDITORIA: trigger AFTER INSERT, UPDATE, DELETE sobre V_MA_ARTICULOS. Se arma con las
    columnas que tiene V_MA_ARTICULOS en esta base, porque cambian de un cliente a otro.

  El trigger no frena grabaciones: todas las conversiones son explicitas y recortan, no hay columnas NOT NULL sin valor.
  Se puede volver a correr: recrea el trigger con las columnas actuales, respeta si estaba deshabilitado y no toca los registros.
  Para apagarlo en una base:  DISABLE TRIGGER dbo.ALFACORE_TR_ARTICULOS_AUDITORIA ON dbo.V_MA_ARTICULOS
  Lo consulta AlfaCore en Articulos > Historial de precios > Cambios del maestro.
*/
IF OBJECT_ID(N'dbo.V_MA_ARTICULOS', N'U') IS NULL
    RETURN;

IF OBJECT_ID(N'dbo.ALFACORE_ARTICULOS_AUDITORIA', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALFACORE_ARTICULOS_AUDITORIA (
        Id bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ALFACORE_ARTICULOS_AUDITORIA PRIMARY KEY,
        FechaHora datetime NOT NULL CONSTRAINT DF_ALFACORE_ARTICULOS_AUDITORIA_FECHA DEFAULT (GETDATE()),
        IdArticulo nvarchar(50) NOT NULL,
        Accion char(1) NOT NULL,
        Campo nvarchar(128) NULL,
        ValorAnterior nvarchar(1000) NULL,
        ValorNuevo nvarchar(1000) NULL,
        Usuario nvarchar(100) NULL,
        Programa nvarchar(128) NULL,
        Equipo nvarchar(128) NULL,
        LoginSql nvarchar(128) NULL
    );
    CREATE INDEX IX_ALFACORE_ARTICULOS_AUDITORIA_FECHA ON dbo.ALFACORE_ARTICULOS_AUDITORIA (FechaHora);
    CREATE INDEX IX_ALFACORE_ARTICULOS_AUDITORIA_ARTICULO ON dbo.ALFACORE_ARTICULOS_AUDITORIA (IdArticulo, FechaHora);
END
GO

IF OBJECT_ID(N'dbo.V_MA_ARTICULOS', N'U') IS NULL OR OBJECT_ID(N'dbo.ALFACORE_ARTICULOS_AUDITORIA', N'U') IS NULL
    RETURN;

DECLARE @Tabla int = OBJECT_ID(N'dbo.V_MA_ARTICULOS');
-- ID es identidad en las bases conocidas: permite registrar tambien el cambio de codigo. Si no, se une por codigo.
DECLARE @Clave sysname = CASE WHEN COLUMNPROPERTY(@Tabla, N'ID', 'IsIdentity') = 1 THEN N'ID' ELSE N'IDARTICULO' END;
DECLARE @Usuario nvarchar(200) = CASE WHEN COL_LENGTH(N'dbo.V_MA_ARTICULOS', N'Usuario') IS NULL THEN N'NULL' ELSE N'LEFT(NULLIF(LTRIM(RTRIM({x}.Usuario)), N''''), 100)' END;
-- En una modificacion el usuario vale solo si la grabacion lo escribio: un UPDATE que no toca Usuario (por ejemplo uno
-- masivo por SQL) deja el del ultimo que grabo. En ese caso queda vacio y se ve el equipo y el login.
DECLARE @UsuarioModif nvarchar(300) = CASE WHEN @Usuario = N'NULL' THEN N'NULL' ELSE N'CASE WHEN UPDATE(Usuario) THEN ' + @Usuario + N' END' END;
DECLARE @Descripcion nvarchar(200) = CASE WHEN COL_LENGTH(N'dbo.V_MA_ARTICULOS', N'DESCRIPCION') IS NULL THEN N'NULL' ELSE N'CONVERT(nvarchar(1000), {x}.DESCRIPCION)' END;

-- Una rama por columna: devuelve la fila solo si el valor cambio. Quedan afuera costo y precios (ya estan en
-- V_MV_PreciosHis), los campos que el sistema toca solo (usuario, marcas del trigger, fechas de costo, transmision)
-- y los tipos que no se pueden comparar (imagenes, binarios, texto largo).
DECLARE @Ramas nvarchar(max) = STUFF((
    SELECT N' UNION ALL ' + Rama
    FROM (
        SELECT c.column_id,
               N'SELECT N''' + REPLACE(c.name, N'''', N'''''') + N''', '
               + REPLACE(v.Valor, N'{x}', N'd') + N', ' + REPLACE(v.Valor, N'{x}', N'i')
               + N' WHERE ' + v.Distinto AS Rama
        FROM sys.columns c
        CROSS APPLY (SELECT TYPE_NAME(c.system_type_id) AS Tipo, QUOTENAME(c.name) AS Col) t
        CROSS APPLY (
            SELECT
                CASE
                    WHEN t.Tipo IN (N'char', N'varchar', N'nchar', N'nvarchar') THEN N'CONVERT(nvarchar(1000), {x}.' + t.Col + N')'
                    WHEN t.Tipo IN (N'float', N'real') THEN N'LTRIM(STR({x}.' + t.Col + N', 38, 6))'
                    WHEN t.Tipo IN (N'datetime', N'smalldatetime', N'date', N'datetime2', N'datetimeoffset') THEN N'CONVERT(nvarchar(30), {x}.' + t.Col + N', 120)'
                    ELSE N'CONVERT(nvarchar(60), {x}.' + t.Col + N')'
                END AS Valor,
                CASE
                    WHEN t.Tipo IN (N'char', N'varchar', N'nchar', N'nvarchar')
                        THEN N'ISNULL(d.' + t.Col + N', N'''') COLLATE Latin1_General_BIN <> ISNULL(i.' + t.Col + N', N'''') COLLATE Latin1_General_BIN'
                    WHEN t.Tipo IN (N'bit', N'tinyint', N'smallint', N'int', N'bigint', N'decimal', N'numeric', N'money', N'smallmoney', N'float', N'real')
                        THEN N'ISNULL(d.' + t.Col + N', 0) <> ISNULL(i.' + t.Col + N', 0)'
                    ELSE N'(d.' + t.Col + N' <> i.' + t.Col + N' OR (d.' + t.Col + N' IS NULL AND i.' + t.Col + N' IS NOT NULL) OR (d.' + t.Col + N' IS NOT NULL AND i.' + t.Col + N' IS NULL))'
                END AS Distinto
        ) v
        WHERE c.object_id = @Tabla
          AND c.is_computed = 0
          AND t.Tipo IN (N'char', N'varchar', N'nchar', N'nvarchar', N'bit', N'tinyint', N'smallint', N'int', N'bigint', N'decimal', N'numeric',
                         N'money', N'smallmoney', N'float', N'real', N'datetime', N'smalldatetime', N'date', N'datetime2', N'datetimeoffset', N'uniqueidentifier')
          AND UPPER(c.name) NOT IN (N'ID', N'USUARIO', N'DESDETRIGGER', N'FHULTIMOCOSTO', N'TRANSMISION', N'MODIFICOIMAGEN', N'COSTOINSUMOS',
                                    N'COSTO', N'IMPUESTOS', N'PRECIO1', N'PRECIO2', N'PRECIO3', N'PRECIO4', N'PRECIO5', N'PRECIO6', N'PRECIO7', N'PRECIO8')
          AND (@Clave = N'ID' OR UPPER(c.name) <> N'IDARTICULO')
    ) r
    ORDER BY r.column_id
    FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 11, N'');

IF @Ramas IS NULL
BEGIN
    RAISERROR('V_MA_ARTICULOS no tiene columnas para registrar.', 16, 1);
    RETURN;
END

DECLARE @Columnas nvarchar(400) = N'IdArticulo, Accion, Campo, ValorAnterior, ValorNuevo, Usuario, Programa, Equipo, LoginSql';
DECLARE @Contexto nvarchar(200) = N'@Programa, @Equipo, @Login';
DECLARE @Sql nvarchar(max) = N'CREATE TRIGGER dbo.ALFACORE_TR_ARTICULOS_AUDITORIA ON dbo.V_MA_ARTICULOS
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;
    DECLARE @Programa nvarchar(128), @Equipo nvarchar(128), @Login nvarchar(128);
    SELECT @Programa = LEFT(ISNULL(APP_NAME(), N''''), 128), @Equipo = LEFT(ISNULL(HOST_NAME(), N''''), 128), @Login = LEFT(ISNULL(SUSER_SNAME(), N''''), 128);

    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
        INSERT INTO dbo.ALFACORE_ARTICULOS_AUDITORIA (' + @Columnas + N')
        SELECT LEFT(ISNULL(i.IDARTICULO, N''''), 50), ''M'', v.Campo, v.Anterior, v.Nuevo, ' + REPLACE(@UsuarioModif, N'{x}', N'i') + N', ' + @Contexto + N'
        FROM inserted i
        INNER JOIN deleted d ON d.' + QUOTENAME(@Clave) + N' = i.' + QUOTENAME(@Clave) + N'
        CROSS APPLY (' + @Ramas + N') v (Campo, Anterior, Nuevo);

    INSERT INTO dbo.ALFACORE_ARTICULOS_AUDITORIA (' + @Columnas + N')
    SELECT LEFT(ISNULL(i.IDARTICULO, N''''), 50), ''A'', NULL, NULL, ' + REPLACE(@Descripcion, N'{x}', N'i') + N', ' + REPLACE(@Usuario, N'{x}', N'i') + N', ' + @Contexto + N'
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM deleted d WHERE d.' + QUOTENAME(@Clave) + N' = i.' + QUOTENAME(@Clave) + N');

    INSERT INTO dbo.ALFACORE_ARTICULOS_AUDITORIA (' + @Columnas + N')
    SELECT LEFT(ISNULL(d.IDARTICULO, N''''), 50), ''B'', NULL, ' + REPLACE(@Descripcion, N'{x}', N'd') + N', NULL, NULL, ' + @Contexto + N'
    FROM deleted d
    WHERE NOT EXISTS (SELECT 1 FROM inserted i WHERE i.' + QUOTENAME(@Clave) + N' = d.' + QUOTENAME(@Clave) + N');
END';

DECLARE @Deshabilitado bit = (SELECT is_disabled FROM sys.triggers WHERE object_id = OBJECT_ID(N'dbo.ALFACORE_TR_ARTICULOS_AUDITORIA'));
IF OBJECT_ID(N'dbo.ALFACORE_TR_ARTICULOS_AUDITORIA', N'TR') IS NOT NULL
    DROP TRIGGER dbo.ALFACORE_TR_ARTICULOS_AUDITORIA;
EXEC (@Sql);
IF @Deshabilitado = 1
    DISABLE TRIGGER dbo.ALFACORE_TR_ARTICULOS_AUDITORIA ON dbo.V_MA_ARTICULOS;
