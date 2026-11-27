-- MCP de consulta en lenguaje natural - script 13: compras y proveedores en el esquema `dir`.
-- Solo SELECT (vistas) + una clave de configuracion que se inserta solo si falta. Idempotente.
-- Sin APPLY, ventanas, OFFSET/FETCH, THROW ni STRING_SPLIT: corre en compatibilidad 80/100.
-- No hace falta ningun GRANT nuevo: el login del MCP tiene SELECT sobre todo el esquema dir (script 005).
-- Verificado contra la base de pruebas (copia de ALFANET, compat 100) el 2026-11-27:
--   * MV_ASIENTOS / C_MV_Cpte / C_MV_CpteInsumos / V_TA_Cpte son TABLAS; Vt_Proveedores, CO_CPTES_SALDOS y
--     CO_CPTES_CREDITOS_PENDIENTES son VISTAS.
--   * El libro de IVA compras (MV_ASIENTOS.LIVA_TIPO='COMPRAS') tiene UNA fila por comprobante (16.398, sin repetidos) y sus
--     importes LIVA_* ya vienen con signo. Octubre 2026 cuadra al centavo con Libro_ComprasConFP (FCC total 83.711.939,56;
--     LIQC 41.187.107,00; NCC -414.403,23). El neto de la cabecera C_MV_Cpte NO siempre coincide con el libro (correcciones
--     hechas en el libro), y 280 de 1998 comprobantes de 2026 no tienen cabecera (LT, GB: cargados en contabilidad):
--     por eso la fuente fiscal es el libro, no la cabecera.
--   * Neto de linea = CANTIDAD * IMPORTE_S_IVA * (1 - PorcDto/100); cuadra al centavo con C_MV_Cpte.ImporteInsumos.
--   * Saldos con proveedores: CO_CPTES_SALDOS (facturas, la que usa AlfaCore) + CO_CPTES_CREDITOS_PENDIENTES (pagos a cuenta
--     y notas de credito sin aplicar; en la base de pruebas 46,8 M que el saldo de AlfaCore no resta).
--   * Todas las compras de la base estan en pesos (Moneda 1, cotizacion 1).
SET NOCOUNT ON;
GO

-- ---------------------------------------------------------------------------------------------
-- Clave de configuracion: tipos de comprobante de compra proforma (no van al libro de IVA). No pisa un valor existente.
-- ---------------------------------------------------------------------------------------------
INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, DESCRIPCION, FechaHora_Grabacion)
SELECT N'IA_MCP', N'IA_MCP_TC_PROFORMA_COMPRAS', N'FPC,NCCP',
       N'Tipos de comprobante de COMPRA proforma (no fiscales), separados por coma. Los usa dir.vw_Compras; se incluyen segun IA_MCP_INCLUIR_PROFORMAS.',
       GETDATE()
WHERE NOT EXISTS (SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE = N'IA_MCP_TC_PROFORMA_COMPRAS');
GO

-- ---------------------------------------------------------------------------------------------
-- dir.vw_Proveedores: maestro de proveedores (MA_CUENTAS con TipoVista 'PR'). Sin telefono, mail ni CBU a proposito.
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID(N'dir.vw_Proveedores', N'V') IS NOT NULL DROP VIEW dir.vw_Proveedores;
GO
CREATE VIEW dir.vw_Proveedores
AS
SELECT
    LTRIM(RTRIM(p.CODIGO))                            AS ProveedorCodigo,
    LTRIM(RTRIM(p.RAZON_SOCIAL))                      AS RazonSocial,
    LTRIM(RTRIM(ISNULL(p.NUMERO_DOCUMENTO, N'')))     AS Documento,
    LTRIM(RTRIM(ISNULL(p.IVA, N'')))                  AS CondicionIva,
    LTRIM(RTRIM(ISNULL(p.LOCALIDAD, N'')))            AS Localidad,
    LTRIM(RTRIM(ISNULL(p.PROVINCIA, N'')))            AS Provincia,
    CASE WHEN ISNULL(p.Dada_De_Baja, 0) = 0 THEN 0 ELSE 1 END AS DadoDeBaja,
    CASE WHEN ISNULL(p.BLOQUEO, 0) = 0 THEN 0 ELSE 1 END      AS Bloqueado
