/*
  Seed local para la prueba manual supervisada de Embedded Signup.

  Uso exclusivo sobre:
    (localdb)\MSSQLLocalDB / ALFA_CENTRAL_DEV

  No forma parte del bootstrap ni de los integration tests automatizados.
  El nombre es deliberadamente ficticio; solo el identificador 84 coincide
  con la Base AlfaCore seleccionada durante la prueba supervisada.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'ALFA_CENTRAL_DEV'
    THROW 51030, 'SEGURIDAD: este seed solo puede ejecutarse en ALFA_CENTRAL_DEV.', 1;

IF OBJECT_ID(N'dbo.bases', N'U') IS NULL
    THROW 51031, 'No existe dbo.bases en ALFA_CENTRAL_DEV.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.bases WHERE id = 84)
BEGIN
    INSERT INTO dbo.bases (id, nombre)
    VALUES (84, N'ES_DEV_BASE_84');
END;
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.bases WHERE id = 84 AND nombre = N'ES_DEV_BASE_84')
    THROW 51032, 'El IdBase 84 ya existe con otra identidad en ALFA_CENTRAL_DEV.', 1;

SELECT id, nombre
FROM dbo.bases
WHERE id = 84;
