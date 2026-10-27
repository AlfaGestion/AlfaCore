-- Conserva el cálculo histórico y evita aplicar todos los artículos de una factura a cada origen.
-- Solo adapta las dos definiciones legacy relevadas; conserva su tratamiento de PR/RM.
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.VT_V_MV_INSUMOS_APLICACION',N'V') IS NULL
    THROW 50000,N'Falta la vista oficial de aplicaciones de artículos. Revisar antes de habilitar consolidaciones.',1;
DECLARE @Actual nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.VT_V_MV_INSUMOS_APLICACION'));
IF CHARINDEX(N'ALFACORE_VENTAS_CARGA_ORIGEN_LINEA',UPPER(@Actual))>0 RETURN;
DECLARE @Esperada nvarchar(max)=N'CREATE VIEW [dbo].[VT_V_MV_INSUMOS_APLICACION]
AS
SELECT A.TCO_ORIGEN, A.IDComprobante_Origen AS IdComprobanteOrigen, I.IDARTICULO, SUM(I.CANTIDAD) AS CantidadAplicada
FROM dbo.V_MV_CpteInsumos AS I INNER JOIN dbo.MV_APLICACION AS A ON I.TC = A.TC AND I.IDCOMPROBANTE = A.IDComprobante
WHERE (NOT (I.TC = ''PR'')) OR (NOT (A.TCO_ORIGEN = ''RM''))
GROUP BY I.IDARTICULO, A.TCO_ORIGEN, A.IDComprobante_Origen';
-- Variante relevada en ELBOLSON2011_PRUEBAS: usa nombres calificados y no excluye PR/RM.
-- No agregar ese filtro a una base que históricamente no lo tenía.
DECLARE @EsperadaSinFiltro nvarchar(max)=N'CREATE VIEW dbo.VT_V_MV_INSUMOS_APLICACION
AS
SELECT dbo.MV_APLICACION.TCO_ORIGEN, dbo.MV_APLICACION.IDComprobante_Origen AS IdComprobanteOrigen,
dbo.V_MV_CpteInsumos.IDARTICULO, SUM(dbo.V_MV_CpteInsumos.CANTIDAD) AS CantidadAplicada
FROM dbo.V_MV_CpteInsumos INNER JOIN dbo.MV_APLICACION
ON dbo.V_MV_CpteInsumos.TC = dbo.MV_APLICACION.TC
AND dbo.V_MV_CpteInsumos.IDCOMPROBANTE = dbo.MV_APLICACION.IDComprobante
GROUP BY dbo.V_MV_CpteInsumos.IDARTICULO, dbo.MV_APLICACION.TCO_ORIGEN, dbo.MV_APLICACION.IDComprobante_Origen';
SELECT @Actual=LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@Actual,N'ALTER VIEW',N'CREATE VIEW'),N'[',N''),N']',N''),CHAR(13),N''),CHAR(10),N''),CHAR(9),N''),N' ',N'')),
       @Esperada=LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@Esperada,N'[',N''),N']',N''),CHAR(13),N''),CHAR(10),N''),CHAR(9),N''),N' ',N'')),
       @EsperadaSinFiltro=LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@EsperadaSinFiltro,N'[',N''),N']',N''),CHAR(13),N''),CHAR(10),N''),CHAR(9),N''),N' ',N''));
IF @Actual IS NULL OR (@Actual<>@Esperada AND @Actual<>@EsperadaSinFiltro)
    THROW 50000,N'La vista de aplicaciones de artículos tiene una variante no relevada. No se modificó: adaptar y verificar la trazabilidad para esta base.',1;
DECLARE @ExcluirPrRm bit=CASE WHEN @Actual=@Esperada THEN 1 ELSE 0 END;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.ALFACORE_VENTAS_CARGA_ORIGEN_LINEA',N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ALFACORE_VENTAS_CARGA_ORIGEN_LINEA
        (
            DetalleDestinoId int NOT NULL CONSTRAINT PK_ALFACORE_VENTAS_CARGA_ORIGEN_LINEA PRIMARY KEY,
            OrigenId int NOT NULL,
            DetalleOrigenId int NOT NULL
        );
        CREATE INDEX IX_ALFACORE_VENTAS_CARGA_ORIGEN_LINEA_Origen
            ON dbo.ALFACORE_VENTAS_CARGA_ORIGEN_LINEA(OrigenId,DetalleOrigenId);
    END;
    -- Sin FK sobre movimientos legacy: su mantenimiento no debe quedar bloqueado por una tabla auxiliar.
    -- Las lecturas requieren que existan ambos detalles y la aplicación; referencias huérfanas no descuentan.
    DECLARE @NuevaVista nvarchar(max)=N'ALTER VIEW dbo.VT_V_MV_INSUMOS_APLICACION
    AS
    SELECT TCO_ORIGEN,IdComprobanteOrigen,IDARTICULO,SUM(CantidadAplicada) CantidadAplicada
    FROM
    (
        SELECT A.TCO_ORIGEN,A.IDComprobante_Origen IdComprobanteOrigen,I.IDARTICULO,SUM(I.CANTIDAD) CantidadAplicada
        FROM dbo.V_MV_CpteInsumos I JOIN dbo.MV_APLICACION A ON I.TC=A.TC AND I.IDCOMPROBANTE=A.IDComprobante
        WHERE /*FILTRO_LEGACY*/ NOT EXISTS(SELECT 1 FROM dbo.ALFACORE_VENTAS_CARGA_ORIGEN_LINEA T WHERE T.DetalleDestinoId=I.ID)
        GROUP BY A.TCO_ORIGEN,A.IDComprobante_Origen,I.IDARTICULO
        UNION ALL
        SELECT O.TC,O.IDCOMPROBANTE,I.IDARTICULO,SUM(I.CANTIDAD)
        FROM dbo.ALFACORE_VENTAS_CARGA_ORIGEN_LINEA T
        JOIN dbo.V_MV_CpteInsumos I ON I.ID=T.DetalleDestinoId
        JOIN dbo.V_MV_Cpte O ON O.ID=T.OrigenId
        JOIN dbo.V_MV_CpteInsumos D ON D.ID=T.DetalleOrigenId AND D.TC=O.TC
            AND D.IDCOMPROBANTE=O.IDCOMPROBANTE AND D.IDCOMPLEMENTO=O.IDCOMPLEMENTO
        WHERE EXISTS(SELECT 1 FROM dbo.MV_APLICACION A WHERE A.TC=I.TC AND A.IDComprobante=I.IDCOMPROBANTE
            AND A.TCO_ORIGEN=O.TC AND A.IDComprobante_Origen=O.IDCOMPROBANTE)
            /*FILTRO_ORIGEN*/
        GROUP BY O.TC,O.IDCOMPROBANTE,I.IDARTICULO
    ) APLICACIONES
    GROUP BY TCO_ORIGEN,IdComprobanteOrigen,IDARTICULO;';
    SET @NuevaVista=REPLACE(@NuevaVista,N'/*FILTRO_LEGACY*/',CASE WHEN @ExcluirPrRm=1 THEN N'((I.TC<>''PR'') OR (A.TCO_ORIGEN<>''RM'')) AND' ELSE N'' END);
    SET @NuevaVista=REPLACE(@NuevaVista,N'/*FILTRO_ORIGEN*/',CASE WHEN @ExcluirPrRm=1 THEN N'AND ((I.TC<>''PR'') OR (O.TC<>''RM''))' ELSE N'' END);
    EXEC sys.sp_executesql @NuevaVista;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