FROM dbo.Vt_Proveedores p
GO

-- ---------------------------------------------------------------------------------------------
-- dir.vw_Compras: UNA FILA POR COMPROBANTE DE COMPRA, con signo (notas de credito en negativo).
--   Fuente FISCAL = libro de IVA compras: MV_ASIENTOS con LIVA_TIPO='COMPRAS' (una fila por comprobante, verificado en toda la historia).
--     Incluye los comprobantes cargados directo en contabilidad, sin cabecera de compra (liquidaciones de tarjeta, gastos bancarios...):
--     en 2026 son 280 de 1998. Los importes LIVA_* ya vienen con signo y cuadran al centavo con Libro_ComprasConFP.
--   Fuente PROFORMA = cabecera C_MV_Cpte de los tipos de IA_MCP_TC_PROFORMA_COMPRAS (no van al libro), no anulados, con signo de V_TA_Cpte.
--   NetoSinIva = gravado + no gravado + exento. Iva = todas las alicuotas. Percepciones = percepciones IVA + IIBB + ganancias.
--   OtrosImpuestos = Total - Neto - Iva - Percepciones (impuestos internos y redondeos; normalmente 0).
--   NetoArticulos = parte del neto con lineas de articulos (de la cabecera; 0 si no tiene cabecera).
--   Id = C_MV_Cpte.ID (NULL si no tiene cabecera de compra), para unir con dir.vw_ComprasDetalle.
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID(N'dir.vw_Compras', N'V') IS NOT NULL DROP VIEW dir.vw_Compras;
GO
CREATE VIEW dir.vw_Compras
AS
SELECT
    x.Id, x.Tc, x.TipoComprobante, x.Comprobante, x.Fecha, YEAR(x.Fecha) AS Anio, MONTH(x.Fecha) AS Mes,
    CONVERT(char(7), x.Fecha, 120) AS AnioMes, x.Vencimiento, x.UnidadNegocio, x.ProveedorCodigo, x.ProveedorNombre,
    x.Documento, x.EsNotaCredito, x.EsProforma, x.TieneCabecera,
    CAST(x.Neto AS decimal(18,2))                                         AS NetoSinIva,
    CAST(x.Iva AS decimal(18,2))                                          AS Iva,
    CAST(x.Percepciones AS decimal(18,2))                                 AS Percepciones,
    CAST(x.Total - x.Neto - x.Iva - x.Percepciones AS decimal(18,2))      AS OtrosImpuestos,
    CAST(x.Total AS decimal(18,2))                                        AS Total,
    CAST(x.NetoArticulos AS decimal(18,2))                                AS NetoArticulos
