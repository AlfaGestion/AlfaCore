-- Consulta propia de AlfaCore, adaptada del relevamiento de cierre de caja.
SET NOCOUNT ON;
SELECT articulo,cantidad,valorventa,aliciva,valorventa_civa  INTO #Reporte FROM (
	SELECT ltrim(IdArticulo) + ' - ' + descripcion as articulo,
    convert(varchar,convert(decimal(15,2),SUM(Consumo) *-1)) as cantidad,
    convert(varchar,convert(decimal(15,2),SUM(ValorVenta) *-1)) as valorventa,
    idrubro,descrubro,IvaRI as aliciva,
    convert(varchar,convert(decimal(15,2),((SUM(ValorVenta)*-1 * isnull(IVARI,21)) / 100) + SUM(VALORVENTA)*-1)) as ValorVenta_civa FROM (
    SELECT dbo.Aux_AC_VentasDiaria.*, dbo.VT_MA_Articulos.DescRubro, dbo.VT_MA_Articulos.IDRUBRO, dbo.VT_MA_Articulos.DESCRIPCION
    FROM dbo.Aux_AC_VentasDiaria LEFT OUTER JOIN dbo.VT_MA_Articulos ON dbo.Aux_AC_VentasDiaria.IdArticulo = dbo.VT_MA_Articulos.IDARTICULO
    Where fecha=@fecha AND (@unidad='' OR LTRIM(RTRIM(dbo.Aux_AC_VentasDiaria.UNEGOCIO))=@unidad)
    ) AS A GROUP BY IdRubro,DescRubro,IdArticulo,DESCRIPCION,IvaRI --ORDER BY idrubro,idarticulo,ValorVenta
    ) AS b;
