-- Cuenta vacía: el operador debe elegirla antes de confirmar el primer ajuste.
IF OBJECT_ID(N'dbo.TA_CONFIGURACION',N'U') IS NULL RETURN;
IF NOT EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE CLAVE=N'CONCILIACION-COBRANZA-AJUSTE')
    INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,ValorAux,DESCRIPCION)
    VALUES(N'CAJA',N'CONCILIACION-COBRANZA-AJUSTE',N'JSON',N'{"Cuenta":"","Limite":1.00}',N'Ajustes de centavos en cobranzas');
