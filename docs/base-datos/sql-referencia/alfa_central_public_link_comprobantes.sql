/*
   Habilita enlaces públicos de comprobantes en ALFA_CENTRAL.
   Ejecutar contra la base ALFA_CENTRAL. No forma parte de App_Data/updates,
   que actualiza las bases de los clientes.
*/
IF OBJECT_ID(N'dbo.ALFA_PUBLIC_LINK', N'U') IS NULL
    RETURN;

IF EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_ALFA_PUBLIC_LINK_Tipo'
      AND parent_object_id = OBJECT_ID(N'dbo.ALFA_PUBLIC_LINK')
)
    ALTER TABLE dbo.ALFA_PUBLIC_LINK DROP CONSTRAINT CK_ALFA_PUBLIC_LINK_Tipo;

ALTER TABLE dbo.ALFA_PUBLIC_LINK
    ADD CONSTRAINT CK_ALFA_PUBLIC_LINK_Tipo
    CHECK ([Tipo] IN ('CARRITO', 'CATALOGO', 'CPTE'));
