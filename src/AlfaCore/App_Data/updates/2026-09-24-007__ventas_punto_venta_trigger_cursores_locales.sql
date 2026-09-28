/*
  El trigger histórico MV_Asientos_ValidaFechas declara dos veces el cursor
  CRS_MV_ASIENTOS. Si una ejecución aborta antes de liberar el cursor, la
  siguiente venta falla con "Ya existe un cursor con ese nombre".
  Se renombran los cursores y se declaran LOCAL para aislar cada ejecución.
*/
IF OBJECT_ID(N'dbo.MV_Asientos_ValidaFechas', N'TR') IS NULL
    RETURN;

DECLARE @Definicion nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.MV_Asientos_ValidaFechas', N'TR'));
IF @Definicion IS NULL
    RETURN;

DECLARE @PrimeraCursor int = CHARINDEX(N'DECLARE CRS_MV', @Definicion);
DECLARE @SegundaCursor int = CHARINDEX(N'DECLARE CRS_MV', @Definicion, @PrimeraCursor + 1);
IF @PrimeraCursor = 0 OR @SegundaCursor = 0
    THROW 51000, 'No se encontraron las dos declaraciones de cursor de MV_Asientos_ValidaFechas.', 1;

DECLARE @BloqueDeleted nvarchar(max) = LEFT(@Definicion, @SegundaCursor - 1);
DECLARE @BloqueInserted nvarchar(max) = SUBSTRING(@Definicion, @SegundaCursor, LEN(@Definicion));

-- Normaliza tanto el trigger original como una versión que ya haya sido
-- procesada parcialmente por una ejecución anterior de este update.
DECLARE @CursorDeletedToken nvarchar(40) = N'__CURSOR_DELETED__';
DECLARE @CursorInsertedToken nvarchar(40) = N'__CURSOR_INSERTED__';

SET @BloqueDeleted = REPLACE(@BloqueDeleted, N'CRS_MV_ASIENTOS_DELETED_DELETED', @CursorDeletedToken);
SET @BloqueDeleted = REPLACE(@BloqueDeleted, N'CRS_MV_ASIENTOS_DELETED_INSERTED', @CursorDeletedToken);
SET @BloqueDeleted = REPLACE(@BloqueDeleted, N'CRS_MV_ASIENTOS_INSERTED', @CursorDeletedToken);
SET @BloqueDeleted = REPLACE(@BloqueDeleted, N'CRS_MV_ASIENTOS_DELETED', @CursorDeletedToken);
SET @BloqueDeleted = REPLACE(@BloqueDeleted, N'CRS_MV_ASIENTOS', @CursorDeletedToken);
SET @BloqueDeleted = REPLACE(@BloqueDeleted, @CursorDeletedToken, N'CRS_MV_ASIENTOS_DELETED');

SET @BloqueInserted = REPLACE(@BloqueInserted, N'CRS_MV_ASIENTOS_DELETED_DELETED', @CursorInsertedToken);
SET @BloqueInserted = REPLACE(@BloqueInserted, N'CRS_MV_ASIENTOS_DELETED_INSERTED', @CursorInsertedToken);
SET @BloqueInserted = REPLACE(@BloqueInserted, N'CRS_MV_ASIENTOS_INSERTED', @CursorInsertedToken);
SET @BloqueInserted = REPLACE(@BloqueInserted, N'CRS_MV_ASIENTOS_DELETED', @CursorInsertedToken);
SET @BloqueInserted = REPLACE(@BloqueInserted, N'CRS_MV_ASIENTOS', @CursorInsertedToken);
SET @BloqueInserted = REPLACE(@BloqueInserted, @CursorInsertedToken, N'CRS_MV_ASIENTOS_INSERTED');

SET @BloqueDeleted = REPLACE(@BloqueDeleted, N'DECLARE CRS_MV_ASIENTOS_DELETED  CURSOR FOR', N'DECLARE CRS_MV_ASIENTOS_DELETED CURSOR LOCAL FOR');
SET @BloqueDeleted = REPLACE(@BloqueDeleted, N'DECLARE CRS_MV_ASIENTOS_DELETED CURSOR FOR', N'DECLARE CRS_MV_ASIENTOS_DELETED CURSOR LOCAL FOR');
SET @BloqueInserted = REPLACE(@BloqueInserted, N'DECLARE CRS_MV_ASIENTOS_INSERTED CURSOR FOR', N'DECLARE CRS_MV_ASIENTOS_INSERTED CURSOR LOCAL FOR');
SET @Definicion = @BloqueDeleted + @BloqueInserted;
SET @Definicion = REPLACE(@Definicion, N'ALTER TRIGGER', N'ALTER TRIGGER');
SET @Definicion = REPLACE(@Definicion, N'CREATE TRIGGER', N'ALTER TRIGGER');
SET @Definicion = REPLACE(@Definicion, N'CREATE  TRIGGER', N'ALTER TRIGGER');

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
EXEC sys.sp_executesql @Definicion;
