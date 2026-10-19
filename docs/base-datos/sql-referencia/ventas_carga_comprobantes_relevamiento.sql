-- Relevamiento de metadatos de la base seleccionada por el operador.
-- No ejecuta procedimientos comerciales ni modifica objetos/datos persistentes.
-- No seleccionar automáticamente la base desde credenciales de un VBP.
-- Requiere visibilidad de metadatos (VIEW DEFINITION) para resultados completos.
-- Compatible con catálogo SQL Server 2008+; revisar motor concreto antes de usar.
-- Una definición NULL puede significar permisos insuficientes u objeto cifrado.

SET NOCOUNT ON;

DECLARE @Objetos TABLE (Nombre sysname PRIMARY KEY);
INSERT INTO @Objetos (Nombre)
VALUES
    (N'V_MV_CPTE'), (N'V_MV_CPTEINSUMOS'), (N'V_MV_CPTETAREAS'),
    (N'V_MV_CPTE_OBSERV'), (N'MV_ASIENTOS'), (N'MV_APLICACION'),
    (N'V_MV_STOCK'), (N'V_TA_CPTE'), (N'VT_CLIENTES'),
    (N'V_MA_ARTICULOS'), (N'V_MA_PRECIOS'), (N'V_MA_PRECIOSCAB'),
    (N'V_TA_UNIDAD'), (N'V_TA_CPRA_VTA'), (N'V_TA_MOTIVOVTA'),
    (N'V_TA_MOTIVOSTOCK'), (N'V_TA_DEPOSITO'), (N'V_TA_VENDEDORES'),
    (N'V_TA_UNIDADNEGOCIO'), (N'TA_CONFIGURACION'), (N'TA_USUARIOS'),
    (N'TA_CUENTASIVA'), (N'TA_COTIZACION'), (N'MA_CUENTAS'),
    (N'MA_CUENTASADIC'), (N'MA_CUENTASSUC'), (N'V_TA_INPUT_VENTAS'),
    (N'V_MV_CPTEACCIONES'), (N'V_MV_CTROL_CPTE'),
    (N'V_MV_CPTE_ELECTRONICOS'), (N'V_MV_CPTE_ELECTRONICOS_EXPORTACION'),
    (N'V_MV_RESERVAS'), (N'MV_PREPARACION'), (N'MV_PREPARACION_DETALLES'),
    (N'MV_ASIENTOSCCOSTO'), (N'MA_CLAVEPIN'), (N'AUX_ERR'),
    (N'NW_ComprobantesVencidos'), (N'sp_web_Alta_Comprobante'),
    (N'sp_web_CpteInsumos'), (N'sp_web_CreaAsientoFactura'),
    (N'sp_web_CreaCobPorFactura'), (N'sp_web_creaLineaAsiento'),
    (N'sp_web_CreaAplicacionCobranzaFactura');

-- 1. Contexto y acceso. Solo metadatos, sin cadena de conexión.
SELECT DB_NAME() AS BaseSeleccionada,
       CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS VersionMotor,
       d.compatibility_level AS Compatibilidad,
       d.collation_name AS CollationBase,
       HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'VIEW DEFINITION') AS PuedeVerDefiniciones
FROM sys.databases d
WHERE d.name = DB_NAME();

-- 2. Tipo real y esquema. Ausencia no certifica inexistencia si faltan permisos.
SELECT n.Nombre AS NombreSolicitado,
       s.name AS Esquema,
       o.name AS NombreReal,
       o.type AS Tipo,
       o.type_desc AS DescripcionTipo,
       o.modify_date AS UltimaModificacion,
       sy.base_object_name AS DestinoSinonimo
FROM @Objetos n
LEFT JOIN sys.objects o ON UPPER(o.name) = UPPER(n.Nombre)
LEFT JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.synonyms sy ON sy.object_id = o.object_id
ORDER BY n.Nombre, s.name;

-- 3. Campos, anchos, escala, nulabilidad, identity, cálculo y defaults.
SELECT s.name AS Esquema, o.name AS Objeto, c.column_id AS Orden,
       c.name AS Campo, ty.name AS Tipo, c.max_length AS AnchoBytes,
       c.precision AS Precision, c.scale AS Escala,
       c.is_nullable AS PermiteNull, c.is_identity AS EsIdentity,
       c.is_computed AS EsCalculado, c.collation_name AS CollationCampo,
       dc.definition AS ValorDefault, cc.definition AS ExpresionCalculada
FROM sys.objects o
INNER JOIN @Objetos n ON UPPER(o.name) = UPPER(n.Nombre)
INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
INNER JOIN sys.columns c ON c.object_id = o.object_id
INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
ORDER BY s.name, o.name, c.column_id;

-- 4. PK/índices: una fila por columna; mantener orden y columnas incluidas.
SELECT s.name AS Esquema, o.name AS Objeto, i.name AS Indice,
       i.type_desc AS TipoIndice, i.is_primary_key AS EsPK,
       i.is_unique AS EsUnico, i.is_disabled AS Deshabilitado,
       i.filter_definition AS Filtro, ic.key_ordinal AS OrdenClave,
       ic.index_column_id AS OrdenIndice, c.name AS Campo,
       ic.is_included_column AS EsIncluida, ic.is_descending_key AS Descendente
FROM sys.objects o
INNER JOIN @Objetos n ON UPPER(o.name) = UPPER(n.Nombre)
INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
INNER JOIN sys.indexes i ON i.object_id = o.object_id AND i.index_id > 0
INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
ORDER BY s.name, o.name, i.index_id, ic.index_column_id;

