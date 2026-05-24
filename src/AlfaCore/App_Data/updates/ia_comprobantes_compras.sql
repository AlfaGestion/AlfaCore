 

/****** Object:  Table [dbo].[IA_Compras_CAB]    Script Date: 9/4/2026 18:03:40 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[IA_Compras_CAB](
	[ID] [int] IDENTITY(1,1) NOT NULL,
	[Estado] [nvarchar](20) NOT NULL,
	[FechaHora_Proceso] [datetime] NOT NULL,
	[FechaHora_Modificacion] [datetime] NULL,
	[Usuario_Proceso] [nvarchar](50) NULL,
	[Observaciones_Rev] [nvarchar](500) NULL,
	[Archivo_RutaOriginal] [nvarchar](500) NOT NULL,
	[Archivo_NombreOriginal] [nvarchar](260) NOT NULL,
	[Archivo_NombreRenombrado] [nvarchar](260) NULL,
	[Proveedor_Nombre] [nvarchar](50) NULL,
	[Proveedor_CUIT] [nvarchar](13) NULL,
	[Proveedor_Domicilio] [nvarchar](100) NULL,
	[Proveedor_CondIVA] [nvarchar](4) NULL,
	[Cuenta_Contable] [nvarchar](15) NULL,
	[Match_Metodo] [nvarchar](20) NULL,
	[TipoComprobante] [nvarchar](50) NULL,
	[Letra] [nvarchar](1) NULL,
	[PuntoVenta] [nvarchar](4) NULL,
	[Numero] [nvarchar](8) NULL,
	[Fecha] [datetime] NULL,
	[Vencimiento] [datetime] NULL,
	[CAE] [nvarchar](14) NULL,
	[VtoCAE] [datetime] NULL,
	[Moneda] [nvarchar](4) NULL,
	[NetoGravado] [money] NULL,
	[NetoNoGravado] [money] NULL,
	[Exento] [money] NULL,
	[IVA_21] [money] NULL,
	[IVA_105] [money] NULL,
	[IVA_27] [money] NULL,
	[Percepcion_IVA] [money] NULL,
	[Percepcion_IIBB] [money] NULL,
	[Percepcion_Ganancias] [money] NULL,
	[ImpuestosInternos] [money] NULL,
	[OtrosImpuestos] [money] NULL,
	[Total] [money] NULL,
	[Lector_Observaciones] [nvarchar](500) NULL,
	[Lector_Error] [nvarchar](500) NULL,
 CONSTRAINT [PK_IA_Compras_CAB] PRIMARY KEY CLUSTERED 
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO

/****** Object:  Table [dbo].[IA_Compras_DET]    Script Date: 9/4/2026 18:03:40 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[IA_Compras_DET](
	[ID] [int] IDENTITY(1,1) NOT NULL,
	[ID_CAB] [int] NOT NULL,
	[NroRenglon] [int] NOT NULL,
	[Cantidad] [nvarchar](20) NULL,
	[Codigo_Articulo] [nvarchar](50) NULL,
	[Descripcion] [nvarchar](200) NULL,
	[UD] [nvarchar](10) NULL,
	[Importe_Lista] [money] NULL,
	[Dto1] [float] NULL,
	[Dto2] [float] NULL,
	[Importe_Neto] [money] NULL,
	[IVA] [float] NULL,
	[ImpuestosInternos] [money] NULL,
	[Total] [money] NULL,
	[AuxNroLote] [nvarchar](50) NULL,
	[AuxNroSerie] [nvarchar](50) NULL,
 CONSTRAINT [PK_IA_Compras_DET] PRIMARY KEY CLUSTERED 
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO

ALTER TABLE [dbo].[IA_Compras_CAB] ADD  CONSTRAINT [DF_IA_Compras_CAB_Estado]  DEFAULT ('PENDIENTE') FOR [Estado]
GO

ALTER TABLE [dbo].[IA_Compras_CAB] ADD  CONSTRAINT [DF_IA_Compras_CAB_FhProceso]  DEFAULT (getdate()) FOR [FechaHora_Proceso]
GO

ALTER TABLE [dbo].[IA_Compras_DET]  WITH CHECK ADD  CONSTRAINT [FK_IA_Compras_DET_CAB] FOREIGN KEY([ID_CAB])
REFERENCES [dbo].[IA_Compras_CAB] ([ID])
ON DELETE CASCADE
GO

ALTER TABLE [dbo].[IA_Compras_DET] CHECK CONSTRAINT [FK_IA_Compras_DET_CAB]
GO

ALTER TABLE [dbo].[IA_Compras_CAB]  WITH CHECK ADD  CONSTRAINT [CK_IA_Compras_CAB_Estado] CHECK  (([Estado]='ELIMINADO' OR [Estado]='PROCESADO' OR [Estado]='PENDIENTE' OR [Estado]='APROBADO' OR [Estado]='RECHAZADO' OR [Estado]='ERROR_LECTURA' OR [Estado]='SIN_PROVEEDOR'))
GO

ALTER TABLE [dbo].[IA_Compras_CAB] CHECK CONSTRAINT [CK_IA_Compras_CAB_Estado]
GO


