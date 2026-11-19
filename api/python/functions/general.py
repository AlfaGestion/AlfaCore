from flask import jsonify
from configs.connection import get_connection
import os


def get_format_response(sql: str, name_error: str, return_list: bool = False):
    """
    Retorna un diccionario con los elementos de la consulta
    """
    result = []
    error = False
    try:
        sql_conn = get_connection()
        if sql_conn in ('', None):
            raise RuntimeError('No se pudo obtener la conexi?n principal.')
        cursor = sql_conn.cursor().execute(sql)
        columns = [column[0] for column in cursor.description]
        for row in cursor.fetchall():
            result.append(dict(zip(columns, row)))

    except Exception as e:
        error = True
        print(f"Ocurrió un error al obtener {name_error}: ", e)
    finally:

        if error:
            if return_list:
                return list(), True
            response = jsonify(
                {"error": f"Ocurrió un error al obtener {name_error}"})
            response.status_code = 500
        else:
            if return_list:
                return result, False
            response = jsonify({"data": result})

        return response


def get_content_sql_file(filename, parameters: dict) -> str:
    """
    Esta función abre un archivo sql, obtiene su contenido y reemplaza los parametros.
    La variable parameters debe tener la siguiente estructura
    {
        '#CAMPO_EN_SQL': 'VALOR_DADO'
    }
    """

    # Por las dudas, verifico si el archivo tiene extension sql, si no, la agrego
    path, extension = os.path.splitext(filename)
    if extension != '.sql':
        filename = filename + '.sql'

    file = open(f'./stores/{filename}')
    content = file.read()
    file.close()

    for key in parameters:
        content = content.replace(f'{key}', parameters[key])

    return content
