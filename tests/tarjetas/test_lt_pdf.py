import importlib.util
from decimal import Decimal
from pathlib import Path
from unittest import TestCase

spec=importlib.util.spec_from_file_location("lector_lt",Path(__file__).resolve().parents[2]/"src/AlfaCore/App_Data/tarjetas/leer_pdf.py")
lector=importlib.util.module_from_spec(spec);spec.loader.exec_module(lector)

class LiquidacionLtTests(TestCase):
    def test_nacion_separa_liquidaciones_no_repite_encabezado_mensual(self):
        text="""BCO DE LA NACION ARGENTINA
Total presentado: 200,00
+ VENTAS C/DESCUENTO CONTADO $ 100,00
- ARANCEL $ 10,00
- IVA CRED.FISC.COMERCIO S/ARANC 21,00% $ 2,10
IMPORTE NETO DE PAGOS $ 87,90
F.de Pago: el día 03/09/2025 $ 87,90 Nro. Liq: 123
Total presentado: 200,00
+ VENTAS C/DESCUENTO CONTADO $ 100,00
- ARANCEL $ 10,00
- DESCUENTO DESCONOCIDO $ 1,00
IMPORTE NETO DE PAGOS $ 89,00
F.de Pago: el día 04/09/2025 $ 89,00 Nro. Liq: 124
"""
        ls=lector.liquidaciones_lt(text,"Nación")
        self.assertEqual([l['Numero'] for l in ls],['123','124'])
        for l in ls:self.assertEqual(sum(Decimal(c['Importe']) for c in l['Conceptos']),Decimal(l['Neto']))
        self.assertEqual(ls[1]['Conceptos'][-1]['Rol'],'SinClasificar')
        self.assertEqual(ls[0]['Conceptos'][-1]['Importe'],'-2.10')

    def test_no_usa_un_cierre_sin_neto_o_sin_desglose(self):
        self.assertEqual([],lector.liquidaciones_lt('BCO DE LA NACION\nF.de Pago: el día 03/09/2025 Nro. Liq: 1','Nación'))

    def test_patagonia_no_duplica_arancel_base_ni_constancia_y_no_reparte(self):
        text="""FECHA DE EMISION: 30/09/2025
N° DE RESUMEN: 999
Arancel Tj.Crédito 1,80 % $ 10,00
Base Imponible IVA Monto Gravado
Tasa 21,00 % $ 10,00
Deducciones Impositivas
IVA 21,00 % $ 2,10
Ret.IB SIRTAC
2,50 % $ 2,50
Percep./Retenc.AFIP - DGI $ 0,30
Fin de la información
Constancia Mensual
Total Regimen 493 $ 0,30
"""
        ls=lector.resumen_lt(text,{'Bruto':'100.00','Neto':'85.10'})
        self.assertEqual(1,len(ls));l=ls[0]
        self.assertTrue(l['EsResumenMensual'])
        self.assertEqual(Decimal('85.10'),sum(Decimal(c['Importe']) for c in l['Conceptos']))
        self.assertEqual(1,sum(c['Rol']=='Gastos' for c in l['Conceptos']))
