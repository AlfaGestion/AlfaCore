"""Casos sintéticos sin datos de clientes. Ejecutar con unittest discover y PYTHONDONTWRITEBYTECODE=1."""
import importlib.util
from pathlib import Path
import unittest
from types import SimpleNamespace
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('extracto_pdf', Path(__file__).resolve().parents[2] / 'src/AlfaCore/App_Data/extractos/leer_pdf.py')
pdf = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pdf)


class PdfBancarioTests(unittest.TestCase):
    def read_text(self, text):
        with patch.object(pdf, 'PdfReader', return_value=SimpleNamespace(pages=[SimpleNamespace(extract_text=lambda **kwargs: text)])):
            return pdf.read_pdf(b'prueba')

    def test_pdf_escaneado_informa_que_no_tiene_texto(self):
        with self.assertRaisesRegex(ValueError, 'no contiene texto'):
            self.read_text('')

    def test_liquidacion_de_tarjetas_no_se_confunde_con_extracto(self):
        with self.assertRaisesRegex(ValueError, 'formato PDF no reconocido'):
            self.read_text('LIQUIDACION VISA BANCO DE LA NACION ARGENTINA Total presentado 100,00')

    def test_dos_cuentas_no_se_importan_juntas(self):
        with self.assertRaisesRegex(ValueError, 'más de una cuenta'):
            self.read_text('CUENTA CORRIENTE- PESOS 0110000000000000000000 0110000000000000000001')

    def test_nacion_signos_saldos_negativos_y_transporte(self):
        header = 'FECHA MOVIMIENTOS COMPROB. DEBITOS CREDITOS SALDO\n'
        pages = [header + '01/04/26 TRANSFERENCIA 1234 50,00 50,00-SALDO ANTERIOR 100,00-\n'
                 '02/04/26 COMISION 2345 10,00 60,00-TRANSPORTE 60,00-',
                 header + '03/04/26 COBRO 3456 100,00 40,00TRANSPORTE 60,00-\nSALDO FINAL 40,00']
        rows, initial, final = pdf.nacion(pages)
        self.assertEqual(['50.00', '-10.00', '100.00'], [r['Amount'] for r in rows])
        self.assertEqual((-100, 40), (initial, final))
        self.assertEqual('3456', rows[-1]['ExternalReference'])

    def test_nacion_fila_ilegible_no_se_omite(self):
        text = 'FECHA MOVIMIENTOS COMPROB. DEBITOS CREDITOS SALDO\nSALDO ANTERIOR 100,00\n01/04/26 MOVIMIENTO SIN IMPORTE\nSALDO FINAL 100,00'
        with self.assertRaisesRegex(ValueError, 'importe y saldo'):
            pdf.nacion([text])

    def test_nacion_importe_no_se_infiere_si_no_coincide_con_saldo(self):
        text = 'FECHA MOVIMIENTOS COMPROB. DEBITOS CREDITOS SALDO\nSALDO ANTERIOR 100,00\n01/04/26 COBRO 1234 20,00 125,00\nSALDO FINAL 125,00'
        with self.assertRaisesRegex(ValueError, 'no coinciden'):
            pdf.nacion([text])

    def test_nacion_saldo_final_incompleto_rechaza_todo(self):
        text = 'FECHA MOVIMIENTOS COMPROB. DEBITOS CREDITOS SALDO\nSALDO ANTERIOR 100,00\n01/04/26 COBRO 1234 20,00 120,00\nSALDO FINAL 200,00'
        with self.assertRaisesRegex(ValueError, 'saldo final'):
            pdf.nacion([text])

    @staticmethod
    def hipotecario(final='130.00'):
        header = 'FECHA'.ljust(15) + 'DESCRIPCION'.ljust(61) + 'SUC.'.ljust(12) + 'REFERENCIA'.ljust(39) + 'DEBITOS'.ljust(20) + 'CREDITOS'.ljust(27) + 'SALDO'
        def row(day, desc, value, end, ref=''):
            return (day.ljust(15) + desc.ljust(61) + '40'.ljust(12) + ref).ljust(end-len(value)) + value
        return '\n'.join(['SALDO INICIAL CREDITOS DEBITOS I.V.A. SALDO FINAL', f'$ 100.00 $ 50.00 $ 19.00 $ 1.00 $ {final}', header,
            row('01/08/2026', 'SALDO INICIAL', '100.00', 180),
            row('02/08/2026', 'COMISION', '19.00', 136, '5 Comp. 1234'),
            row('02/08/2026', 'IVA', '1.00', 137, '5 Comp. 1234'),
            row('02/08/2026', 'TRANSFERENCIA', '50.00', 158, '8 Comp. 4567'),
            ' '.ljust(15)+'CLIENTE DE EJEMPLO',
            row('02/08/2026', 'SALDO FINAL DEL DIA', '130.00', 180)])

    def test_hipotecario_columnas_iva_y_descripcion_continuada(self):
        rows, initial, final = pdf.hipotecario([self.hipotecario()])
        self.assertEqual(['-19.00', '-1.00', '50.00'], [r['Amount'] for r in rows])
        self.assertEqual((100, 130), (initial, final))
        self.assertEqual('8 Comp. 4567', rows[-1]['ExternalReference'])
        self.assertIn('CLIENTE DE EJEMPLO', rows[-1]['Description'])

    def test_hipotecario_totales_distintos_rechazan_todo(self):
        with self.assertRaisesRegex(ValueError, 'no coinciden'):
            pdf.hipotecario([self.hipotecario('140.00')])
