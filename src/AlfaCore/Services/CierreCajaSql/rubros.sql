-- Consulta propia de AlfaCore, adaptada del relevamiento de cierre de caja.
SET NOCOUNT ON;
SELECT ltrim(idrubro) + ' - ' + descrubro as rubro,cantidad,valorventa,aliciva,valorventa_civa  INTO #Reporte FROM (
    SELECT ISNULL(IdRubro,'') as idrubro,isnull(DescRubro,'') as descrubro,convert(varchar,convert(decimal(15,2),SUM(CONSUMO)*-1)) as cantidad,
    convert(varchar,convert(decimal(15,2),SUM(ValorVenta)*-1)) as valorventa,convert(varchar,convert(decimal(15,2),IVARI)) as aliciva,
    convert(varchar,convert(decimal(15,2),((SUM(ValorVenta)*-1 * isnull(IVARI,21)) / 100) + SUM(VALORVENTA)*-1)) as ValorVenta_civa
    FROM ( SELECT dbo.Aux_AC_VentasDiaria.*, dbo.VT_MA_Articulos.DescRubro, dbo.VT_MA_Articulos.IDRUBRO
    FROM dbo.Aux_AC_VentasDiaria LEFT OUTER JOIN dbo.VT_MA_Articulos ON dbo.Aux_AC_VentasDiaria.IdArticulo = dbo.VT_MA_Articulos.IDARTICULO
    Where Fecha = @fecha AND (@unidad='' OR LTRIM(RTRIM(dbo.Aux_AC_VentasDiaria.UNEGOCIO))=@unidad)) AS a GROUP BY ISNULL(IdRubro,'') ,DescRubro,IVARI
    ) AS b;
