"""Genera extractos y manifiesto ficticios; nunca copia filas ni metadatos del banco.
Requiere openpyxl. Ejecutar: python tools/conciliacion_demo/generar_lotes.py --salida C:/dev/Extractos/DEMO_Conciliacion
"""
import argparse, csv, hashlib, json, zipfile
from datetime import date, datetime, timedelta
from decimal import Decimal
from pathlib import Path
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Alignment

D=Decimal
BANCOS=[('Santander','111020901'),('Provincia','111020902'),('MercadoPago','111010904')]
CASOS=[('E01','Coincidencia exacta'),('E02','Diferencia de 22 centavos'),('E03','Fecha contable dos días después'),
 ('E04','Fecha contable siete días después'),('E05','Un pago cancela dos facturas'),('E06','Diferencia por retención'),
 ('E07','Pago parcial'),('E08','Pagador sin cliente identificado'),('E09','Anticipo sin factura'),
 ('E10','Cobranza ya cargada con otra fecha'),('E11','Dos clientes con el mismo CUIT ficticio'),
 ('E12','Comisión ya contabilizada'),('E13','Comisión para generar asiento'),('E14','Retención bancaria IIBB'),
 ('E15','Intereses ganados'),('E16','Transferencia interna'),('E17','Dos depósitos contra un recibo'),
 ('E18','Dos contrapartidas contables del mismo importe')]
NOMBRES=['ALMACEN LUNA','PANADERIA NUBE','TALLER HORIZONTE','FARMACIA ESTRELLA','DISTRIBUIDORA RIO',
 'TRANSPORTES COMETA','HOTEL BRISA','MADERAS FANTASIA','COMERCIAL AURORA','SERVICIOS CEDRO','GRUPO FANTASIA']

