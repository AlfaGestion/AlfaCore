"""Ejecuta el BAT real sobre instalaciones ficticias; nunca publica ni usa destinos remotos."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


@unittest.skipUnless(os.name == "nt", "Requiere cmd y robocopy de Windows")
class ActualizarInstalacionTests(unittest.TestCase):
    def setUp(self):
        self.temporal = tempfile.TemporaryDirectory(prefix="AlfaCore_deploy_test_")
        self.root = Path(self.temporal.name).resolve()
        # La limpieza recursiva queda limitada al directorio temporal creado para este ensayo.
        self.assertEqual(self.root.parent, Path(tempfile.gettempdir()).resolve())
        self.addCleanup(self.temporal.cleanup)
        self.script = self.root / "scripts" / "actualizar_instalacion.bat"
        self.script.parent.mkdir()
        origen = Path(__file__).resolve().parents[2] / "scripts" / self.script.name
        shutil.copyfile(origen, self.script)
        self.source = self.root / "publish" / "AlfaCoreLAN"
        self.dest = self.root / "instalacion con espacios"
        self.escribir(self.source / "AlfaCore.dll", "binario nuevo de ensayo")
        self.escribir(self.dest / "AlfaCore.dll", "anterior")
        for carpeta in ("extractos", "tarjetas"):
            self.escribir(self.source / "App_Data" / carpeta / "leer_pdf.py", f"# lector nuevo {carpeta}")
            self.escribir(self.dest / "App_Data" / carpeta / "leer_pdf.py", "# lector anterior")
            self.escribir(self.dest / "App_Data" / carpeta / "local.txt", "conservar")
            self.escribir(self.source / "App_Data" / carpeta / "privado.pdf", "no distribuir")
        self.preservados = {
            "appsettings.json": '{"Conexion":"PROPIA"}',
            "appsettings.Production.json": '{"Legacy":true}',
            ".env": "CONFIGURACION_PROPIA=1",
            "web.config": "puerto propio",
            "App_Data/uploads/documento.pdf": "documento del servidor",
            "App_Data/sesiones.json": "sesiones del servidor",
            "wwwroot/uploads/imagen.png": "imagen del servidor",
        }
        for ruta, contenido in self.preservados.items():
            self.escribir(self.dest / ruta, contenido)
            self.escribir(self.source / ruta, "NO REEMPLAZAR")

    @staticmethod
    def escribir(ruta, contenido):
        ruta.parent.mkdir(parents=True, exist_ok=True)
        ruta.write_text(contenido, encoding="utf-8")

    def ejecutar(self):
        runner = self.root / "ejecutar_ensayo.bat"
        runner.write_bytes(b'@echo off\r\ncall scripts\\actualizar_instalacion.bat "instalacion con espacios" /nopublish\r\n')
        return subprocess.run(
            ["cmd.exe", "/d", "/c", str(runner)],
            input="S\n", capture_output=True, text=True, encoding="utf-8", errors="replace",
            timeout=30, cwd=self.root,
        )

    def comprobar_preservados(self):
        for ruta, contenido in self.preservados.items():
            self.assertEqual((self.dest / ruta).read_text(encoding="utf-8"), contenido, ruta)

    def test_copia_lectores_y_preserva_configuracion_y_documentos_incluso_al_repetir(self):
        for _ in range(2):
            resultado = self.ejecutar()
            self.assertEqual(resultado.returncode, 0, resultado.stdout + resultado.stderr)
            self.assertEqual((self.dest / "AlfaCore.dll").read_text(), "binario nuevo de ensayo")
            for carpeta in ("extractos", "tarjetas"):
                destino = self.dest / "App_Data" / carpeta
                self.assertEqual((destino / "leer_pdf.py").read_text(), f"# lector nuevo {carpeta}")
                self.assertEqual((destino / "local.txt").read_text(), "conservar")
                self.assertFalse((destino / "privado.pdf").exists())
            self.comprobar_preservados()

    def test_publicacion_incompleta_aborta_antes_de_reemplazar_binarios(self):
        (self.source / "App_Data" / "tarjetas" / "leer_pdf.py").unlink()
        resultado = self.ejecutar()
        self.assertNotEqual(resultado.returncode, 0, resultado.stdout)
        self.assertIn("Falta App_Data\\tarjetas\\leer_pdf.py", resultado.stdout)
        self.assertEqual((self.dest / "AlfaCore.dll").read_text(), "anterior")
        self.comprobar_preservados()

    def test_error_de_copia_impide_informar_actualizacion_exitosa(self):
        # Una carpeta ocupa el lugar donde debe copiarse el archivo del lector.
        destino = self.dest / "App_Data" / "extractos" / "leer_pdf.py"
        destino.unlink()
        destino.mkdir()
        resultado = self.ejecutar()
        self.assertNotEqual(resultado.returncode, 0, resultado.stdout)
        self.assertIn("No inicies el sitio/servicio", resultado.stdout)
        self.assertNotIn("Actualizacion completada de punta a punta", resultado.stdout)
        self.comprobar_preservados()


if __name__ == "__main__":
    unittest.main()
