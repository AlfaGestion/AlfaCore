 
/****** Object:  Table [dbo].[IA_Costos_Actualizacion_Hist]    Script Date: 23/4/2026 12:32:25 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[IA_Costos_Actualizacion_Hist](
	[ID] [int] IDENTITY(1,1) NOT NULL,
	[FechaHora] [datetime] NOT NULL,
	[ImportacionID] [int] NULL,
	[ImportacionDetID] [int] NULL,
	[Usuario] [nvarchar](50) NOT NULL,
	[Proveedor] [nvarchar](50) NULL,
	[CuentaProveedor] [nvarchar](15) NULL,
	[ArchivoOrigen] [nvarchar](500) NOT NULL,
	[FilaOrigen] [int] NOT NULL,
	[ArticuloID] [nvarchar](50) NOT NULL,
	[ArticuloCodigo] [nvarchar](50) NULL,
	[ProveedorCodigo] [nvarchar](50) NULL,
	[DescripcionImportada] [nvarchar](250) NULL,
	[DescripcionArticulo] [nvarchar](250) NULL,
	[CostoAnterior] [money] NULL,
	[CostoNuevo] [money] NOT NULL,
	[VariacionPct] [float] NULL,
	[MatchTipo] [nvarchar](30) NOT NULL,
	[MatchScore] [float] NULL,
	[AlertaVariacion] [bit] NOT NULL,
	[AlertaDetalle] [nvarchar](250) NULL,
	[Observaciones] [nvarchar](500) NULL,
 CONSTRAINT [PK_IA_Costos_Actualizacion_Hist] PRIMARY KEY CLUSTERED 
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO

/****** Object:  Table [dbo].[IA_Costos_Importacion_DET]    Script Date: 23/4/2026 12:32:25 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[IA_Costos_Importacion_DET](
	[ID] [int] IDENTITY(1,1) NOT NULL,
	[ID_CAB] [int] NOT NULL,
	[FilaOrigen] [int] NOT NULL,
	[Estado] [nvarchar](20) NOT NULL,
	[CodigoProveedorLeido] [nvarchar](100) NULL,
	[DescripcionLeida] [nvarchar](250) NULL,
	[PrecioCostoLeido] [money] NULL,
	[MonedaLeida] [nvarchar](4) NULL,
	[UnidadLeida] [nvarchar](20) NULL,
	[JsonFilaOriginal] [nvarchar](max) NULL,
	[ObservacionesLectura] [nvarchar](500) NULL,
	[IdArticulo] [nvarchar](25) NULL,
	[DescripcionArticulo] [nvarchar](100) NULL,
	[CostoActual] [money] NULL,
	[CostoNuevo] [money] NULL,
	[FhUltimoCosto] [datetime] NULL,
	[TipoMatch] [nvarchar](30) NULL,
	[ScoreMatch] [float] NULL,
	[CoincidenciaCodigoProveedor] [bit] NOT NULL,
	[ScoreDescripcion] [float] NULL,
	[ScorePrecioApoyo] [float] NULL,
	[FueSeleccionManual] [bit] NOT NULL,
	[DecisionUsuario] [nvarchar](20) NULL,
	[UsuarioRevision] [nvarchar](50) NULL,
	[FechaHoraRevision] [datetime] NULL,
	[ObservacionesRevision] [nvarchar](500) NULL,
	[AlertaVariacion] [bit] NOT NULL,
	[AlertaDetalle] [nvarchar](250) NULL,
	[VariacionPct] [float] NULL,
	[Aplicado] [bit] NOT NULL,
	[FechaHoraAplicacion] [datetime] NULL,
	[UsuarioAplicacion] [nvarchar](50) NULL,
	[ResultadoAplicacion] [nvarchar](20) NULL,
	[ErrorAplicacion] [nvarchar](500) NULL,
 CONSTRAINT [PK_IA_Costos_Importacion_DET] PRIMARY KEY CLUSTERED 
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

/****** Object:  Table [dbo].[IA_Costos_Importacion_CAB]    Script Date: 23/4/2026 12:32:25 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[IA_Costos_Importacion_CAB](
	[ID] [int] IDENTITY(1,1) NOT NULL,
	[FechaHora_Alta] [datetime] NOT NULL,
	[FechaHora_InicioProceso] [datetime] NULL,
	[FechaHora_FinProceso] [datetime] NULL,
	[Estado] [nvarchar](20) NOT NULL,
	[Usuario] [nvarchar](50) NOT NULL,
	[IdInterODBC] [int] NULL,
	[Proveedor] [nvarchar](50) NOT NULL,
	[CuentaProveedor] [nvarchar](15) NOT NULL,
	[PoliticaPrecios] [nvarchar](4) NULL,
	[Lista] [nvarchar](4) NULL,
	[SoloAlta] [bit] NOT NULL,
	[SoloModificacion] [bit] NOT NULL,
	[ArchivoOrigen] [nvarchar](500) NOT NULL,
	[ArchivoNombre] [nvarchar](260) NOT NULL,
	[ArchivoHash] [nvarchar](64) NULL,
	[TipoArchivo] [nvarchar](10) NULL,
	[HojaDetectada] [nvarchar](100) NULL,
	[HojaConfigurada] [nvarchar](100) NULL,
	[RangoDesde] [nvarchar](10) NULL,
	[RangoHasta] [nvarchar](10) NULL,
	[ColumnasDetectadas] [nvarchar](max) NULL,
	[ColumnasMapeadas] [nvarchar](max) NULL,
	[PrecioColumnaSeleccionada] [nvarchar](100) NULL,
	[NotasConfiguracion] [nvarchar](max) NULL,
	[NotasProceso] [nvarchar](max) NULL,
	[PromptIAAdicional] [nvarchar](max) NULL,
	[TotalFilasLeidas] [int] NOT NULL,
	[TotalFilasConCosto] [int] NOT NULL,
	[TotalFilasConfirmadas] [int] NOT NULL,
	[TotalActualizadas] [int] NOT NULL,
	[TotalAltas] [int] NOT NULL,
	[TotalSinCambios] [int] NOT NULL,
	[TotalErrores] [int] NOT NULL,
	[ErrorDetalle] [nvarchar](max) NULL,
 CONSTRAINT [PK_IA_Costos_Importacion_CAB] PRIMARY KEY CLUSTERED 
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

ALTER TABLE [dbo].[IA_Costos_Actualizacion_Hist] ADD  CONSTRAINT [DF_IA_Costos_Actualizacion_Hist_FechaHora]  DEFAULT (getdate()) FOR [FechaHora]
GO

ALTER TABLE [dbo].[IA_Costos_Actualizacion_Hist] ADD  CONSTRAINT [DF_IA_Costos_Actualizacion_Hist_Alerta]  DEFAULT ((0)) FOR [AlertaVariacion]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_DET] ADD  CONSTRAINT [DF_IA_Costos_Importacion_DET_Estado]  DEFAULT ('PENDIENTE') FOR [Estado]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_DET] ADD  CONSTRAINT [DF_IA_Costos_Importacion_DET_CodProv]  DEFAULT ((0)) FOR [CoincidenciaCodigoProveedor]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_DET] ADD  CONSTRAINT [DF_IA_Costos_Importacion_DET_Manual]  DEFAULT ((0)) FOR [FueSeleccionManual]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_DET] ADD  CONSTRAINT [DF_IA_Costos_Importacion_DET_Alerta]  DEFAULT ((0)) FOR [AlertaVariacion]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_DET] ADD  CONSTRAINT [DF_IA_Costos_Importacion_DET_Aplicado]  DEFAULT ((0)) FOR [Aplicado]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_CAB] ADD  CONSTRAINT [DF_IA_Costos_Importacion_CAB_FechaHoraAlta]  DEFAULT (getdate()) FOR [FechaHora_Alta]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_CAB] ADD  CONSTRAINT [DF_IA_Costos_Importacion_CAB_Estado]  DEFAULT ('PENDIENTE') FOR [Estado]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_CAB] ADD  CONSTRAINT [DF_IA_Costos_Importacion_CAB_SoloAlta]  DEFAULT ((0)) FOR [SoloAlta]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_CAB] ADD  CONSTRAINT [DF_IA_Costos_Importacion_CAB_SoloModificacion]  DEFAULT ((0)) FOR [SoloModificacion]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_CAB] ADD  CONSTRAINT [DF_IA_Costos_Importacion_CAB_TotalFilasLeidas]  DEFAULT ((0)) FOR [TotalFilasLeidas]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_CAB] ADD  CONSTRAINT [DF_IA_Costos_Importacion_CAB_TotalFilasConCosto]  DEFAULT ((0)) FOR [TotalFilasConCosto]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_CAB] ADD  CONSTRAINT [DF_IA_Costos_Importacion_CAB_TotalFilasConfirmadas]  DEFAULT ((0)) FOR [TotalFilasConfirmadas]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_CAB] ADD  CONSTRAINT [DF_IA_Costos_Importacion_CAB_TotalActualizadas]  DEFAULT ((0)) FOR [TotalActualizadas]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_CAB] ADD  CONSTRAINT [DF_IA_Costos_Importacion_CAB_TotalAltas]  DEFAULT ((0)) FOR [TotalAltas]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_CAB] ADD  CONSTRAINT [DF_IA_Costos_Importacion_CAB_TotalSinCambios]  DEFAULT ((0)) FOR [TotalSinCambios]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_CAB] ADD  CONSTRAINT [DF_IA_Costos_Importacion_CAB_TotalErrores]  DEFAULT ((0)) FOR [TotalErrores]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_DET]  WITH CHECK ADD  CONSTRAINT [FK_IA_Costos_Importacion_DET_CAB] FOREIGN KEY([ID_CAB])
REFERENCES [dbo].[IA_Costos_Importacion_CAB] ([ID])
ON DELETE CASCADE
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_DET] CHECK CONSTRAINT [FK_IA_Costos_Importacion_DET_CAB]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_DET]  WITH CHECK ADD  CONSTRAINT [CK_IA_Costos_Importacion_DET_Decision] CHECK  (([DecisionUsuario] IS NULL OR ([DecisionUsuario]='REASIGNAR' OR [DecisionUsuario]='DESCARTAR' OR [DecisionUsuario]='CONFIRMAR')))
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_DET] CHECK CONSTRAINT [CK_IA_Costos_Importacion_DET_Decision]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_DET]  WITH CHECK ADD  CONSTRAINT [CK_IA_Costos_Importacion_DET_Estado] CHECK  (([Estado]='ERROR' OR [Estado]='SIN_MATCH' OR [Estado]='APLICADO' OR [Estado]='LISTO_APLICAR' OR [Estado]='DESCARTADO' OR [Estado]='CONFIRMADO' OR [Estado]='MATCHEADO' OR [Estado]='PENDIENTE'))
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_DET] CHECK CONSTRAINT [CK_IA_Costos_Importacion_DET_Estado]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_DET]  WITH CHECK ADD  CONSTRAINT [CK_IA_Costos_Importacion_DET_Resultado] CHECK  (([ResultadoAplicacion] IS NULL OR ([ResultadoAplicacion]='BLOQUEADO' OR [ResultadoAplicacion]='ERROR' OR [ResultadoAplicacion]='ALTA' OR [ResultadoAplicacion]='SIN_CAMBIO' OR [ResultadoAplicacion]='OK')))
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_DET] CHECK CONSTRAINT [CK_IA_Costos_Importacion_DET_Resultado]
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_CAB]  WITH CHECK ADD  CONSTRAINT [CK_IA_Costos_Importacion_CAB_Estado] CHECK  (([Estado]='CANCELADA' OR [Estado]='ERROR' OR [Estado]='APLICADA_PARCIAL' OR [Estado]='APLICADA' OR [Estado]='LISTA_PARA_APLICAR' OR [Estado]='EN_REVISION' OR [Estado]='IMPORTADA' OR [Estado]='CONFIGURADA' OR [Estado]='PENDIENTE'))
GO

ALTER TABLE [dbo].[IA_Costos_Importacion_CAB] CHECK CONSTRAINT [CK_IA_Costos_Importacion_CAB_Estado]
GO


