/*
    2026-11-03 · Emisión real desde AlfaCore: las operaciones de Carga de comprobantes y sus
    autorizaciones ARCA admiten el ambiente PRODUCCION además de HOMOLOGACION.
    Las tablas se crearon cuando AlfaCore solo facturaba en homologación (CHECK = 'HOMOLOGACION')
    y el primer intento de emisión real en una base real falló con el error 547.
    Idempotente. Compatibility level 100.
*/
IF OBJECT_ID(N'dbo.ALFACORE_VENTAS_CARGA_OPERACION', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_ALFACORE_VENTAS_CARGA_AMBIENTE'
               AND parent_object_id = OBJECT_ID(N'dbo.ALFACORE_VENTAS_CARGA_OPERACION')
               AND definition NOT LIKE N'%PRODUCCION%')
        ALTER TABLE dbo.ALFACORE_VENTAS_CARGA_OPERACION DROP CONSTRAINT CK_ALFACORE_VENTAS_CARGA_AMBIENTE;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_ALFACORE_VENTAS_CARGA_AMBIENTE'
                   AND parent_object_id = OBJECT_ID(N'dbo.ALFACORE_VENTAS_CARGA_OPERACION'))
        ALTER TABLE dbo.ALFACORE_VENTAS_CARGA_OPERACION WITH CHECK ADD CONSTRAINT CK_ALFACORE_VENTAS_CARGA_AMBIENTE
            CHECK (Ambiente IN ('HOMOLOGACION', 'PRODUCCION'));
END;

IF OBJECT_ID(N'dbo.ALFACORE_VENTAS_HOMO_PRUEBA', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_ALFACORE_VENTAS_HOMO_AMBIENTE'
               AND parent_object_id = OBJECT_ID(N'dbo.ALFACORE_VENTAS_HOMO_PRUEBA')
               AND definition NOT LIKE N'%PRODUCCION%')
        ALTER TABLE dbo.ALFACORE_VENTAS_HOMO_PRUEBA DROP CONSTRAINT CK_ALFACORE_VENTAS_HOMO_AMBIENTE;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_ALFACORE_VENTAS_HOMO_AMBIENTE'
                   AND parent_object_id = OBJECT_ID(N'dbo.ALFACORE_VENTAS_HOMO_PRUEBA'))
        ALTER TABLE dbo.ALFACORE_VENTAS_HOMO_PRUEBA WITH CHECK ADD CONSTRAINT CK_ALFACORE_VENTAS_HOMO_AMBIENTE
            CHECK (Ambiente IN (N'HOMOLOGACION', N'PRODUCCION'));
END;