-- 5. FK que salen de los objetos o llegan a ellos, relevantes para reconstrucción.
SELECT fk.name AS Relacion, ps.name AS EsquemaOrigen, po.name AS ObjetoOrigen,
       pc.name AS CampoOrigen, rs.name AS EsquemaDestino, ro.name AS ObjetoDestino,
       rc.name AS CampoDestino, fkc.constraint_column_id AS Orden,
       fk.delete_referential_action_desc AS AccionBorrado,
       fk.update_referential_action_desc AS AccionActualizacion,
       fk.is_disabled AS Deshabilitada, fk.is_not_trusted AS SinValidar
FROM sys.foreign_keys fk
INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
INNER JOIN sys.objects po ON po.object_id = fkc.parent_object_id
INNER JOIN sys.schemas ps ON ps.schema_id = po.schema_id
INNER JOIN sys.columns pc ON pc.object_id = po.object_id AND pc.column_id = fkc.parent_column_id
INNER JOIN sys.objects ro ON ro.object_id = fkc.referenced_object_id
INNER JOIN sys.schemas rs ON rs.schema_id = ro.schema_id
INNER JOIN sys.columns rc ON rc.object_id = ro.object_id AND rc.column_id = fkc.referenced_column_id
WHERE EXISTS (SELECT 1 FROM @Objetos n WHERE UPPER(n.Nombre) = UPPER(po.name) OR UPPER(n.Nombre) = UPPER(ro.name))
ORDER BY po.name, fk.name, fkc.constraint_column_id;

-- 6. CHECK: puede haber reglas adicionales en triggers/SP.
SELECT s.name AS Esquema, o.name AS Objeto, ch.name AS Restriccion,
       ch.definition AS Definicion, ch.is_disabled AS Deshabilitada,
       ch.is_not_trusted AS SinValidar
FROM sys.check_constraints ch
INNER JOIN sys.objects o ON o.object_id = ch.parent_object_id
INNER JOIN @Objetos n ON UPPER(n.Nombre) = UPPER(o.name)
INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
ORDER BY s.name, o.name, ch.name;

-- 7. Triggers y definición, incluyendo INSTEAD OF en vistas.
SELECT s.name AS Esquema, o.name AS Objeto, tr.name AS TriggerNombre,
       tr.is_disabled AS Deshabilitado, tr.is_instead_of_trigger AS EsInsteadOf,
       tr.type_desc AS TipoTrigger, m.definition AS Definicion,
       m.uses_ansi_nulls AS UsaAnsiNulls, m.uses_quoted_identifier AS UsaQuotedIdentifier
FROM sys.triggers tr
INNER JOIN sys.objects o ON o.object_id = tr.parent_id
INNER JOIN @Objetos n ON UPPER(n.Nombre) = UPPER(o.name)
INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.sql_modules m ON m.object_id = tr.object_id
ORDER BY s.name, o.name, tr.name;

-- 8. Definiciones de vistas/SP/funciones incluidos, sin ejecutarlos.
SELECT s.name AS Esquema, o.name AS Objeto, o.type_desc AS Tipo,
       m.definition AS Definicion,
       m.uses_ansi_nulls AS UsaAnsiNulls, m.uses_quoted_identifier AS UsaQuotedIdentifier
FROM sys.objects o
INNER JOIN @Objetos n ON UPPER(n.Nombre) = UPPER(o.name)
INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id
WHERE o.type IN ('V', 'P', 'FN', 'IF', 'TF', 'PC', 'FS', 'FT')
ORDER BY s.name, o.name;

-- 9. Dependencias directas. No detecta SQL dinámico ni realiza cierre transitivo.
SELECT s.name AS EsquemaOrigen, o.name AS ModuloOrigen,
       d.referenced_server_name AS ServidorDestino,
       d.referenced_database_name AS BaseDestino,
       d.referenced_schema_name AS EsquemaDestino,
       d.referenced_entity_name AS ObjetoDestino,
       d.is_schema_bound_reference AS ReferenciaSchemaBound
FROM sys.sql_expression_dependencies d
INNER JOIN sys.objects o ON o.object_id = d.referencing_id
INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE EXISTS (SELECT 1 FROM @Objetos n WHERE UPPER(n.Nombre) = UPPER(o.name))
   OR EXISTS (
       SELECT 1 FROM sys.triggers tr
       INNER JOIN sys.objects parentObject ON parentObject.object_id = tr.parent_id
       INNER JOIN @Objetos n ON UPPER(n.Nombre) = UPPER(parentObject.name)
       WHERE tr.object_id = o.object_id
   )
ORDER BY s.name, o.name, d.referenced_entity_name;

-- 10. Parámetros de procedimientos/funciones para evaluar reutilización por Dapper.
SELECT s.name AS Esquema, o.name AS Objeto, p.parameter_id AS Orden,
       p.name AS Parametro, ty.name AS Tipo, p.max_length AS AnchoBytes,
       p.precision AS Precision, p.scale AS Escala,
       p.is_output AS EsSalida, p.has_default_value AS TieneDefault
FROM sys.objects o
INNER JOIN @Objetos n ON UPPER(n.Nombre) = UPPER(o.name)
INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
INNER JOIN sys.parameters p ON p.object_id = o.object_id
INNER JOIN sys.types ty ON ty.user_type_id = p.user_type_id
ORDER BY s.name, o.name, p.parameter_id;
