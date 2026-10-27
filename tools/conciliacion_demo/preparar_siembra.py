"""Genera SQL revisable a partir del manifiesto ficticio; no ejecuta ni borra datos.
El SQL incluye guardas de servidor/base, colisiones y balance. Se valida inicialmente con ROLLBACK.
"""
import argparse
import json
import uuid
from decimal import Decimal
from pathlib import Path

def q(value):
    if value is None:return 'NULL'
    if isinstance(value,(int,Decimal)):return str(value)
    return "N'"+str(value).replace("'","''")+"'"

def preparar(manifiesto,salida):
    m=json.loads(manifiesto.read_text(encoding='utf-8'))
    if m['lote']!='DEMO-CONCILIACION-202610':raise ValueError('Manifiesto no reconocido')
    lineas=["""SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
IF DB_NAME()<>N'ALFANET' OR CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))<>N'SERVER-ALFAVB6'
    THROW 51000,'Este lote solo puede ejecutarse en SERVER-ALFAVB6 / ALFANET.',1;
BEGIN TRANSACTION;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource=N'ALFACORE-DEMO-CONCILIACION',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
IF @lock<0 THROW 51000,'No se obtuvo el bloqueo del lote DEMO.',1;
IF EXISTS(SELECT 1 FROM TA_CONFIGURACION WHERE CLAVE=N'DEMO-CONCILIACION-202610')
    THROW 51000,'El lote ya fue sembrado: no se repite ni se borran sus operaciones.',1;
DECLARE @unidad nvarchar(4)=(SELECT TOP(1) Codigo FROM V_TA_UnidadNegocio WHERE LTRIM(RTRIM(Codigo))=N'1');
IF @unidad IS NULL THROW 51000,'No existe la unidad de negocio 1 esperada en el relevamiento.',1;
DECLARE @periodo nvarchar(6)=(SELECT CONVERT(nvarchar(6),PERIODO) FROM MV_EJERCICIOS WHERE '20261001' BETWEEN [FECHA DESDE] AND [FECHA HASTA] AND CERRADO=0);
IF @periodo IS NULL THROW 51000,'Falta un ejercicio abierto para octubre 2026.',1;
DECLARE @sub nvarchar(4)=(SELECT s.CODIGO FROM MA_SUBDIARIOSCOMPROBANTES c JOIN MA_SUBDIARIOS s ON s.CODIGO=c.Subdiario WHERE LTRIM(RTRIM(c.TC))='CB');
DECLARE @subcc nvarchar(4)=(SELECT s.CODIGO FROM MA_SUBDIARIOSCOMPROBANTES c JOIN MA_SUBDIARIOS s ON s.CODIGO=c.Subdiario WHERE LTRIM(RTRIM(c.TC))='CC');
IF @sub IS NULL OR @subcc IS NULL THROW 51000,'Falta subdiario para CB o CC.',1;
IF NOT EXISTS(SELECT 1 FROM V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='CB' AND Talonario=0 AND X_NRO_HASTA>ISNULL(X_ULTIMO_NRO,0))
    THROW 51000,'Falta talonario CB disponible.',1;
"""]
    # Actualizaciones oficiales idempotentes necesarias para criterios, IA y generación de CC.
    for nombre in ['2026-10-22-001__caja_conciliacion_criterios.sql','2026-10-23-001__caja_conciliacion_ia.sql','2026-10-25-001__caja_conciliacion_asientos.sql']:
        lineas.append((Path('src/AlfaCore/App_Data/updates')/nombre).read_text(encoding='utf-8-sig'))
    cuentas=[(b['codigo'],'BANCO '+b['nombre'].upper()+' DEMO','BC','DB') for b in m['bancos']]
    cuentas += [('43991','Ingresos por servicios DEMO','',''),('53991','Gastos bancarios DEMO','',''),
                ('53992','Impuestos bancarios DEMO','',''),('43992','Intereses ganados DEMO','',''),
                ('11991','Retenciones IIBB DEMO','','RB')]
    cuentas += [(c['codigo'],c['nombre'],'CL','') for c in m['clientes']]
    codigos=','.join(q(c[0]) for c in cuentas)
    lineas.append(f"IF EXISTS(SELECT 1 FROM MA_CUENTAS WHERE LTRIM(RTRIM(CODIGO)) IN ({codigos})) THROW 51000,'Hay códigos del lote ocupados: no se reemplazan cuentas existentes.',1;")
    for codigo,nombre,vista,medio in cuentas:
        lineas.append('INSERT dbo.MA_CUENTAS(CODIGO,DESCRIPCION,TITULO,Dada_De_Baja,TipoVista,CajaYBanco,MedioDePago,Moneda,Libro_Iva_Ventas,FechaHora_Grabacion) VALUES('+','.join([
            q(codigo.ljust(15)),q(nombre), '0','0',q(vista), '1' if medio else '0',q(medio),q('   1'),'1' if vista=='CL' else '0','GETDATE()'])+');')
    for c in m['clientes']:
        lineas.append('INSERT dbo.MA_CUENTASADIC(CODIGO,CONTACTO,CALLE,LOCALIDAD,MAIL,DOCUMENTO_TIPO,NUMERO_DOCUMENTO,OBSERVACIONES,FechaHora_Grabacion) VALUES('+','.join([
            q(c['codigo'].ljust(15)),q('CONTACTO DEMO'),q('CALLE FICTICIA'),q('CIUDAD DEMO'),q('demo@example.invalid'),q('CUIT'),q(c['cuit']),q('Datos ficticios. No emitir comprobantes fiscales.'),'GETDATE()'])+');')
    clientes={c['codigo']:c for c in m['clientes']}
    documentos=m['documentos']
    for i,d in enumerate(documentos,1):
        cliente=clientes[d['cliente']];fecha=d['fecha'].replace('-','');cp=d['comprobante'];importe=Decimal(d['importe']);numero=80000000+i
        detalle=f"DEMO {d['caso']} - "+('Cob. ' if d['tc']=='CB' else 'Servicio ficticio - ')+cliente['nombre']
        lineas.append(f"IF EXISTS(SELECT 1 FROM V_MV_CPTE WHERE TC={q(d['tc'])} AND IDCOMPROBANTE={q(cp)}) THROW 51000,'Número de comprobante DEMO ocupado.',1;")
        lineas.append(f"IF NOT EXISTS(SELECT 1 FROM MA_CALENDARIO WHERE Fecha={q(fecha)}) THROW 51000,'Falta fecha del lote en MA_CALENDARIO.',1;")
        cols='TC,IDCOMPROBANTE,IDCOMPLEMENTO,FECHA,FECHAESTINICIO,FECHAESTFIN,CUENTA,NOMBRE,DOCUMENTONUMERO,COMENTARIOS,OBSERVACIONES,IMPORTE,IMPORTE_S_IVA,Moneda,Cotizacion,SUCURSAL,NUMERO,LETRA,UNEGOCIO,UNEGOCIO_DESTINO,Usuario,ANULADA,FINALIZADA,APROBADO,FechaHora_Grabacion'
        vals=[q(d['tc']),q(cp),'0',q(fecha),q(fecha),q(fecha),q(d['cliente']),q(cliente['nombre']),q(cliente['cuit']),q(detalle),q('DEMOSTRACION - SIN VALIDEZ FISCAL'),q(importe),q(importe),q('   1'),'1',q(cp[:4]),q(cp[4:12]),q('X'),'@unidad','@unidad',q('DEMO'),'0','0','1','GETDATE()']
        lineas.append(f"INSERT dbo.V_MV_CPTE({cols}) VALUES({','.join(vals)});")
        ledger=[]
        if d['tc']=='FP':ledger=[(d['cliente'],'D',importe),('43991','H',importe)]
        else:
            ret=Decimal(d['retencion']);ledger=[(d['cliente'],'H',importe),(d['cuenta_banco'],'D',importe-ret)]
            if ret:ledger.append(('11991','D',ret))
        for sec,(cuenta,sentido,monto) in enumerate(ledger,1):
            lineas.append(linea(cuenta,sec,numero,fecha,detalle,d['tc'],cp,sentido,monto,importe))
        for sec,a in enumerate(d['aplicaciones'],1):
            origen=a['comprobante'];cols='CUENTA,TC,SUCURSAL,NUMERO,LETRA,IDCOMPROBANTE,TCO_ORIGEN,SUCURSAL_ORIGEN,NUMERO_ORIGEN,LETRA_ORIGEN,IDCOMPLEMENTO_ORIGEN,IDComprobante_Origen,IMPORTE,UNegocio,Secuencia,TC_PRINT,IdComprobante_Print'
            vals=[q(d['cliente']),q('CB'),q(cp[:4]),q(cp[4:12]),q('X'),q(cp),q(a['tc']),q(origen[:4]),q(origen[4:12]),q(origen[-1]),'0',q(origen),q(Decimal(a['importe'])),'@unidad',str(sec),q('CB'),q(cp)]
            lineas.append(f"INSERT dbo.MV_APLICACION({cols}) VALUES({','.join(vals)});")
    for i,c in enumerate(m['contables'],1):
        numero=81000000+i;cp=f'0099{numero:08d}X';a=Decimal(c['importe']);fecha=c['fecha'].replace('-','');otro=c.get('destino',c.get('contrapartida'))
        for sec,cuenta,sentido in [(1,c['banco'],'D' if a>0 else 'H'),(2,otro,'H' if a>0 else 'D')]:
            lineas.append(linea(cuenta,sec,numero,fecha,'DEMO '+c['caso']+' - '+c['detalle'],'CC',cp,sentido,abs(a),abs(a)))
    lineas.append("DECLARE @cfg nvarchar(max)=(SELECT CONVERT(nvarchar(max),ValorAux) FROM TA_CONFIGURACION WHERE CLAVE='CONCILIACION-ASIENTOS'); IF ISJSON(@cfg)<>1 THROW 51000,'La configuración de asientos existente no es JSON válido.',1;")
    for banco in m['bancos']:
        for categoria,texto,cuenta,nombre in [('Comision','','53991','Comisión'),('Gasto','comision','53991','Gasto por comisión'),('Impuesto','sircreb','11991','Retención IIBB'),('IngresoFinanciero','','43992','Intereses ganados')]:
            regla=dict(Id=str(uuid.uuid5(uuid.NAMESPACE_URL,m['lote']+banco['codigo']+categoria)),Nombre='DEMO '+banco['nombre']+' '+nombre,
                Categoria=categoria,CuentaBanco=banco['codigo'],Texto=texto,Contrapartida=cuenta,Detalle='DEMO - '+nombre)
            lineas.append("SET @cfg=JSON_MODIFY(@cfg,'append $.Reglas',JSON_QUERY("+q(json.dumps(regla,ensure_ascii=False))+"));")
    lineas.append("UPDATE TA_CONFIGURACION SET ValorAux=@cfg WHERE CLAVE='CONCILIACION-ASIENTOS';")
    lineas.append("""
IF EXISTS(SELECT 1 FROM MV_ASIENTOS WHERE SUCURSAL='0099' AND CONVERT(int,NUMERO) BETWEEN 80000001 AND 81000999
 GROUP BY PERIODO,MES_OPERATIVO,[NUMERO ASIENTO],TIPO_REG HAVING ABS(SUM(CASE WHEN [DEBE-HABER]='D' THEN IMPORTE ELSE -IMPORTE END))>0.001)
 THROW 51000,'Un asiento DEMO no balancea.',1;
IF EXISTS(SELECT 1 FROM MV_ASIENTOS WHERE SUCURSAL='0099' AND [NUMERO ASIENTO] BETWEEN 80000001 AND 81000999
 AND (DATALENGTH(TC)<>DATALENGTH(RTRIM(TC)) OR DATALENGTH(CUENTA)<>DATALENGTH(RTRIM(CUENTA))))
 THROW 51000,'TC o CUENTA DEMO contienen relleno.',1;
IF EXISTS(
 SELECT f.ID
 FROM V_MV_CPTE f
 OUTER APPLY(SELECT SUM(IMPORTE) aplicado FROM MV_APLICACION a
     WHERE a.TCO_ORIGEN=f.TC AND a.IDComprobante_Origen=f.IDCOMPROBANTE AND a.CUENTA=f.CUENTA) ap
 OUTER APPLY(SELECT SUM(SALDO) saldo FROM VE_CPTES_SALDOS_VENTAS s
     WHERE s.CUENTA=f.CUENTA AND s.TC=f.TC AND s.SUCURSAL=f.SUCURSAL AND s.NUMERO=f.NUMERO AND s.LETRA=f.LETRA) sv
 WHERE f.TC='FP' AND f.SUCURSAL='0099' AND f.NUMERO BETWEEN '80000001' AND '80999999'
 AND ABS(ISNULL(sv.saldo,0)-(f.IMPORTE-ISNULL(ap.aplicado,0)))>0.01)
 THROW 51000,'El saldo de una proforma DEMO no coincide con la vista oficial de saldos.',1;
INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION,FechaHora_Grabacion)
 VALUES(N'CAJA',N'DEMO-CONCILIACION-202610',N'SEMBRADO',N'Lote ficticio conciliación octubre 2026',GETDATE());
SELECT (SELECT COUNT(*) FROM MA_CUENTAS WHERE LTRIM(RTRIM(CODIGO)) LIKE '112019%') clientes_demo,
 (SELECT COUNT(*) FROM V_MV_CPTE WHERE SUCURSAL='0099' AND NUMERO BETWEEN '80000001' AND '80999999') comprobantes_demo,
 (SELECT COUNT(*) FROM MV_ASIENTOS WHERE SUCURSAL='0099' AND [NUMERO ASIENTO] BETWEEN 80000001 AND 81000999) renglones_demo FOR JSON PATH;
-- REVISAR: el archivo validación revierte todos sus INSERT; el archivo aplicar confirma el lote.
""")
    salida.mkdir(parents=True,exist_ok=True)
    for nombre,fin in [('validar_siembra.sql','ROLLBACK TRANSACTION;'),('aplicar_siembra.sql','COMMIT TRANSACTION;')]:
        (salida/nombre).write_text('\n'.join(lineas)+'\n'+fin+'\n',encoding='utf-8')
    print(f'SQL generado: {len(cuentas)} cuentas, {len(documentos)} comprobantes. Sin ejecutar.')

