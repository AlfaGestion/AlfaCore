SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

SET NOCOUNT ON;

-- ============================================================
-- Modulo Ventas / POS - Salon (mesas y comandas)
-- Una mesa abierta es un POS_PEDIDO con MODALIDAD = 'SALON' e
-- IDMESA. Estados de la mesa: ABIERTA / CUENTA (pidio la cuenta) /
-- ENTREGADO (cobrada, la marca MarcarFacturadoAsync) / ANULADO.
-- Cada linea de POS_PEDIDO_DET lleva su propio estado de cocina:
-- PENDIENTE (cargada, sin enviar) / ENVIADO / EN_PREPARACION /
-- LISTO / SERVIDO / ANULADO. Las lineas de Delivery quedan con
-- ESTADO NULL (no usan comanda).
-- DESTINO sale de V_MA_ARTICULOS.TalleDefault como en el legacy
-- (C = COCINA, B = BARRA, R = BARRA2); NULL = no se prepara.
-- No usa V_MV_COMANDA ni el comprobante PE del salon VB6: una base
-- debe trabajar el salon con VB6 o con AlfaCore, no con los dos.
-- Idempotente: puede ejecutarse varias veces sin romper datos.
-- ============================================================

-- POS_PEDIDO / POS_PEDIDO_DET los crea 2026-07-23-006.

IF COL_LENGTH(N'dbo.POS_PEDIDO', N'IDMESA') IS NULL
    ALTER TABLE dbo.POS_PEDIDO ADD IDMESA int NULL;
GO
IF COL_LENGTH(N'dbo.POS_PEDIDO', N'MOZO') IS NULL
    ALTER TABLE dbo.POS_PEDIDO ADD MOZO nvarchar(6) NULL;
GO
IF COL_LENGTH(N'dbo.POS_PEDIDO', N'CUBIERTOS') IS NULL
    ALTER TABLE dbo.POS_PEDIDO ADD CUBIERTOS int NULL;
GO
-- PIN de la sesion de mesa: lo usara la carta por QR para sumarse
-- a la misma cuenta. Cambia en cada apertura.
IF COL_LENGTH(N'dbo.POS_PEDIDO', N'PIN') IS NULL
    ALTER TABLE dbo.POS_PEDIDO ADD PIN nvarchar(6) NULL;
GO
IF COL_LENGTH(N'dbo.POS_PEDIDO', N'FECHAHORA_CUENTA') IS NULL
    ALTER TABLE dbo.POS_PEDIDO ADD FECHAHORA_CUENTA datetime NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_POS_PEDIDO_IDMESA_ESTADO' AND object_id = OBJECT_ID(N'dbo.POS_PEDIDO'))
    CREATE NONCLUSTERED INDEX IX_POS_PEDIDO_IDMESA_ESTADO ON dbo.POS_PEDIDO(IDMESA, ESTADO);
GO

IF COL_LENGTH(N'dbo.POS_PEDIDO_DET', N'ESTADO') IS NULL
    ALTER TABLE dbo.POS_PEDIDO_DET ADD ESTADO nvarchar(20) NULL;
GO
IF COL_LENGTH(N'dbo.POS_PEDIDO_DET', N'DESTINO') IS NULL
    ALTER TABLE dbo.POS_PEDIDO_DET ADD DESTINO nvarchar(10) NULL;
GO
-- ORIGEN: MOZO / QR (carta en el celular del cliente).
IF COL_LENGTH(N'dbo.POS_PEDIDO_DET', N'ORIGEN') IS NULL
    ALTER TABLE dbo.POS_PEDIDO_DET ADD ORIGEN nvarchar(10) NULL;
GO
IF COL_LENGTH(N'dbo.POS_PEDIDO_DET', N'USUARIO') IS NULL
    ALTER TABLE dbo.POS_PEDIDO_DET ADD USUARIO nvarchar(50) NULL;
GO
IF COL_LENGTH(N'dbo.POS_PEDIDO_DET', N'FECHAHORA_ALTA') IS NULL
    ALTER TABLE dbo.POS_PEDIDO_DET ADD FECHAHORA_ALTA datetime NULL;
GO
IF COL_LENGTH(N'dbo.POS_PEDIDO_DET', N'FECHAHORA_ENVIO') IS NULL
    ALTER TABLE dbo.POS_PEDIDO_DET ADD FECHAHORA_ENVIO datetime NULL;
GO
IF COL_LENGTH(N'dbo.POS_PEDIDO_DET', N'FECHAHORA_LISTO') IS NULL
    ALTER TABLE dbo.POS_PEDIDO_DET ADD FECHAHORA_LISTO datetime NULL;
GO
IF COL_LENGTH(N'dbo.POS_PEDIDO_DET', N'MOTIVOANULACION') IS NULL
    ALTER TABLE dbo.POS_PEDIDO_DET ADD MOTIVOANULACION nvarchar(100) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_POS_PEDIDO_DET_ESTADO_DESTINO' AND object_id = OBJECT_ID(N'dbo.POS_PEDIDO_DET'))
    CREATE NONCLUSTERED INDEX IX_POS_PEDIDO_DET_ESTADO_DESTINO ON dbo.POS_PEDIDO_DET(ESTADO, DESTINO);
GO