FROM (
    -- Comprobantes del libro de IVA compras
    SELECT
        c.ID                                                     AS Id,
        LTRIM(RTRIM(a.TC))                                       AS Tc,
        ISNULL(LTRIM(RTRIM(t.Descripcion)), LTRIM(RTRIM(a.TC)))  AS TipoComprobante,
        LTRIM(RTRIM(a.SUCURSAL)) + N'-' + LTRIM(RTRIM(a.NUMERO)) + LTRIM(RTRIM(a.LETRA)) AS Comprobante,
        a.FECHA                                                  AS Fecha,
        ISNULL(a.VENCIMIENTO, a.FECHA)                           AS Vencimiento,
        LTRIM(RTRIM(ISNULL(a.UNEGOCIO, N'')))                    AS UnidadNegocio,
        LTRIM(RTRIM(a.CUENTA))                                   AS ProveedorCodigo,
        COALESCE(NULLIF(LTRIM(RTRIM(a.CABNOMBRE)), N''), LTRIM(RTRIM(m.DESCRIPCION)), N'') AS ProveedorNombre,
        LTRIM(RTRIM(ISNULL(a.CABCUIT, N'')))                     AS Documento,
        CASE WHEN a.[DEBE-HABER] = N'D' THEN 1 ELSE 0 END        AS EsNotaCredito,
        0                                                        AS EsProforma,
        CASE WHEN c.ID IS NULL THEN 0 ELSE 1 END                 AS TieneCabecera,
        ISNULL(a.LIVA_ImpNetoGrav, 0) + ISNULL(a.LIVA_ImpNetoNGrav, 0) + ISNULL(a.LIVA_EXENTO, 0) AS Neto,
        ISNULL(a.LIVA_ImpIVA, 0) + ISNULL(a.LIVA_ImpIVARec, 0) + ISNULL(a.LIVA_ImpIva2, 0)
          + ISNULL(a.LIVA_ImpIVA3, 0) + ISNULL(a.LIVA_ImpIVA4, 0)                                 AS Iva,
        ISNULL(a.LIVA_Ret_Perc, 0) + ISNULL(a.LIVA_Ret_IBtos, 0) + ISNULL(a.LIVA_Ret_Ganancias, 0) AS Percepciones,
        ISNULL(a.LIVA_TOTAL, 0)                                  AS Total,
        CASE WHEN a.[DEBE-HABER] = N'D' THEN -1 ELSE 1 END * ISNULL(c.ImporteInsumos, 0) AS NetoArticulos
    FROM dbo.MV_ASIENTOS a
    LEFT JOIN dbo.C_MV_Cpte c
           ON c.TC = a.TC AND c.CUENTA = a.CUENTA AND c.SUCURSAL = a.SUCURSAL AND c.NUMERO = a.NUMERO AND c.LETRA = a.LETRA
          AND ISNULL(c.ANULADA, 0) = 0
    LEFT JOIN (SELECT CODIGO, MAX(DESCRIPCION) AS Descripcion FROM dbo.V_TA_Cpte GROUP BY CODIGO) t ON t.CODIGO = a.TC
    LEFT JOIN dbo.MA_CUENTAS m ON m.CODIGO = a.CUENTA
    WHERE a.LIVA_TIPO = N'COMPRAS'

    UNION ALL

    -- Proformas de compra (no fiscales): desde la cabecera
    SELECT
        c.ID,
        LTRIM(RTRIM(c.TC)),
        LTRIM(RTRIM(t.Descripcion)),
        LTRIM(RTRIM(c.SUCURSAL)) + N'-' + LTRIM(RTRIM(c.NUMERO)) + LTRIM(RTRIM(c.LETRA)),
        c.FECHA,
        ISNULL(c.VENCIMIENTO, c.FECHA),
        LTRIM(RTRIM(ISNULL(c.UNEGOCIO, N''))),
        LTRIM(RTRIM(c.CUENTA)),
        COALESCE(NULLIF(LTRIM(RTRIM(c.NOMBRE)), N''), LTRIM(RTRIM(m.DESCRIPCION)), N''),
        LTRIM(RTRIM(ISNULL(c.DOCUMENTONUMERO, N''))),
        CASE WHEN t.DebeHaber = N'D' THEN 1 ELSE 0 END,
        1,
        1,
        CASE WHEN t.DebeHaber = N'D' THEN -1 ELSE 1 END * ISNULL(c.IMPORTE_S_IVA, 0),
        CASE WHEN t.DebeHaber = N'D' THEN -1 ELSE 1 END * (ISNULL(c.ImporteIva, 0) + ISNULL(c.ImporteIva2, 0) + ISNULL(c.ImporteIvaRec, 0)),
        0,
        CASE WHEN t.DebeHaber = N'D' THEN -1 ELSE 1 END * ISNULL(c.IMPORTE, 0),
        CASE WHEN t.DebeHaber = N'D' THEN -1 ELSE 1 END * ISNULL(c.ImporteInsumos, 0)
    FROM dbo.C_MV_Cpte c
    INNER JOIN (SELECT CODIGO, MAX(DEBEHABER) AS DebeHaber, MAX(DESCRIPCION) AS Descripcion
                FROM dbo.V_TA_Cpte
                WHERE UPPER(SISTEMA) = N'COMPRAS' AND DEBEHABER IN (N'D', N'H')
                GROUP BY CODIGO) t
            ON t.CODIGO = c.TC
    INNER JOIN (SELECT TOP 1 N',' + REPLACE(ISNULL(VALOR, N''), N' ', N'') + N',' AS Lista
                FROM dbo.TA_CONFIGURACION WHERE CLAVE = N'IA_MCP_TC_PROFORMA_COMPRAS') cfg
            ON CHARINDEX(N',' + LTRIM(RTRIM(c.TC)) + N',', cfg.Lista) > 0
    LEFT JOIN dbo.MA_CUENTAS m ON m.CODIGO = c.CUENTA
    WHERE ISNULL(c.ANULADA, 0) = 0
) x
GO

