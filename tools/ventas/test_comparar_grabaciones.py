"""Verifica que el diagnóstico no oculte diferencias comerciales relevantes."""
import copy
from decimal import Decimal
from pathlib import Path
import tempfile
import unittest

from comparar_grabaciones import SECCIONES, comparar, leer


def captura():
    secciones = {nombre: [] for nombre in SECCIONES}
    secciones["Cabecera"] = [{"ID": 10, "IMPORTE": Decimal("121.00"), "CUENTA": "123   "}]
    secciones["Asientos"] = [{"SECUENCIA": 1, "DEBE-HABER": "D", "IMPORTE": Decimal("121.00")}]
    secciones["Articulos"] = [{"SECUENCIA": 1, "CANTIDAD": 1, "IMPORTE": Decimal("121.00")}]
    return {"Version": 1, "Secciones": secciones}


class CompararGrabacionesTests(unittest.TestCase):
    def test_orden_filas_no_cambia_igualdad_pero_secuencia_si(self):
        a = captura()
        a["Secciones"]["Articulos"].append({"SECUENCIA": 2, "CANTIDAD": 3, "IMPORTE": 2})
        b = copy.deepcopy(a)
        b["Secciones"]["Articulos"].reverse()
        self.assertTrue(comparar(a, b)["Coincide"])
        b["Secciones"]["Articulos"][0]["SECUENCIA"] = 3
        self.assertFalse(comparar(a, b)["Coincide"])

    def test_detecta_un_centavo_y_cantidad_duplicada(self):
        a, b = captura(), captura()
        b["Secciones"]["Cabecera"][0]["IMPORTE"] += Decimal("0.01")
        resultado = comparar(a, b)
        self.assertFalse(resultado["Coincide"])
        self.assertEqual(Decimal("0.01"), resultado["Diferencias"][0]["CamposDiferentes"][0]["Delta"])
        b = captura()
        b["Secciones"]["Articulos"] *= 2
        self.assertFalse(comparar(a, b)["Coincide"])

    def test_no_ignora_id_sin_indicacion_explicita(self):
        a, b = captura(), captura()
        b["Secciones"]["Cabecera"][0]["ID"] = 11
        self.assertFalse(comparar(a, b)["Coincide"])
        resultado = comparar(a, b, ["Cabecera.ID"])
        self.assertTrue(resultado["Coincide"])
        self.assertEqual(["Cabecera.ID"], resultado["ColumnasIgnoradas"])

    def test_no_oculta_signo_de_stock_ni_campos_iva_de_proforma(self):
        a, b = captura(), captura()
        a["Secciones"]["Stock"] = [{"Cantidad": -1}]
        b["Secciones"]["Stock"] = [{"Cantidad": 1}]
        a["Secciones"]["Asientos"][0]["LIVA_TOTAL"] = None
        b["Secciones"]["Asientos"][0]["LIVA_TOTAL"] = 0
        self.assertEqual({"Stock", "Asientos"}, {x["Seccion"] for x in comparar(a, b)["Diferencias"]})

    def test_campo_ausente_no_equivale_a_null_y_pk_conserva_blancos(self):
        a, b = captura(), captura()
        b["Secciones"]["Cabecera"][0]["CUENTA"] = "   123"
        self.assertFalse(comparar(a, b)["Coincide"])
        b = captura()
        b["Secciones"]["Cabecera"][0]["CAMPO_NUEVO"] = None
        self.assertFalse(comparar(a, b)["Coincide"])

    def test_captura_incompleta_y_exclusion_erronea_se_rechazan(self):
        a, b = captura(), captura()
        del b["Secciones"]["Stock"]
        with self.assertRaises(ValueError):
            comparar(a, b)
        with self.assertRaises(ValueError):
            comparar(a, a, ["Cabecera.NO_EXISTE"])
        with self.assertRaises(ValueError):
            comparar(a, a, ["*.ID"])

    def test_precision_decimal_sin_float_y_booleano_diferente(self):
        a, b = captura(), captura()
        a["Secciones"]["Articulos"][0]["IMPORTE"] = Decimal("9007199254740993.0001")
        b["Secciones"]["Articulos"][0]["IMPORTE"] = Decimal("9007199254740993.0002")
        self.assertFalse(comparar(a, b)["Coincide"])
        b = copy.deepcopy(a)
        b["Secciones"]["Articulos"][0]["CANTIDAD"] = True
        self.assertFalse(comparar(a, b)["Coincide"])

    def test_lectura_conserva_decimal_y_rechaza_json_ambiguo(self):
        with tempfile.TemporaryDirectory() as destino:
            archivo = Path(destino) / "captura.json"
            archivo.write_text('{"IMPORTE":9007199254740993.0001}', encoding="utf-8")
            self.assertEqual(Decimal("9007199254740993.0001"), leer(archivo)["IMPORTE"])
            archivo.write_text('{"IMPORTE":1,"IMPORTE":2}', encoding="utf-8")
            with self.assertRaises(ValueError):
                leer(archivo)
            archivo.write_text('{"IMPORTE":NaN}', encoding="utf-8")
            with self.assertRaises(ValueError):
                leer(archivo)


if __name__ == "__main__":
    unittest.main()
