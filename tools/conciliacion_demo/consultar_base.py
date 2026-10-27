"""Consulta de diagnóstico; obtiene la credencial guardada sin imprimirla."""
import argparse
import json
import os
import subprocess
import tempfile
from pathlib import Path

def consultar(sql, base='ALFANET', salida=None, *, tabla=False, silencioso=False):
    usuario=os.environ.get('ALFACORE_DEMO_SQL_USER')
    clave=os.environ.get('ALFACORE_DEMO_SQL_PASSWORD')
    if bool(usuario)!=bool(clave):raise ValueError('La conexión explícita requiere usuario y clave en el entorno.')
    if not usuario or not clave:
        ruta=Path(__file__).resolve().parents[2]/'src/AlfaCore/App_Data/sessions.json'
        sesiones=json.loads(ruta.read_text(encoding='utf-8-sig'))['Sessions']
        sesion=next(s for s in sesiones if s['Servidor'].upper()=='SERVER-ALFAVB6')
        usuario,clave=sesion['Usuario'],sesion['Password']
    env=os.environ.copy();env['SQLCMDPASSWORD']=clave
    cmd=['sqlcmd','-S','SERVER-ALFAVB6','-U',usuario,'-d',base,'-l','10','-t','120','-b','-u','-w','65535']
    cmd+=['-s','\t','-W','-h','65535'] if tabla else ['-y','0']
    with tempfile.TemporaryDirectory(prefix='alfacore-demo-query-') as temp:
        output=Path(temp)/'resultado.txt'
        query=Path(temp)/'consulta.sql'
        query.write_text('SET NOCOUNT ON; '+sql,encoding='utf-8')
        result=subprocess.run(cmd+['-f','65001','-i',str(query),'-o',str(output)],env=env,capture_output=True)
        raw=output.read_bytes() if output.exists() else result.stdout
        texto=raw.decode('utf-16' if raw.startswith((b'\xff\xfe',b'\xfe\xff')) else 'utf-8',errors='replace').strip()
    if result.returncode:raise RuntimeError(texto+' '+result.stderr.decode('utf-8',errors='replace'))
    if tabla:
        return parsear_tabla_sqlcmd(texto)
    if texto.startswith('['):texto=''.join(texto.splitlines())
    if salida:Path(salida).write_text(texto,encoding='utf-8')
    elif not silencioso:print(texto)
    return texto

def parsear_tabla_sqlcmd(texto):
    lineas=[line for line in texto.splitlines() if line.strip()]
    if len(lineas)<2:raise ValueError('SQLCMD no devolvió una tabla con encabezados.')
    encabezados=[v.strip() for v in lineas[0].split('\t')]
    if len(encabezados)!=len(set(encabezados)):raise ValueError('Encabezados SQL ambiguos.')
    if any(c not in '-\t ' for c in lineas[1]):raise ValueError('Separador de tabla SQL no reconocido.')
    filas=[]
    for linea in lineas[2:]:
        partes=[v.strip() for v in linea.split('\t')]
        if len(partes)!=len(encabezados):raise ValueError('La tabla SQL está truncada o contiene separadores inesperados.')
        filas.append(dict(zip(encabezados,partes)))
    return filas

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--sql',type=Path,required=True);p.add_argument('--salida',type=Path);p.add_argument('--base',default='ALFANET')
    a=p.parse_args();consultar(a.sql.read_text(encoding='utf-8-sig'),a.base,a.salida)
