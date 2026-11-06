/*
    2026-11-06 · Anulación de órdenes de pago (OPG/PGFP) desde AlfaCore. Crea ALFACORE_PAGO_ANULACION: copia del asiento y de las
    aplicaciones que se retiran al anular, con usuario y motivo, para poder auditar o reconstruir la orden. Idempotente. Compatibility level 80/100.
*/
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.ALFACORE_PAGO_ANULACION', N'U') IS NULL
    CREATE TABLE dbo.ALFACORE_PAGO_ANULACION (
        Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ALFACORE_PAGO_ANULACION PRIMARY KEY,
        ComprobanteId    int            NOT NULL,
        Tc               nvarchar(4)    NOT NULL,
        Comprobante      nvarchar(13)   NOT NULL,
        Usuario          nvarchar(50)   NOT NULL,
        Motivo           nvarchar(500)  NOT NULL,
        AsientoJson      nvarchar(max)  NULL,
        AplicacionesJson nvarchar(max)  NULL,
        FechaHora        datetime       NOT NULL CONSTRAINT DF_ALFACORE_PAGO_ANULACION_Fecha DEFAULT (GETDATE())
    );
