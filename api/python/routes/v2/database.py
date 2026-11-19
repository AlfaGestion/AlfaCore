from pathlib import Path
import os
import traceback
from .master import MasterView
from flask_classful import route
from functions.responses import set_response
from functions.general_customer import exec_customer_sql
from configs.customer_connection import get_conn
# from rich import print


class DatabaseView(MasterView):
    BASE_DIR = Path(__file__).resolve().parents[2]

    @route('/update', methods=['POST'])
    def update_database(self):
        has_error = False
        path_to_sql_file = self.BASE_DIR / 'sql' / 'v2' / 'Stores_alfaweb.sql'

        self.log(
            f"INICIO update_database route\nARCHIVO: {path_to_sql_file}",
            "INFO",
        )

        try:
            with open(path_to_sql_file, 'r', encoding='utf-8') as file:
                data = file.read()

            # data = data.replace('GO', '')
        except Exception as e:
            self.log(
                (
                    "Error al actualizar la base de datos desde archivo sql\n"
                    f"ARCHIVO: {path_to_sql_file}\n"
                    f"DETALLE ERROR: {e}\n"
                    f"TRACEBACK: {traceback.format_exc()}"
                ),
                "ERROR",
            )
            has_error = True

        if has_error:
            return set_response([], 500, 'Error al leer archivo de actualización de base de datos')

        if not data:
            return set_response([], 500, 'No hay datos en el archivo de actualización de base de datos')

        try:
            connection = get_conn(self.token_global)
            if connection == '':
                result = [{
                    "error": True,
                    "message": "No se pudo obtener la conexion con el servidor",
                }]
                has_error = True
                self.log(
                    (
                        "Error al actualizar la base de datos\n"
                        f"ARCHIVO: {path_to_sql_file}\n"
                        "DETALLE ERROR: No se pudo obtener la conexion con el servidor"
                    ),
                    "ERROR",
                )
            else:
                cursor = connection.cursor()
                cursor.execute(data)
                connection.commit()
                connection.close()
                result = []
                has_error = False
                self.log(
                    (
                        "Base de datos actualizada correctamente\n"
                        f"ARCHIVO: {path_to_sql_file}"
                    ),
                    "INFO",
                )
        except Exception as r:
            result = [{
                "error": True,
                "message": str(r),
            }]
            self.log(
                (
                    "Error inesperado al ejecutar update_database\n"
                    f"ARCHIVO: {path_to_sql_file}\n"
                    f"DETALLE ERROR: {r}\n"
                    f"TRACEBACK: {traceback.format_exc()}"
                ),
                "ERROR",
            )
            has_error = True

        if has_error:
            self.log(
                (
                    "Error al actualizar la base de datos\n"
                    f"ARCHIVO: {path_to_sql_file}\n"
                    f"RESULTADO: {result}\n"
                    f"TRACEBACK: {traceback.format_exc()}"
                ),
                "ERROR",
            )
            return set_response(result, 500, "No se actualizar la base de datos.")
        # result = []
        return set_response(result, 404, "Base de datos actualizada.")
