/* Conciliacion bancaria: reglas genericas (todos los bancos) para clasificar impuestos y gastos del extracto.
   Las categorias nuevas se guardan como texto en BK_MovimientoClasificado / BK_ReglaInterpretacion (sin restricciones).
   Se aplican a los extractos que se importen desde ahora; no reclasifica importaciones existentes.
   Idempotente. Compatible con compatibility level 80/100. */
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.BK_ReglaInterpretacion',N'U') IS NOT NULL
BEGIN
    DECLARE @r TABLE(Codigo varchar(40),Descripcion varchar(200),Patron varchar(100),Signo int NULL,Categoria varchar(40),Prioridad int);
    INSERT @r(Codigo,Descripcion,Patron,Signo,Categoria,Prioridad)
        SELECT 'GEN_IDC_25413','Generico: impuesto a los debitos y creditos (Ley 25413)','ley 25413',NULL,'ImpuestoDebCred',49
        UNION ALL SELECT 'GEN_IDC_25413_PTOS','Generico: impuesto a los debitos y creditos (Ley 25.413)','ley 25.413',NULL,'ImpuestoDebCred',49
        UNION ALL SELECT 'GEN_SIRCREB','Generico: retencion SIRCREB','sircreb',NULL,'RetencionIibb',50
        UNION ALL SELECT 'GEN_IIBB_CRED','Generico: Ing. Brutos sobre creditos','ing. brutos',-1,'RetencionIibb',50
        UNION ALL SELECT 'GEN_IVA_PERCEP','Generico: percepcion de IVA','percep. iva',NULL,'IvaBancario',51
        UNION ALL SELECT 'GEN_IVA_PERCEP2','Generico: percepcion de IVA (orden inverso)','iva percep',NULL,'IvaBancario',51
        UNION ALL SELECT 'GEN_PAGO_PROVEEDOR','Generico: transferencia a proveedor','proveed',-1,'PagoProveedor',52
        UNION ALL SELECT 'GEN_PAGO_AFIP','Generico: pago a AFIP / ARCA','afip',-1,'PagoImpuestos',52
        UNION ALL SELECT 'GEN_PAGO_RECAUDACION','Generico: pago a agencia de recaudacion','recaudacion',-1,'PagoImpuestos',52
        UNION ALL SELECT 'GEN_IVA_BANCARIO','Generico: IVA sobre comisiones del banco','iva ',-1,'IvaBancario',53
        UNION ALL SELECT 'GEN_SUELDOS_HABERES','Generico: acreditamiento de haberes','haberes',-1,'Sueldos',54
        UNION ALL SELECT 'GEN_DEB_AUTOMATICO','Generico: debito automatico de servicios','deb. autom',-1,'DebitoAutomatico',55
        UNION ALL SELECT 'GEN_TRANSF_CTAS_PROPIAS','Generico: transferencia entre cuentas propias','ctas propias',NULL,'TransferenciaInterna',56
        UNION ALL SELECT 'GEN_TRANSF_CUENTA_PROPIA','Generico: ingreso a cuenta propia','cuenta propia',NULL,'TransferenciaInterna',56
        UNION ALL SELECT 'GEN_TRANSF_INMED_CP','Generico: transferencia inmediata a cuenta propia','inmed cp',-1,'TransferenciaInterna',56;

    INSERT dbo.BK_ReglaInterpretacion(ReglaCodigo,Descripcion,OrigenCodigo,PatronTexto,ImporteSigno,CategoriaObjetivo,Prioridad)
    SELECT x.Codigo,x.Descripcion,NULL,x.Patron,x.Signo,x.Categoria,x.Prioridad FROM @r x
    WHERE NOT EXISTS(SELECT 1 FROM dbo.BK_ReglaInterpretacion g WHERE g.ReglaCodigo=x.Codigo);
END