-- ---------------------------------------------------------------------------------------------
-- dir.vw_ComprasDetalle: UNA FILA POR LINEA DE ARTICULO de las compras de dir.vw_Compras (mismo filtro, mismo signo).
--   CostoUnitario = precio neto unitario de compra (sin IVA, con descuento de linea). Cantidad y NetoSinIva con signo.
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID(N'dir.vw_ComprasDetalle', N'V') IS NOT NULL DROP VIEW dir.vw_ComprasDetalle;
GO
CREATE VIEW dir.vw_ComprasDetalle
AS
SELECT
    v.Id, v.Tc, v.Comprobante, v.Fecha, v.AnioMes, v.UnidadNegocio, v.ProveedorCodigo, v.ProveedorNombre,
    v.EsNotaCredito, v.EsProforma,
    LTRIM(RTRIM(i.IDARTICULO))                                   AS ArticuloCodigo,
    LTRIM(RTRIM(i.DESCRIPCION))                                  AS ArticuloDescripcion,
    ISNULL(LTRIM(RTRIM(r.Descripcion)), N'Sin rubro')            AS RubroNombre,
    LTRIM(RTRIM(ISNULL(i.IDUNIDAD, N'')))                        AS Unidad,
    CAST(CASE WHEN v.EsNotaCredito = 1 THEN -1 ELSE 1 END * ISNULL(i.CANTIDAD, 0) AS decimal(18,4))       AS Cantidad,
    CAST(ISNULL(i.IMPORTE_S_IVA, 0) * (1 - ISNULL(i.PorcDto, 0) / 100.0) AS decimal(18,4)) AS CostoUnitario,
    CAST(CASE WHEN v.EsNotaCredito = 1 THEN -1 ELSE 1 END * ISNULL(i.CANTIDAD, 0) * ISNULL(i.IMPORTE_S_IVA, 0) * (1 - ISNULL(i.PorcDto, 0) / 100.0) AS decimal(18,2)) AS NetoSinIva
FROM dbo.C_MV_Cpte c
INNER JOIN dir.vw_Compras v      ON v.Id = c.ID
INNER JOIN dbo.C_MV_CpteInsumos i ON i.TC = c.TC AND i.IDCOMPROBANTE = c.IDCOMPROBANTE AND i.CUENTA = c.CUENTA
LEFT JOIN dbo.V_MA_ARTICULOS a   ON a.IDARTICULO = i.IDARTICULO
LEFT JOIN dbo.V_TA_Rubros r      ON r.IdRubro = a.IDRUBRO
GO

