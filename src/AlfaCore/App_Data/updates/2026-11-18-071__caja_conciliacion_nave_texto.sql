-- El texto que identifica a NAVE debe incluir también las devoluciones (NAVE DEVOLUCIÓN), no solo NAVE PAGO.
-- Solo se corrige el valor inicial; si el usuario lo cambió, no se toca.
UPDATE dbo.TA_CONFIGURACION SET VALOR=N'NAVE',DESCRIPCION=N'Texto de la descripción del extracto que identifica los créditos y devoluciones de NAVE'
WHERE CLAVE='CONCILIACION-NAVE-TEXTO' AND LTRIM(RTRIM(VALOR))=N'NAVE PAGO';
GO
