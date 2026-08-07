/*
Catalogo de modulos vendibles por separado (arranca con Conversaciones) + activacion por
cliente. Ver docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md para el diseno completo.

Modelo elegido:
- Un modulo = un nodo raiz de ALFACORE_MENU_WEB (MenuKeyRaiz) + todo lo que cuelga de el en el
  menu (eso no se modela aqui: se resuelve en tiempo de lectura contra la tabla de menu de cada
  base, que hoy es igual en todas las bases de clientes).
- Un modulo puede depender de otros modulos ya definidos (ModulosDependencias), de dos tipos:
  - Obligatoria (EsObligatoria = 1): se activa gratis en cascada junto con el modulo que
    depende de ella (caso Conversaciones -> Clientes/Tecnicos: sin esto Conversaciones ni
    funciona).
  - Opcional (EsObligatoria = 0): NO se activa sola. Es un enganche informativo que la propia
    pantalla usa para decidir si mostrar u ocultar una funcion puntual (caso Conversaciones ->
    Tickets/Partes de horas: si el cliente no compro ese modulo aparte, el boton
    correspondiente dentro de Conversaciones se oculta en vez de regalarse).
- ClienteModulos es la activacion real: sin autoservicio en esta primera version, la carga un
  admin de Alfa (superadmin) desde /admin, despues de confirmar el pago por fuera del sistema.
- EsClienteLegacy en dbo.Clientes: los clientes existentes antes de este sistema quedan con
  todos los modulos habilitados sin necesidad de cargar filas en ClienteModulos una por una;
  la logica de la aplicacion debe interpretar EsClienteLegacy = 1 como "sin filtro de modulos".

Ejecutar manualmente contra ALFA_CENTRAL (no forma parte de App_Data/updates, que se aplica
por cliente contra la base de cada uno).

Los ALTER TABLE / CREATE TABLE que se referencian entre si van en batches separados por GO,
por el mismo motivo que en webhook_token_bases.sql: el compilador valida el CREATE INDEX /
la FK contra el esquema antes de reconocer lo agregado en el mismo batch.
*/

SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRAN;

    IF NOT EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.Clientes') AND name = N'EsClienteLegacy'
    )
    BEGIN
        ALTER TABLE dbo.Clientes ADD EsClienteLegacy bit NOT NULL CONSTRAINT DF_Clientes_EsClienteLegacy DEFAULT (0);
        -- Los clientes que ya existian antes de este sistema quedan marcados como legacy: van
        -- a seguir viendo todo el menu sin depender de filas en ClienteModulos.
        EXEC('UPDATE dbo.Clientes SET EsClienteLegacy = 1');
    END;

    IF OBJECT_ID(N'dbo.Modulos', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Modulos
        (
            Id int IDENTITY(1,1) NOT NULL,
            Codigo nvarchar(50) NOT NULL,
            Nombre nvarchar(150) NOT NULL,
            Descripcion nvarchar(500) NULL,
            MenuKeyRaiz nvarchar(100) NOT NULL,
            Precio decimal(18,2) NOT NULL CONSTRAINT DF_Modulos_Precio DEFAULT (0),
            Activo bit NOT NULL CONSTRAINT DF_Modulos_Activo DEFAULT (1),
            CreadoUtc datetime NOT NULL CONSTRAINT DF_Modulos_CreadoUtc DEFAULT (GETUTCDATE()),
            CONSTRAINT PK_Modulos PRIMARY KEY CLUSTERED (Id ASC),
            CONSTRAINT UQ_Modulos_Codigo UNIQUE (Codigo)
        );
    END;

    IF OBJECT_ID(N'dbo.ModulosDependencias', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ModulosDependencias
        (
            IdModulo int NOT NULL,
            IdModuloDependeDe int NOT NULL,
            EsObligatoria bit NOT NULL CONSTRAINT DF_ModulosDependencias_EsObligatoria DEFAULT (1),
            CONSTRAINT PK_ModulosDependencias PRIMARY KEY CLUSTERED (IdModulo ASC, IdModuloDependeDe ASC),
            CONSTRAINT CK_ModulosDependencias_NoAutoDependencia CHECK (IdModulo <> IdModuloDependeDe)
        );
    END;

    IF OBJECT_ID(N'dbo.ClienteModulos', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ClienteModulos
        (
            Id int IDENTITY(1,1) NOT NULL,
            IdCliente nvarchar(50) NOT NULL,
            IdModulo int NOT NULL,
            Estado nvarchar(20) NOT NULL CONSTRAINT DF_ClienteModulos_Estado DEFAULT (N'Activo'),
            ActivadoUtc datetime NOT NULL CONSTRAINT DF_ClienteModulos_ActivadoUtc DEFAULT (GETUTCDATE()),
            ActivadoPor nvarchar(100) NULL,
            CONSTRAINT PK_ClienteModulos PRIMARY KEY CLUSTERED (Id ASC),
            CONSTRAINT UQ_ClienteModulos_ClienteModulo UNIQUE (IdCliente, IdModulo),
            CONSTRAINT CK_ClienteModulos_Estado CHECK (Estado IN (N'Activo', N'Suspendido'))
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

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ModulosDependencias_Modulo'
    )
        ALTER TABLE dbo.ModulosDependencias
            ADD CONSTRAINT FK_ModulosDependencias_Modulo FOREIGN KEY (IdModulo) REFERENCES dbo.Modulos (Id);

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ModulosDependencias_ModuloDependeDe'
    )
        ALTER TABLE dbo.ModulosDependencias
            ADD CONSTRAINT FK_ModulosDependencias_ModuloDependeDe FOREIGN KEY (IdModuloDependeDe) REFERENCES dbo.Modulos (Id);

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ClienteModulos_Modulo'
    )
        ALTER TABLE dbo.ClienteModulos
            ADD CONSTRAINT FK_ClienteModulos_Modulo FOREIGN KEY (IdModulo) REFERENCES dbo.Modulos (Id);

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    THROW;
END CATCH;
GO