-- ---------------------------------------------------------------------------------------------
-- dir.vw_CuentaCorrienteProveedores: saldos abiertos con proveedores, por comprobante.
--   Saldo > 0 = le debemos (facturas impagas, fuente CO_CPTES_SALDOS, la misma que usa AlfaCore).
--   Saldo < 0 = credito a nuestro favor sin aplicar (pagos a cuenta, notas de credito; fuente CO_CPTES_CREDITOS_PENDIENTES).
--   Deuda neta = SUM(Saldo). DiasVencido > 0 = vencido hace esos dias (solo tiene sentido en las facturas).
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID(N'dir.vw_CuentaCorrienteProveedores', N'V') IS NOT NULL DROP VIEW dir.vw_CuentaCorrienteProveedores;
GO
CREATE VIEW dir.vw_CuentaCorrienteProveedores
AS
SELECT
    y.ProveedorCodigo, ISNULL(LTRIM(RTRIM(m.DESCRIPCION)), y.Nombre) AS ProveedorNombre,
    y.Tc, y.Comprobante, y.Fecha, y.Vencimiento, y.Saldo, y.EsCredito,
    CASE WHEN y.EsCredito = 1 THEN NULL ELSE DATEDIFF(day, y.Vencimiento, GETDATE()) END AS DiasVencido,
    CASE WHEN y.EsCredito = 1 THEN N'Credito a favor'
         WHEN DATEDIFF(day, y.Vencimiento, GETDATE()) <= 0  THEN N'A vencer'
         WHEN DATEDIFF(day, y.Vencimiento, GETDATE()) <= 30 THEN N'Vencido 1-30'
         WHEN DATEDIFF(day, y.Vencimiento, GETDATE()) <= 60 THEN N'Vencido 31-60'
         WHEN DATEDIFF(day, y.Vencimiento, GETDATE()) <= 90 THEN N'Vencido 61-90'
         ELSE N'Vencido mas de 90' END AS Tramo
FROM (
    SELECT s.CUENTA AS CuentaRaw, LTRIM(RTRIM(s.CUENTA)) AS ProveedorCodigo, LTRIM(RTRIM(ISNULL(s.CABNOMBRE, N''))) AS Nombre,
           LTRIM(RTRIM(s.TC)) AS Tc,
           LTRIM(RTRIM(s.SUCURSAL)) + N'-' + LTRIM(RTRIM(s.NUMERO)) + LTRIM(RTRIM(s.LETRA)) AS Comprobante,
           s.FECHA AS Fecha, ISNULL(s.VENCIMIENTO, s.FECHA) AS Vencimiento,
           CAST(s.SALDO AS decimal(18,2)) AS Saldo, 0 AS EsCredito
    FROM dbo.CO_CPTES_SALDOS s
    WHERE s.TipoVista = N'PR' AND s.SALDO <> 0
    UNION ALL
    SELECT k.CUENTA, LTRIM(RTRIM(k.CUENTA)), LTRIM(RTRIM(ISNULL(k.CABNOMBRE, N''))),
           LTRIM(RTRIM(k.TC)),
           LTRIM(RTRIM(k.SUCURSAL)) + N'-' + LTRIM(RTRIM(k.NUMERO)) + LTRIM(RTRIM(k.LETRA)),
           k.FECHA, ISNULL(k.VENCIMIENTO, k.FECHA),
           CAST(-k.SALDO AS decimal(18,2)), 1
    FROM dbo.CO_CPTES_CREDITOS_PENDIENTES k
    WHERE k.TipoVista = N'PR' AND k.SALDO <> 0
) y
LEFT JOIN dbo.MA_CUENTAS m ON m.CODIGO = y.CuentaRaw
GO

-- Control rapido (no cambia nada): cantidad de filas por vista.
SELECT N'dir.vw_Proveedores' AS Vista, COUNT(*) AS Filas FROM dir.vw_Proveedores
UNION ALL SELECT N'dir.vw_Compras (ultimos 30 dias)', COUNT(*) FROM dir.vw_Compras WHERE Fecha >= DATEADD(day, -30, GETDATE())
UNION ALL SELECT N'dir.vw_CuentaCorrienteProveedores', COUNT(*) FROM dir.vw_CuentaCorrienteProveedores;
GO
