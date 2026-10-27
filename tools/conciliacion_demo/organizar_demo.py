"""Reúne extractos, guía y scripts de ALFANET_DEMO y guarda una versión inmutable del paquete."""
import argparse
from datetime import datetime
import json
from pathlib import Path
import re
import shutil
from generar_lotes import generar
from documentar_lote import documentar
from verificar_paquete import verificar
from consultar_base import consultar

def organizar(destino,version):
    if not re.fullmatch(r'[0-9A-Za-z_-]+',version):raise ValueError('Nombre de versión no válido.')
    archivo=destino/'Versiones'/version
    if archivo.exists():raise ValueError('Esa versión ya existe; elegí un nombre nuevo para conservarla.')
    origen=Path(__file__).resolve().parent
    for carpeta in ['Extractos','Guia','Scripts','Versiones','Logs']:(destino/carpeta).mkdir(parents=True,exist_ok=True)
    extractos=destino/'Extractos'
    generar(extractos,'ALFANET_DEMO')
    # La verificación se guarda fuera del paquete público.
    estado=consultar((origen/'verificar_siembra.sql').read_text(encoding='utf-8'),'ALFANET_DEMO',silencioso=True)
    (extractos/'base').mkdir(exist_ok=True)
    (extractos/'base/verificacion.json').write_text(estado,encoding='utf-8')
    documentar(extractos);verificar(extractos)
    for nombre in ['Guion_demostracion.xlsx','Hoja_de_ruta_demostracion.md']:
        shutil.copyfile(extractos/nombre,destino/'Guia'/nombre)
    shutil.copyfile(extractos/'Extractos_DEMO_Conciliacion.zip',destino/'Paquete_DEMO_Conciliacion.zip')
    for nombre in ['restablecer_demo.py','consultar_base.py','Restablecer_DEMO.cmd','crear_base_demo.sql',
        'aislar_base_demo.sql','crear_respaldo_inicial.sql','verificar_siembra.sql','requirements.txt']:
        shutil.copyfile(origen/nombre,destino/'Scripts'/nombre)
    estado_inicial={'version':version,'servidor':'SERVER-ALFAVB6','base':'ALFANET_DEMO',
        'respaldo_inicial':r'E:\MSSQL\BACKUP\ALFANET_DEMO.bak','datos_historicos_heredados':True,
        'preparado':datetime.now().astimezone().isoformat(),'verificacion':json.loads(estado)[0]}
    (destino/'Scripts/estado_inicial.json').write_text(json.dumps(estado_inicial,ensure_ascii=False,indent=2),encoding='utf-8')
    shutil.copytree(destino/'Scripts',archivo/'Scripts')
    shutil.copytree(destino/'Guia',archivo/'Guia')
    shutil.copyfile(extractos/'manifiesto_demo.json',archivo/'manifiesto_demo.json')
    shutil.copyfile(destino/'Paquete_DEMO_Conciliacion.zip',archivo/'Paquete_DEMO_Conciliacion.zip')
    (destino/'LEEME.txt').write_text('ALFANET_DEMO - Conciliación y Cobranzas\n\n'
        '1. Conectar AlfaCore a SERVER-ALFAVB6 / ALFANET_DEMO.\n'
        '2. Abrir Guia/Guion_demostracion.xlsx e importar los archivos 01_mes de Extractos.\n'
        '3. Al terminar, cerrar las pantallas de esa base y ejecutar Scripts/Restablecer_DEMO.cmd.\n'
        '4. Ingresar usuario y clave SQL; confirmar escribiendo ALFANET_DEMO.\n'
        '5. Actualizar o reconectar AlfaCore antes de la próxima demo.\n\n'
        'ALFANET mantiene sus otros usos. El restablecedor solo admite ALFANET_DEMO.\n'
        'El respaldo está en el servidor SQL, no en esta carpeta. Las versiones previas se conservan en Versiones.\n'
        'La copia contiene datos históricos heredados: usar el lote ficticio en las presentaciones.\n',encoding='utf-8')
    print('Carpeta de demostraciones preparada: '+str(destino))

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--destino',type=Path,required=True);p.add_argument('--version',required=True)
    a=p.parse_args();organizar(a.destino,a.version)
