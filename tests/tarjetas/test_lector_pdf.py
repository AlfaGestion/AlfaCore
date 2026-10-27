import importlib.util
from pathlib import Path
from unittest import TestCase, main
from unittest.mock import patch
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location("tarjetas_pdf",ROOT/"src/AlfaCore/App_Data/tarjetas/leer_pdf.py")
lector=importlib.util.module_from_spec(spec);spec.loader.exec_module(lector)
class Page:
    def __init__(self,text):self.text=text
    def extract_text(self,**kwargs):return self.text
class Reader:
    def __init__(self,text):self.pages=[Page(text)]
class LecturaTests(TestCase):
    def read(self,text,name="visa.pdf"):
        with patch.object(lector,"PdfReader",return_value=Reader(text)):
            return lector.read_pdf(b"test",name)
    def test_anulacion_con_signo_final_no_se_convierte_en_venta(self):
        r=self.read("""BCO DE LA NACION ARGENTINA
Nro Comercio: 1234
Total presentado: 50,00
Neto de pagos: 45,00
Venta ctdo 01/09/25 181127 44 0011 7098 0,0 75,00 1,00 0,00
Venta ctdo 01/09/25 181127 44 0012 7098 0,0 25,00- 0,50- 0,00
F.de Pago: el día 03/09/2025 Nro. Liq: 999 F. Pres 01/09/2025""")
        self.assertTrue(r["DetalleCompleto"])
        self.assertEqual(r["Detalles"][1]["Importe"],"-25.00")
        self.assertEqual(r["Detalles"][0]["FechaPago"],"2025-09-03")
    def test_diferencia_se_informa_sin_recalcular_el_bruto(self):
        r=self.read("""BCO DE LA NACION ARGENTINA
Total presentado: 60,00
Neto de pagos: 45,00
Venta ctdo 01/09/25 181127 44 0011 7098 0,0 50,00 1,00 0,00
F.de Pago: el día 03/09/2025 Nro. Liq: 999 F. Pres 01/09/2025""")
        self.assertFalse(r["DetalleCompleto"])
        self.assertEqual(r["Bruto"],"60.00")
        self.assertEqual(r["TotalDetalle"],"50.00")
    def test_patagonia_conserva_lote_y_cantidad_sin_inventar_cupones(self):
        r=self.read("""BANCO PATAGONIA
FECHA DE EMISION: 30/09/2025
N° DE ESTABLECIMIENTO: 1234
TOTAL PRESENTADO $ TOTAL PRESENTADO U$S
100,00 0,00
SALDO $ SALDO U$S
95,00 0,00
FECHA DE PAGO 03/09
Fecha de presentación 01/09
Liq. N° 999 - Lote N° 0044
2 Ventas en 1 pago $ 100,00""","cabal.pdf")
        self.assertTrue(r["DetalleCompleto"])
        self.assertEqual(r["Detalles"][0]["Cantidad"],2)
        self.assertEqual(r["Detalles"][0]["Lote"],"0044")
        self.assertEqual(r["Detalles"][0]["Cupon"],"")
    def test_formato_desconocido_no_se_importa(self):
        with self.assertRaises(ValueError):self.read("Documento sin liquidaciones")
    def test_hipotecario_se_detecta_por_contenido_sin_cuenta_ni_nombre_bancario(self):
        r=self.read("""Resumen Mensual de Liquidaciones
PAGADOR: 044 BANCO HIPOTECARIO
FECHA DE EMISION: 30/09/2026
N° DE ESTABLECIMIENTO: 1234
TOTAL PRESENTADO $ TOTAL PRESENTADO U$S
150,00 0,00
SALDO $ SALDO U$S
140,00 0,00
FECHA DE PAGO 01/09
Fecha de presentación 28/08
Liq. N° 999 - Lote N° 0270
2 Ventas en 6 pagos - Plan Cuotas $ 100,00
Fecha de presentación 31/08
Liq. N° 998 - Lote N° 0271
5 Ventas Tj.Débito $ 50,00
Total del día $ 150,00 $ 10,00 $ 140,00
RESUMEN DE PLANES
16 Ventas en 3 cuotas $ 20,00
9 Ventas en 6 cuotas $ 30,00""", "archivo.pdf")
        self.assertEqual(r["Banco"],"Hipotecario")
        self.assertEqual(r["Comercio"],"1234")
        self.assertTrue(r["DetalleCompleto"])
        self.assertEqual(r["TotalDetalle"],"150.00")
        self.assertEqual(len(r["Detalles"]),2)
        self.assertEqual(r["Neto"],"140.00")
        self.assertEqual(r["Detalles"][0]["Cantidad"],2)
        self.assertEqual(r["Detalles"][0]["Importe"],"100.00")
        self.assertEqual(r["Detalles"][0]["Fecha"],"2026-08-28")
        self.assertEqual(r["Detalles"][0]["FechaPago"],"2026-09-01")
        self.assertEqual(r["Detalles"][1]["Lote"],"0271")
        self.assertTrue(all(d["Cupon"]=="" for d in r["Detalles"]))
    def test_nombre_hipotecario_no_reclasifica_una_liquidacion_de_otro_banco(self):
        with self.assertRaises(ValueError):
            self.read("Resumen Mensual de Liquidaciones\nPAGADOR: 123 BANCO OTRO", "VISA HIPOTECARIO.pdf")
    def test_extracto_cuenta_corriente_no_es_liquidacion_de_tarjetas(self):
        with self.assertRaisesRegex(ValueError,"extracto de cuenta corriente"):
            self.read("DETALLE DE MOVIMIENTOS\nCUENTA CORRIENTE EN PESOS\nBanco Hipotecario S.A.","TOR HIPOTECARIO CTA CTE.pdf")
    def test_nacion_formato_pagador_no_confunde_aranceles_con_ventas(self):
        r=self.read("""Resumen Mensual de Liquidaciones
PAGADOR: 511 BANCO DE LA NACION ARGENTINA
FECHA DE EMISION: 30/04/2026
N° DE ESTABLECIMIENTO: 1234
TOTAL PRESENTADO $ TOTAL PRESENTADO U$S
150,00 0,00
SALDO $ SALDO U$S
140,00 0,00
FECHA DE PAGO 01/04 Arancel $ 2,00
Fecha de presentación 31/03
Liq. N° 00322102 - Lote N° 0011
1 Venta en 3 pagos - Cuotas MiPyME 3 $ 100,00 Serv.Cobro Anticipado $ 1,00
Liq. N° 00322104 - Lote N° 0011
2 Ventas Tj.Débito $ 50,00
Total del día $ 150,00 $ 10,00 $ 140,00
16 Ventas en 3 cuotas $ 20,00""", "documento.pdf")
        self.assertEqual(r["Banco"],"Nación")
        self.assertTrue(r["DetalleCompleto"])
        self.assertEqual(r["Neto"],"140.00")
        self.assertEqual(len(r["Detalles"]),2)
        self.assertEqual(r["Detalles"][0]["Importe"],"100.00")
        self.assertEqual(r["Detalles"][0]["Fecha"],"2026-03-31")
        self.assertEqual(r["Detalles"][0]["FechaPago"],"2026-04-01")
        self.assertEqual(r["Detalles"][1]["Liquidacion"],"00322104")
        self.assertTrue(all(d["Nivel"]=="Lote" and d["Cupon"]=="" for d in r["Detalles"]))
    def test_resumen_cuenta_bna_no_se_importa_como_tarjetas(self):
        with self.assertRaisesRegex(ValueError,"extracto de cuenta corriente"):
            self.read("RESUMEN DE CUENTA\nRESUMEN DE EMISION MENSUAL\nCUENTA CORRIENTE- PESOS\nFECHA MOVIMIENTOS COMPROB. DEBITOS CREDITOS SALDO", "documento.pdf")
if __name__=="__main__":main()
