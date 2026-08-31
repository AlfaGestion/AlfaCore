/*
  Seed histórico para la antigua prueba supervisada ES-2 con Base 106.

  No usar para pruebas nuevas: la identidad supervisada vigente es Base 84.

  Uso exclusivamente manual sobre:
    (localdb)\MSSQLLocalDB / ALFA_CENTRAL_DEV

  No forma parte del bootstrap ni de los integration tests automatizados.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'ALFA_CENTRAL_DEV'
    THROW 51020, 'SEGURIDAD: este seed solo puede ejecutarse en ALFA_CENTRAL_DEV.', 1;

IF OBJECT_ID(N'dbo.bases', N'U') IS NULL
    THROW 51021, 'No existe dbo.bases en ALFA_CENTRAL_DEV.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.bases WHERE id = 106)
BEGIN
    INSERT INTO dbo.bases (id, nombre)
    VALUES (106, N'ES_DEV_BASE_106');
END;

SELECT id, nombre
FROM dbo.bases
WHERE id IN (106, 1900000001, 1900000002)
ORDER BY id;
