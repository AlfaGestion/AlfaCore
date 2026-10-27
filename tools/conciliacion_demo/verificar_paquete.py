"""Verificación local del paquete ficticio. No conecta a SQL."""
import argparse
import json
import re
import zipfile
from pathlib import Path
from openpyxl import load_workbook

def verificar(salida):
    m=json.loads((salida/'manifiesto_demo.json').read_text(encoding='utf-8'))
    permitidos={a['archivo'] for a in m['archivos']}|{'manifiesto_demo.json','Guion_demostracion.xlsx','Hoja_de_ruta_demostracion.md'}
    with zipfile.ZipFile(salida/'Extractos_DEMO_Conciliacion.zip') as z:
        assert set(z.namelist())==permitidos,'El ZIP contiene archivos fuera de la lista pública'
    for c in m['clientes']:
        assert 'DEMO' in c['nombre']
        cuit=re.sub(r'\D','',c['cuit'])
        assert len(cuit)==11 and cuit.startswith('99')
    for p in salida.glob('*.xlsx'):
        wb=load_workbook(p)
        assert wb.properties.creator=='AlfaCore DEMO'
        assert wb.properties.lastModifiedBy=='AlfaCore DEMO'
        for ws in wb:
            assert ws.sheet_state=='visible'
            for row in ws:
                for c in row:
                    assert c.comment is None and c.hyperlink is None
        wb.close()
        with zipfile.ZipFile(p) as z:
            assert not any('externalLinks/' in n or 'vbaProject' in n or 'xl/media/' in n for n in z.namelist())
    print(f'Paquete público correcto: {len(permitidos)} archivos; autores ficticios; sin hojas ocultas, comentarios, enlaces, imágenes ni macros. CUIT ficticios: {len(m["clientes"])}.')

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--salida',type=Path,required=True)
    verificar(p.parse_args().salida)
