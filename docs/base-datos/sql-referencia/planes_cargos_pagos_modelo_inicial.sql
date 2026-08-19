/*
Fase 2 del motor de comercializacion/suscripciones. Ver
docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md ("Actualizacion 2026-08-18: plan de
implementacion") para el diseno completo y las decisiones de producto confirmadas.

No se crea una tabla "Suscripciones" aparte: dbo.ClienteModulos ya es, conceptualmente, la
suscripcion + entitlement combinados (Cliente + Modulo + Estado + vigencia). Este script la
extiende con IdPlan y precio historico en vez de duplicar el concepto.

Decisiones de producto ya confirmadas que definen este modelo:
- Moneda explicita desde ya (ARS/USD) en Planes, Cargos y Pagos - no implicita como Modulos.Precio.
- Limite de agentes/asientos por plan se resuelve ahora (CantidadAgentesIncluida en Planes,
  CantidadAgentesContratados en ClienteModulos para excedente comprado aparte). El calculo real
  de "cuantos agentes tiene usados un cliente hoy" queda pendiente de una investigacion aparte
  (como se identifica un agente de Conversaciones en el esquema existente) antes de construir el
  servicio de validacion en la Fase 3 - no se inventa esa tabla aqui.
- Sin prorrateo: cambio de plan queda efectivo recien en el proximo periodo.
- Piloto: CONVERSACIONES y los modulos que ya tienen precio propio hoy (TICKETS, ALFAKNOWLEDGE,
  PARTES_HORAS, AUTOMATIZACIONES). CLIENTES/TECNICOS siguen siendo dependencias obligatorias
  gratuitas, no llevan Plan.

Ejecutar manualmente contra ALFA_CENTRAL (no forma parte de App_Data/updates, que se aplica por
cliente contra la base de cada uno). Idempotente: puede correrse varias veces sin romper datos.
*/

SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRAN;

    IF OBJECT_ID(N'dbo.Planes', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Planes
        (
            Id int IDENTITY(1,1) NOT NULL,
            IdModulo int NOT NULL,
            Codigo nvarchar(50) NOT NULL,
            Nombre nvarchar(150) NOT NULL,
            Descripcion nvarchar(500) NULL,
            TipoFacturacion nvarchar(20) NOT NULL,
            Precio decimal(18,2) NOT NULL CONSTRAINT DF_Planes_Precio DEFAULT (0),
            Moneda nvarchar(3) NOT NULL CONSTRAINT DF_Planes_Moneda DEFAULT (N'ARS'),
            DiasPrueba int NOT NULL CONSTRAINT DF_Planes_DiasPrueba DEFAULT (0),
            DiasGracia int NOT NULL CONSTRAINT DF_Planes_DiasGracia DEFAULT (0),
            CantidadIncluida int NULL,
            PermiteExcedentes bit NOT NULL CONSTRAINT DF_Planes_PermiteExcedentes DEFAULT (0),
            PrecioExcedente decimal(18,2) NULL,
            CantidadAgentesIncluida int NULL,
            PrecioPorAgenteExcedente decimal(18,2) NULL,
            RenovacionAutomaticaDefault bit NOT NULL CONSTRAINT DF_Planes_RenovacionAutomaticaDefault DEFAULT (1),
            Activo bit NOT NULL CONSTRAINT DF_Planes_Activo DEFAULT (1),
            VisibleCatalogo bit NOT NULL CONSTRAINT DF_Planes_VisibleCatalogo DEFAULT (1),
            CreadoUtc datetime NOT NULL CONSTRAINT DF_Planes_CreadoUtc DEFAULT (GETUTCDATE()),
            ModificadoUtc datetime NULL,
            CONSTRAINT PK_Planes PRIMARY KEY CLUSTERED (Id ASC),
            CONSTRAINT UQ_Planes_ModuloCodigo UNIQUE (IdModulo, Codigo),
            CONSTRAINT CK_Planes_Moneda CHECK (Moneda IN (N'ARS', N'USD')),
            CONSTRAINT CK_Planes_TipoFacturacion CHECK (TipoFacturacion IN (
                N'GRATIS', N'MENSUAL', N'ANUAL', N'DIAS', N'PAGO_UNICO',
                N'POR_USO', N'PAQUETE_USOS', N'CREDITOS'
            ))
        );
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.ClienteModulos') AND name = N'IdPlan'
    )
        ALTER TABLE dbo.ClienteModulos ADD IdPlan int NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.ClienteModulos') AND name = N'PrecioContratado'
    )
        ALTER TABLE dbo.ClienteModulos ADD PrecioContratado decimal(18,2) NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.ClienteModulos') AND name = N'MonedaContratada'
    )
        ALTER TABLE dbo.ClienteModulos ADD MonedaContratada nvarchar(3) NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.ClienteModulos') AND name = N'FechaProximoCobro'
    )
        ALTER TABLE dbo.ClienteModulos ADD FechaProximoCobro datetime NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.ClienteModulos') AND name = N'RenovacionAutomatica'
    )
        ALTER TABLE dbo.ClienteModulos ADD RenovacionAutomatica bit NOT NULL CONSTRAINT DF_ClienteModulos_RenovacionAutomatica DEFAULT (1);

    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.ClienteModulos') AND name = N'CantidadAgentesContratados'
    )
        ALTER TABLE dbo.ClienteModulos ADD CantidadAgentesContratados int NULL;

    IF OBJECT_ID(N'dbo.Cargos', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Cargos
        (
            Id int IDENTITY(1,1) NOT NULL,
            IdCliente nvarchar(50) NOT NULL,
            IdClienteModulo int NOT NULL,
            Concepto nvarchar(200) NOT NULL,
            PeriodoDesde date NULL,
            PeriodoHasta date NULL,
            Importe decimal(18,2) NOT NULL,
            Moneda nvarchar(3) NOT NULL CONSTRAINT DF_Cargos_Moneda DEFAULT (N'ARS'),
            FechaEmision datetime NOT NULL CONSTRAINT DF_Cargos_FechaEmision DEFAULT (GETUTCDATE()),
            FechaVencimiento datetime NOT NULL,
            Estado nvarchar(20) NOT NULL CONSTRAINT DF_Cargos_Estado DEFAULT (N'PENDIENTE'),
            CreadoUtc datetime NOT NULL CONSTRAINT DF_Cargos_CreadoUtc DEFAULT (GETUTCDATE()),
            ModificadoUtc datetime NULL,
            CONSTRAINT PK_Cargos PRIMARY KEY CLUSTERED (Id ASC),
            CONSTRAINT UQ_Cargos_ClienteModuloPeriodo UNIQUE (IdClienteModulo, PeriodoDesde),
            CONSTRAINT CK_Cargos_Moneda CHECK (Moneda IN (N'ARS', N'USD')),
            CONSTRAINT CK_Cargos_Estado CHECK (Estado IN (
                N'BORRADOR', N'PENDIENTE', N'PAGADO', N'PAGO_PARCIAL', N'VENCIDO', N'ANULADO'
            ))
        );
    END;

    IF OBJECT_ID(N'dbo.Pagos', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Pagos
        (
            Id int IDENTITY(1,1) NOT NULL,
            IdCliente nvarchar(50) NOT NULL,
            IdCargo int NULL,
            Fecha datetime NOT NULL CONSTRAINT DF_Pagos_Fecha DEFAULT (GETUTCDATE()),
            Importe decimal(18,2) NOT NULL,
            Moneda nvarchar(3) NOT NULL CONSTRAINT DF_Pagos_Moneda DEFAULT (N'ARS'),
            Estado nvarchar(20) NOT NULL CONSTRAINT DF_Pagos_Estado DEFAULT (N'CREADO'),
            MedioPago nvarchar(30) NOT NULL,
            Provider nvarchar(30) NULL,
            ProviderPaymentId nvarchar(100) NULL,
            ProviderTransactionId nvarchar(100) NULL,
            Referencia nvarchar(200) NULL,
            Observaciones nvarchar(500) NULL,
            RegistradoPor nvarchar(100) NULL,
            FechaAprobacion datetime NULL,
            CreadoUtc datetime NOT NULL CONSTRAINT DF_Pagos_CreadoUtc DEFAULT (GETUTCDATE()),
            ModificadoUtc datetime NULL,
            CONSTRAINT PK_Pagos PRIMARY KEY CLUSTERED (Id ASC),
            CONSTRAINT CK_Pagos_Moneda CHECK (Moneda IN (N'ARS', N'USD')),
            CONSTRAINT CK_Pagos_Estado CHECK (Estado IN (
                N'CREADO', N'PENDIENTE', N'APROBADO', N'RECHAZADO', N'CANCELADO', N'REEMBOLSADO'
            )),
            CONSTRAINT CK_Pagos_MedioPago CHECK (MedioPago IN (
                N'EFECTIVO', N'TRANSFERENCIA', N'MERCADO_PAGO', N'TARJETA', N'DEBITO_AUTOMATICO', N'OTRO'
            ))
        );
    END;

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH;
GO

BEGIN TRY
    BEGIN TRAN;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Planes_Modulo')
        ALTER TABLE dbo.Planes
            ADD CONSTRAINT FK_Planes_Modulo FOREIGN KEY (IdModulo) REFERENCES dbo.Modulos (Id);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ClienteModulos_Plan')
        ALTER TABLE dbo.ClienteModulos
            ADD CONSTRAINT FK_ClienteModulos_Plan FOREIGN KEY (IdPlan) REFERENCES dbo.Planes (Id);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Cargos_ClienteModulo')
        ALTER TABLE dbo.Cargos
            ADD CONSTRAINT FK_Cargos_ClienteModulo FOREIGN KEY (IdClienteModulo) REFERENCES dbo.ClienteModulos (Id);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Pagos_Cargo')
        ALTER TABLE dbo.Pagos
            ADD CONSTRAINT FK_Pagos_Cargo FOREIGN KEY (IdCargo) REFERENCES dbo.Cargos (Id);

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH;
GO
