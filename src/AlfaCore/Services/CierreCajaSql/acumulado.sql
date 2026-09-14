-- Consulta propia de AlfaCore, adaptada del relevamiento de cierre de caja.
SET NOCOUNT ON;
SELECT *  INTO #Reporte FROM (SELECT '01 ACUMULADO VENTAS' AS nombre,
    isnull(convert(varchar,convert(decimal(15,2),SUM(LIVA_TOTAL))),0) AS importe_venta_total,
    isnull(convert(varchar,convert(decimal(15,2),SUM(LIVA_IMPIVA + LIVA_IMPIVA2 +LIVA_IMPIVA3+LIVA_IMPIVA4))),0) AS total_iva,
    count(tc) as cantidad_cptes FROM libroivaventas
    WHERE TC<>'NC'  AND (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND fecha=@fecha AND (@idcaja='' OR LTRIM(RTRIM(idcajas))=@idcaja)
    Union
    SELECT '02 ACUM NOTA DE CREDITO',
    isnull(convert(varchar,convert(decimal(15,2),SUM(LIVA_TOTAL))),0),
    isnull(convert(varchar,convert(decimal(15,2),SUM(
        LIVA_IMPIVA + LIVA_IMPIVA2 +LIVA_IMPIVA3+LIVA_IMPIVA4))),0),
    count(tc) FROM libroivaventas
    WHERE TC='NC'  AND (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND fecha=@fecha AND (@idcaja='' OR LTRIM(RTRIM(idcajas))=@idcaja)
    Union
    SELECT '03 ACUM PROFORMAS',
    isnull(convert(varchar,convert(decimal(15,2),SUM(IMPORTE))),0),
    isnull(convert(varchar,convert(decimal(15,2),SUM(
        LIVA_IMPIVA + LIVA_IMPIVA2 +LIVA_IMPIVA3+LIVA_IMPIVA4))),0),
    count(tc) FROM MV_ASIENTOS
    WHERE (TC='FP') AND SECUENCIA = 1  AND (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND fecha=@fecha AND (@idcaja='' OR LTRIM(RTRIM(idcajas))=@idcaja)
    Union
    SELECT '04 ACUM NC PROFORMA',
    isnull(convert(varchar,convert(decimal(15,2),SUM(IMPORTE)*-1 )),0),
    isnull(convert(varchar,convert(decimal(15,2),SUM(
        LIVA_IMPIVA + LIVA_IMPIVA2 +LIVA_IMPIVA3+LIVA_IMPIVA4)*-1)),0),
    count(tc) FROM MV_ASIENTOS
    WHERE (TC='NCFP') AND SECUENCIA = 1  AND (@unidad='' OR LTRIM(RTRIM(UNEGOCIO))=@unidad) AND fecha=@fecha AND (@idcaja='' OR LTRIM(RTRIM(idcajas))=@idcaja)
    ) AS A;