def generar(salida,base='ALFANET'):
 if base not in ('ALFANET','ALFANET_DEMO'):raise ValueError('Base no permitida para este lote.')
 salida.mkdir(parents=True,exist_ok=True)
 clientes=[];documentos=[];movimientos=[];contables=[];archivos=[]
 def cliente(b,i,nombre=None,cuit=None):
  codigo=f'112019{b*100+i:03d}'
  if not any(c['codigo']==codigo for c in clientes):
   razon=(NOMBRES[i-1] if i<=len(NOMBRES) else f'CLIENTE {i:02d}')+f' DEMO {b}'
   clientes.append(dict(codigo=codigo,nombre=nombre or razon,cuit=cuit or f'99{b:02d}{i:06d}0'))
  return codigo
 def doc(b,caso,c,importe,fecha,recibo=False,cuenta=None,retencion='0'):
  numero=80000000+len(documentos)+1
  d=dict(caso=caso,cliente=c,tc='CB' if recibo else 'FP',comprobante=f'0099{numero:08d}X',
   fecha=fecha.isoformat(),importe=str(importe),cuenta_banco=cuenta,retencion=str(retencion),aplicaciones=[])
  documentos.append(d);return d
 def factura(b,caso,c,importe,fecha):return doc(b,caso,c,importe,fecha)
 def cobro(b,caso,c,importe,fecha,cuenta,facturas,retencion=D('0')):
  r=doc(b,caso,c,importe,fecha,True,cuenta,retencion)
  r['aplicaciones']=[dict(tc=f['tc'],comprobante=f['comprobante'],importe=f['importe']) for f in facturas]
  return r
 for b,(banco,cuenta) in enumerate(BANCOS,1):
  for i,(caso,titulo) in enumerate(CASOS,1):
   f=date(2026,10,i);a=D(b*100000+i*1000);c=cliente(b,i);nombre=next(x['nombre'] for x in clientes if x['codigo']==c)
   cuit=next(x['cuit'] for x in clientes if x['codigo']==c)
   detalle=f'Transferencia recibida de {nombre} CUIT {cuit}';tipo='Transferencia recibida';ref=f'DEMO{b}{i:04d}'
   if caso=='E08':detalle=f'Transferencia recibida de PAGADOR FANTASIA {b}'
   if caso=='E11':
    mismo=f'99{b:02d}0000500';clientes[-1]['cuit']=mismo
    otro=cliente(b,51,nombre=f'SEGUNDA EMPRESA DEMO {b}',cuit=f'{mismo[:2]}-{mismo[2:10]}-{mismo[10:]}')
    factura(b,caso,otro,a+D('7500'),f-timedelta(days=5))
    detalle=f'Transferencia de GRUPO FANTASIA {b} CUIT {mismo}'
   if caso in ('E12','E13','E14'):
    a=-D(1200+b*100+i);detalle={'E12':'Comision bancaria contabilizada DEMO','E13':'Comision mantenimiento cuenta DEMO','E14':'Retencion bancaria IIBB SIRCREB DEMO'}[caso];tipo='Comisión' if caso!='E14' else 'Retención'
   if caso=='E15':a=D('450.35')+b;detalle='Intereses por saldo DEMO';tipo='Rendimientos'
   if caso=='E16':
    a=D('-55000') if b==1 else D('55000') if b==2 else D('-33000')
    detalle='Transferencia interna entre cuentas DEMO';tipo='Transferencia enviada' if a<0 else 'Transferencia recibida'
   banco_fecha=f
   if caso in ('E12','E13','E14','E15','E16'):
    if caso=='E12':contables.append(dict(caso=caso,banco=cuenta,fecha=f.isoformat(),importe=str(a),detalle=detalle,contrapartida='53991'))
   elif caso=='E09':pass
   elif caso=='E05':
    factura(b,caso,c,a*D('.4'),f-timedelta(days=8));factura(b,caso,c,a*D('.6'),f-timedelta(days=3))
   elif caso=='E06':
    factura(b,caso,c,a+D('500'),f-timedelta(days=6))
    previa=factura(b,caso,c,D('10000'),date(2026,9,1))
    cobro(b,caso,c,D('10000'),date(2026,9,10),cuenta,[previa],D('500'))
   elif caso=='E07':factura(b,caso,c,a*2,f-timedelta(days=10))
   else:
    importe=a+D('.22') if caso=='E02' else a+D('.81') if caso=='E10' else a
    fc=factura(b,caso,c,importe,f-timedelta(days=5))
    if caso in ('E01','E02','E03','E04','E10','E17','E18'):
     ff=f+timedelta(days=2 if caso=='E03' else 7 if caso=='E04' else -6 if caso=='E10' else 0)
     cobro(b,caso,c,importe,ff,cuenta,[fc])
     if caso=='E18':
      otra=factura(b,caso,c,importe,f-timedelta(days=4));cobro(b,caso,c,importe,ff,cuenta,[otra])
   partes=[a*D('.4'),a*D('.6')] if caso=='E17' else [a]
   for j,importe in enumerate(partes):
    movimientos.append(dict(banco=banco,cuenta=cuenta,caso=caso,objetivo=titulo,fecha=banco_fecha.isoformat(),
      descripcion=detalle,referencia=ref+(str(j+1) if len(partes)>1 else ''),importe=str(importe.quantize(D('.01'))),tipo=tipo))
  c=cliente(b,60,nombre=f'COMERCIO FANTASIA DEMO {b}')
  for n in range(42):
   f=date(2026,10,19+n%6);a=D(b*10000+500+n*37)+D('.25');caso=f'V{n+1:02d}'
   fc=factura(b,caso,c,a,f-timedelta(days=8));cobro(b,caso,c,a,f,cuenta,[fc])
   movimientos.append(dict(banco=banco,cuenta=cuenta,caso=caso,objetivo='Volumen para filtros, orden y paginación',fecha=f.isoformat(),
    descripcion=f'Transferencia recibida de COMERCIO FANTASIA DEMO {b}',referencia=f'DEMO{b}V{n+1:04d}',importe=str(a),tipo='Transferencia recibida'))
  # Dos nuevos ingresos en la segunda descarga: el resto del archivo se superpone exactamente.
  for n in range(2):
   f=date(2026,10,25+n);a=D(27000+b*100+n*17);fc=factura(b,'N'+str(n+1),c,a,f-timedelta(days=8))
   movimientos.append(dict(banco=banco,cuenta=cuenta,caso='N'+str(n+1),objetivo='Movimiento nuevo de la segunda descarga',fecha=f.isoformat(),
    descripcion=f'Transferencia recibida de COMERCIO FANTASIA DEMO {b}',referencia=f'DEMO{b}N{n+1}',importe=str(a),tipo='Transferencia recibida'))
 # Un asiento por transferencia; ambas cuentas bancarias quedan con su contrapartida coherente.
 contables.extend([dict(caso='E16',banco=BANCOS[0][1],destino=BANCOS[1][1],fecha='2026-10-16',importe='-55000',detalle='Transferencia interna DEMO Santander a Provincia'),
  dict(caso='E16',banco=BANCOS[2][1],destino=BANCOS[0][1],fecha='2026-10-16',importe='-33000',detalle='Transferencia interna DEMO Mercado Pago a Santander')])
 for banco,cuenta in BANCOS:
  rows=sorted([m for m in movimientos if m['banco']==banco],key=lambda m:(m['fecha'],m['referencia']))
  saldo=D('0')
  for m in rows:saldo+=D(m['importe']);m['saldo']=str(saldo)
  mes=[m for m in rows if m['fecha']<='2026-10-24'];semana=[m for m in rows if m['fecha']>='2026-10-19']
  def tabla(items):
   if banco=='Santander':
    headers=['','Fecha','','Descripción','Referencia','Caja de Ahorro','Cuenta Corriente','Saldo'];inicio=14
    values=[['',datetime.fromisoformat(m['fecha']),'',m['descripcion'],m['referencia'],'',float(D(m['importe'])),float(D(m['saldo']))] for m in items]
   elif banco=='Provincia':
    headers=['Número Secuencia','Fecha','Descripción','Importe','Saldo','Descripción Extendida'];inicio=6
    values=[[m['referencia'],datetime.fromisoformat(m['fecha']),m['descripcion'],float(D(m['importe'])),float(D(m['saldo'])),m['descripcion']] for m in items]
   else:
    headers=['Fecha','Hora','Descripción','Tipo','Medio de pago','Monto'];inicio=1
    values=[[datetime.fromisoformat(m['fecha']),'12:00',m['descripcion'],m['tipo'],'Dinero disponible DEMO',float(D(m['importe']))] for m in items]
   return headers,inicio,values
  for formato,items,sufijo in [('xlsx',mes,'01_mes'),('csv',semana,'02_semana_superpuesta')]:
   path=salida/f'{banco}_DEMO_{sufijo}.{formato}';headers,inicio,values=tabla(items)
   if formato=='xlsx':
    wb=Workbook();ws=wb.active;ws.title='Movimientos DEMO';wb.properties.creator='AlfaCore DEMO';wb.properties.lastModifiedBy='AlfaCore DEMO'
    wb.properties.description='Datos enteramente ficticios. Sin datos reales ni metadatos del archivo original.'
    if inicio>1:ws.cell(1,1,f'{banco} - DEMOSTRACIÓN - DATOS FICTICIOS')
    for col,text in enumerate(headers,1):
     cell=ws.cell(inicio,col,text);cell.font=Font(bold=True,color='FFFFFF');cell.fill=PatternFill('solid',fgColor='264866')
    for row,values_row in enumerate(values,inicio+1):
     for col,v in enumerate(values_row,1):
      cell=ws.cell(row,col,v)
      if isinstance(v,datetime):cell.number_format='dd/mm/yyyy'
      elif isinstance(v,float):cell.number_format='#,##0.00'
    for column in ws.columns:ws.column_dimensions[column[0].column_letter].width=22
    ws.freeze_panes=f'A{inicio+1}';ws.auto_filter.ref=f'A{inicio}:{ws.cell(ws.max_row,len(headers)).coordinate}'
    wb.save(path)
   else:
    with path.open('w',encoding='utf-8',newline='') as out:
     writer=csv.writer(out,delimiter=';');writer.writerow(headers)
     for row in values:writer.writerow([v.strftime('%d/%m/%Y') if isinstance(v,datetime) else str(v) for v in row])
   archivos.append(dict(archivo=path.name,banco=banco,cuenta=cuenta,filas=len(items),duplicados_esperados=0 if formato=='xlsx' else len(items)-2,nuevos_esperados=len(items) if formato=='xlsx' else 2,
    sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
 manifiesto=dict(version=1,lote='DEMO-CONCILIACION-202610',servidor_destino='SERVER-ALFAVB6',base_destino=base,
  aviso='Todos los nombres, identificadores, importes y movimientos son ficticios. CUIT con prefijo 99 no válido fiscalmente. No emitir comprobantes fiscales.',
  bancos=[dict(nombre=b,codigo=c) for b,c in BANCOS],clientes=clientes,documentos=documentos,contables=contables,movimientos=movimientos,archivos=archivos)
 (salida/'manifiesto_demo.json').write_text(json.dumps(manifiesto,ensure_ascii=False,indent=2),encoding='utf-8')
 with zipfile.ZipFile(salida/'Extractos_DEMO_Conciliacion.zip','w',zipfile.ZIP_DEFLATED) as z:
  for x in archivos:z.write(salida/x['archivo'],x['archivo'])
  z.write(salida/'manifiesto_demo.json','manifiesto_demo.json')
 print(json.dumps(dict(salida=str(salida),archivos=archivos,clientes=len(clientes),documentos=len(documentos),movimientos_unicos=len(movimientos)),ensure_ascii=False,indent=2))

if __name__=='__main__':
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--salida',type=Path,required=True)
 parser.add_argument('--base',choices=['ALFANET','ALFANET_DEMO'],default='ALFANET')
 args=parser.parse_args();generar(args.salida,args.base)
