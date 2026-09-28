/* Índices para las agregaciones diarias de FMT_V_MV_CPTE_ID. */
IF OBJECT_ID(N'dbo.V_MV_CPTE', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.V_MV_CPTE')
         AND name = N'IX_V_MV_CPTE_Fecha_Tc_Importe_POS'
   )
BEGIN
    CREATE NONCLUSTERED INDEX IX_V_MV_CPTE_Fecha_Tc_Importe_POS
        ON dbo.V_MV_CPTE (FECHA, TC)
        INCLUDE (IMPORTE);
END;

IF OBJECT_ID(N'dbo.MV_ASIENTOS', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.MV_ASIENTOS')
         AND name = N'IX_MV_ASIENTOS_Fecha_Tc_Iva_POS'
   )
BEGIN
    CREATE NONCLUSTERED INDEX IX_MV_ASIENTOS_Fecha_Tc_Iva_POS
        ON dbo.MV_ASIENTOS (FECHA, TC)
        INCLUDE (LIVA_AlicIVA, LIVA_ImpIVA, LIVA_AlicIVA2, LIVA_ImpIVA2);
END;
