/*
   Tabla auxiliar usada por sp_web_Alta_Comprobante para guardar la ubicación
   del vendedor. Es opcional para el POS, pero debe existir en bases nuevas y
   antiguas para que el procedimiento no falle al compilar la sentencia INSERT.
*/
IF OBJECT_ID(N'dbo.S_TA_UBICACIONES_VENDEDOR', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.S_TA_UBICACIONES_VENDEDOR
    (
        id int IDENTITY(1,1) NOT NULL,
        lat nvarchar(15) NULL,
        [long] nvarchar(15) NULL,
        idvendedor nvarchar(4) NULL,
        fechahora datetime NULL,
        idcomprobante nvarchar(13) NULL
    );
END;
