import json
import unittest
from unittest.mock import patch
import restablecer_demo as demo
from consultar_base import parsear_tabla_sqlcmd

def header(**cambios):
    fila=dict(DatabaseName='ALFANET_DEMO',BackupType='1',Position='1',HasBackupChecksums='1',BackupSetGUID='DEMO-UUID',BackupFinishDate='2026-10-26')
    fila.update(cambios);return fila

def archivos():
    return [dict(Type='D',LogicalName='ALFANET',PhysicalName=demo.DATOS),dict(Type='L',LogicalName='ALFANET_log',PhysicalName=demo.LOG)]

class RestablecerDemoTests(unittest.TestCase):
    def test_solo_respaldo_completo_demo_con_checksum(self):
        self.assertEqual('ALFANET_DEMO',demo.validar_header([header()])['DatabaseName'])
        for cambio in [dict(DatabaseName='ALFANET'),dict(BackupType='2'),dict(HasBackupChecksums='0'),dict(Position='2')]:
            with self.subTest(cambio=cambio),self.assertRaises(ValueError):demo.validar_header([header(**cambio)])
        for filas in [[],[header(),header()]]:
            with self.assertRaises(ValueError):demo.validar_header(filas)

    def test_archivos_de_origen_o_extras_son_rechazados(self):
        self.assertEqual(2,len(demo.validar_archivos(archivos())))
        origen=archivos();origen[0]['PhysicalName']=r'E:\MSSQL\DATA\ALFANET.mdf'
        with self.assertRaises(ValueError):demo.validar_archivos(origen)
        with self.assertRaises(ValueError):demo.validar_archivos(archivos()[:1])
        with self.assertRaises(ValueError):demo.validar_archivos(archivos()+archivos()[:1])

    def test_servidor_incorrecto_se_detiene_antes_de_validar_respaldo(self):
        with patch.object(demo,'objeto',return_value=dict(servidor='OTRO',estado='ONLINE')),patch.object(demo,'consultar') as consulta:
            with self.assertRaises(ValueError):demo.prevalidar()
            consulta.assert_not_called()

    def test_restauracion_solo_emite_destino_demo_y_verifica_resultado(self):
        esperado=dict(clientes_demo=60,comprobantes_demo=333,renglones_demo=679,importaciones_demo=0)
        with patch.object(demo,'sql') as ejecutar,patch.object(demo,'estado_demo',return_value=esperado):
            self.assertEqual(esperado,demo.restaurar(dict(archivos=archivos())))
            texto=ejecutar.call_args.args[0]
            self.assertIn('RESTORE DATABASE [ALFANET_DEMO]',texto)
            self.assertNotIn('RESTORE DATABASE [ALFANET]',texto)
            self.assertNotIn('ALTER DATABASE [ALFANET]',texto)
            self.assertIn('SET MULTI_USER',texto)
        with patch.object(demo,'sql'),patch.object(demo,'estado_demo',return_value=dict(importaciones_demo=1)):
            with self.assertRaises(RuntimeError):demo.restaurar(dict(archivos=archivos()))

    def test_lector_tabular_no_acepta_resultados_truncados(self):
        tabla='DatabaseName\tBackupType\n------------\t----------\nALFANET_DEMO\t1\n'
        self.assertEqual([dict(DatabaseName='ALFANET_DEMO',BackupType='1')],parsear_tabla_sqlcmd(tabla))
        with self.assertRaises(ValueError):parsear_tabla_sqlcmd(tabla+'fila truncada\n')

if __name__=='__main__':unittest.main()
