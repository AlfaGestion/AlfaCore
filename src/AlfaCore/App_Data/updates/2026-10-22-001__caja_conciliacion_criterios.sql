-- Configuración global de propuestas; conserva valores definidos por cada base.
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.TA_CONFIGURACION',N'U') IS NULL RETURN;
INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION)
SELECT 'CAJA',v.Clave,v.Valor,v.Descripcion FROM (VALUES
    (N'CONCILIACION-TOLERANCIA-BANCO',N'1.00',N'Tolerancia importe banco/Alfa'),
    (N'CONCILIACION-COMPARAR-FECHA',N'SI',N'Comparar fechas banco/Alfa'),
    (N'CONCILIACION-COMPARAR-REFERENCIA',N'NO',N'Exigir referencia banco/Alfa'))v(Clave,Valor,Descripcion)
WHERE NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION c WHERE c.CLAVE=v.Clave);

-- Auditoría de diferencias aceptadas; no modifica movimientos ni importes.
IF OBJECT_ID(N'dbo.BK_Conciliacion',N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.BK_Conciliacion','DiferenciaImporte') IS NULL
        ALTER TABLE dbo.BK_Conciliacion ADD DiferenciaImporte decimal(19,4) NULL;
    IF COL_LENGTH('dbo.BK_Conciliacion','ToleranciaImporte') IS NULL
        ALTER TABLE dbo.BK_Conciliacion ADD ToleranciaImporte decimal(19,4) NULL;
    IF COL_LENGTH('dbo.BK_Conciliacion','MotivoDiferencia') IS NULL
        ALTER TABLE dbo.BK_Conciliacion ADD MotivoDiferencia nvarchar(300) NULL;
END;
