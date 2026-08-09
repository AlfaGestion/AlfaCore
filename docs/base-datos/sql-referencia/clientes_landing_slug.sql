-- El link de confirmacion de /verify/{code} suele visitarse dos veces sin que el usuario haga
-- nada (webmails/filtros de seguridad que pre-visitan links antes de que el usuario los abra).
-- Cuando eso pasa, la fila de dbo.RegistroPublicoPendiente ya se borro para cuando el usuario
-- real hace clic, y VerifyAsync cae a un camino de "ya estaba confirmada" que hasta ahora no
-- sabia que modulo se habia elegido en la landing -> Verify.razor mostraba el selector manual
-- en vez de saltearlo. Esta columna persiste ese dato en dbo.Clientes (que no se borra) para que
-- ese segundo visiteo tambien pueda saltear el selector.
-- Ejecutar manualmente contra ALFA_CENTRAL — no se aplica solo.

IF COL_LENGTH('dbo.Clientes', 'ModuloSlugLanding') IS NULL
    ALTER TABLE dbo.Clientes ADD ModuloSlugLanding nvarchar(50) NULL;
GO
