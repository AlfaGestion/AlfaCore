 
/****** Object:  View [dbo].[vw_estadisticas_ingresos_diarias]    Script Date: 16/4/2026 09:42:19 ******/
IF OBJECT_ID('dbo.vw_estadisticas_ingresos_diarias', 'V') IS NOT NULL
BEGIN
	DROP VIEW [dbo].[vw_estadisticas_ingresos_diarias]
END
GO

/****** Object:  View [dbo].[vw_estadisticas_ingresos_diarias]    Script Date: 16/4/2026 09:42:19 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE     VIEW [dbo].[vw_estadisticas_ingresos_diarias]
AS
SELECT
    CONVERT(date, i.Ingreso) AS Fecha,
    /* =========================
       REGISTROS (operaciones)
       ========================= */
    COUNT(CASE WHEN i.Anulada = 0 THEN 1 END) AS ingresos_registros,
    COUNT(CASE WHEN i.Anulada = 0 AND i.EgresoReal IS NULL THEN 1 END) AS en_predio_registros,
    COUNT(CASE WHEN i.Anulada = 0 AND i.EgresoReal IS NOT NULL THEN 1 END) AS egresos_registros,
    /* =========================
       PERSONAS (totales)
       ========================= */
    SUM(CASE WHEN i.Anulada = 0 THEN
          COALESCE(i.Adultos,0)    + COALESCE(i.AdultosL,0)
        + COALESCE(i.Menores,0)   + COALESCE(i.MenoresL,0)
        + COALESCE(i.Jubilados,0) + COALESCE(i.JubiladosL,0)
        ELSE 0 END) AS ingresaron,
    SUM(CASE WHEN i.Anulada = 0 AND i.EgresoReal IS NULL THEN
          COALESCE(i.Adultos,0)    + COALESCE(i.AdultosL,0)
        + COALESCE(i.Menores,0)   + COALESCE(i.MenoresL,0)
        + COALESCE(i.Jubilados,0) + COALESCE(i.JubiladosL,0)
        ELSE 0 END) AS en_predio,
    SUM(CASE WHEN i.Anulada = 0 AND i.EgresoReal IS NOT NULL THEN
          COALESCE(i.Adultos,0)    + COALESCE(i.AdultosL,0)
        + COALESCE(i.Menores,0)   + COALESCE(i.MenoresL,0)
        + COALESCE(i.Jubilados,0) + COALESCE(i.JubiladosL,0)
        ELSE 0 END) AS egresaron,
    /* =========================
       GRUPOS (separados)
       ========================= */
    SUM(CASE WHEN i.Anulada = 0 THEN COALESCE(i.Adultos, 0)   ELSE 0 END) AS adultos,
    SUM(CASE WHEN i.Anulada = 0 THEN COALESCE(i.AdultosL, 0)  ELSE 0 END) AS adultos_l,
    SUM(CASE WHEN i.Anulada = 0 THEN COALESCE(i.Menores, 0)   ELSE 0 END) AS menores,
    SUM(CASE WHEN i.Anulada = 0 THEN COALESCE(i.MenoresL, 0)  ELSE 0 END) AS menores_l,
    SUM(CASE WHEN i.Anulada = 0 THEN COALESCE(i.Jubilados, 0)  ELSE 0 END) AS jubilados,
    SUM(CASE WHEN i.Anulada = 0 THEN COALESCE(i.JubiladosL, 0) ELSE 0 END) AS jubilados_l,
    /* Totales por grupo (L + no L) */
    SUM(CASE WHEN i.Anulada = 0 THEN COALESCE(i.Adultos,0)  + COALESCE(i.AdultosL,0)  ELSE 0 END) AS adultos_total,
    SUM(CASE WHEN i.Anulada = 0 THEN COALESCE(i.Menores,0) + COALESCE(i.MenoresL,0) ELSE 0 END) AS menores_total,
    SUM(CASE WHEN i.Anulada = 0 THEN COALESCE(i.Jubilados,0)+ COALESCE(i.JubiladosL,0) ELSE 0 END) AS jubilados_total,
    /* =========================
       OTROS
       ========================= */
    SUM(CASE WHEN i.Anulada = 0 THEN COALESCE(i.Estacionamiento, 0) ELSE 0 END) AS estacionamientos,
    SUM(CASE WHEN i.Anulada = 0 THEN COALESCE(i.BajadaLancha, 0)  ELSE 0 END) AS bajada_lancha,
    SUM(CASE WHEN i.Anulada = 0 THEN COALESCE(i.BajadaLanchaL, 0) ELSE 0 END) AS bajada_lancha_l,
    SUM(CASE WHEN i.Anulada = 0 THEN COALESCE(i.BajadaLancha,0) + COALESCE(i.BajadaLanchaL,0) ELSE 0 END) AS bajada_lancha_total,
    SUM(CASE WHEN i.Anulada = 0 THEN COALESCE(i.Adicional, 0)  ELSE 0 END) AS motorhome,
    SUM(CASE WHEN i.Anulada = 0 THEN COALESCE(i.AdicionalL, 0) ELSE 0 END) AS motorhome_l,
    SUM(CASE WHEN i.Anulada = 0 THEN COALESCE(i.Adicional,0) + COALESCE(i.AdicionalL,0) ELSE 0 END) AS motorhome_total
FROM dbo.MV_Ingresos i
GROUP BY CONVERT(date, i.Ingreso);
GO