def linea(cuenta,sec,numero,fecha,detalle,tc,cp,sentido,monto,total):
    cols='CUENTA,SECUENCIA,MES_OPERATIVO,[NUMERO ASIENTO],FECHA,DETALLE,TC,SUCURSAL,NUMERO,LETRA,[DEBE-HABER],IMPORTE,MONEDA,COTIZACION,PERIODO,CABIMPORTE,VENCIMIENTO,TIPO_REG,CONTABILIZADO,FechaHora_Grabacion,FechaSubdiario,USUARIO_LOGEADO,UNEGOCIO,Conciliado'
    vals=[q(cuenta),str(sec),str(int(fecha[4:6])),str(numero),q(fecha),q(detalle),q(tc),q(cp[:4]),q(cp[4:12]),q(cp[-1]),q(sentido),q(monto),q('   1'),'1','@periodo',q(total),q(fecha),'@subcc' if tc=='CC' else '@sub','0','GETDATE()',q(fecha),q('DEMO'),'@unidad','0']
    return f"INSERT dbo.MV_ASIENTOS({cols}) VALUES({','.join(vals)});"

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--manifiesto',type=Path,required=True);p.add_argument('--salida',type=Path,required=True)
    a=p.parse_args();preparar(a.manifiesto,a.salida)
