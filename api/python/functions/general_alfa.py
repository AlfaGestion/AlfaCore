import os
from flask_mail import Mail, Message
from flask import render_template, abort, jsonify
from functions.general_customer import exec_customer_sql
from config import CTA_MAIL
from functions.Log import Log
from configs.connection_alfa import get_conn_alfa


def get_path_tasks_file(is_local: bool = False):
    if is_local:
        return 'C:\\TAREAS_CLIENTE'
    else:
        return r'\\Server-alfavb6\c\TAREAS_CLIENTE'


def get_path_tasks_file_both():
    #return 'C:\\TAREAS_CLIENTE', r'\\Server-alfavb6\c\TAREAS_CLIENTE' # PRODUCTION
    return 'C:\\TAREAS_CLIENTE', 'C:\\TAREAS_CLIENTE'


def mkdir(path):

    if os.path.isdir(path):
        return True
    else:
        try:
            os.mkdir(path)
            return True
        except OSError as error:
            raise Exception(f"Error al crear el directorio {path} : {error}")


def get_extension_and_b64string_file(file):
    """
    data:application/pdf;base64, => PDF
    """
    extension = ""
    b64string = ""

    index = file.find('base64,')
    if index > 0:
        index += 7
        b64string = file[index:]
        tmp_extension = file[0:index]

        if tmp_extension == "data:application/pdf;base64,":
            extension = ".pdf"
        elif tmp_extension == "data:text/plain;base64,":
            extension = ".txt"
        elif tmp_extension == "data:application/vnd.openxmlformats-officedocument.wordprocessingml.document;base64,":
            extension = ".docx"
        elif tmp_extension == "data:application/vnd.ms-excel;base64,":
            extension = ".csv"
        elif tmp_extension == "data:image/jpeg;base64,":
            extension = ".jpeg"
        elif tmp_extension == "data:application/octet-stream;base64,":
            extension = ".rpt"
        elif tmp_extension == "data:application/vnd.openxmlformats-officedocument.spreadsheetml.sheet;base64,":
            extension = ".xlsx"
        if tmp_extension == "data:image/png;base64,":
            extension = ".png"
        if tmp_extension == "data:image/gif;base64,":
            extension = ".gif"
        if tmp_extension == "data:image/bmp;base64,":
            extension = ".bmp"
    else:
        b64string = file
        extension = ".jpg"

    return b64string, extension


def insert_attachment(idcpte, filename,token):
    """
    Inserta adjuntos del comprobante
    """
    try:
        sql = f"""
        INSERT INTO V_MV_CPTEDOC (TC,IDCOMPROBANTE,IDCOMPLEMENTO,DOCUMENTO,Transmision,Recepcion) VALUES ('OT','{idcpte}',0,'{filename}',0,0)
        """

        # result, error = exec_customer_sql(sql, "", "", False, True)
        result, error = exec_customer_sql(sql, " al insertar adjunto ", token)

        if error:
            Log.create(f"Error al insertar archivo : {result}")
            print(result)
        #     return True
        # return False
    except Exception as e:
        Log.create(f"Error al insertar archivo : {e}")
        print(
            f"Ocurrió un error al insertar el comprobante {idcpte} documento {filename}: ", e)
        # return True


def send_email(to: str, body_task: str = '', action: str = 'R'):
    """
    Acciones disponibles
    R = Tarea registrada
    C = Respuesta enviada
    """
    if to == '':
        return

    mail = Mail()
    msg = Message('Notificación web', sender=CTA_MAIL, recipients=[to])
    msg.html = render_template(
        'mail_response.html', type=action, body=body_task)
    try:
        mail.send(msg)
    except Exception as e:
        # print(f"Error al enviar email: {e}")
        Log.create(f"Error al enviar email: {e}", "EMAIL")


def exec_sql(sql: str, name_error: str = "", return_list: bool = True):
    """
    Retorna un diccionario con los elementos de la consulta
    """

    result = []
    error = False
    error_message = ''
    try:
        connection = get_conn_alfa()

        if connection == '':
            result.append({
                'error': 'true',
                'message': 'Error en la conexión con el servidor'
            })
            error_message = 'No se pudo obtener la conexión con el servidor'
            error = True

        cursor = connection.cursor().execute(sql)
        columns = [column[0] for column in cursor.description]
        for row in cursor.fetchall():
            result.append(dict(zip(columns, row)))

    except Exception as e:
        error = True
        result.append({
            'error': 'true',
            'message': f"Ocurrió un error al obtener {name_error}: {e}"
        })

        error_message = f'Error: {e}\nSQL: {sql}'

        print(f"Ocurrió un error al obtener {name_error}: ", e)
    finally:

        if error:
            result = []
            result.append({
                "error": True,
                "message": name_error,
                "data": None,
                "status_code": 500
            })

            Log.create(error_message, "")
            abort(jsonify(result[0]), 500)

            # if return_list:
            #     return result, True
            # response = jsonify(
            #     {"error": f"Ocurrió un error al obtener {name_error}"})
            # response.status_code = 500
        else:
            if return_list:
                return result, False
            response = jsonify({"data": result})

        return response


def exec_alfa_sql(sql: str, name_error: str, return_result=False):
    result = []
    connection = get_conn_alfa()

    try:

        if connection == '':
            result.append({
                'error': 'true',
                'message': 'Error en la conexión con el servidor'
            })
            return result, True

        cursor = connection.cursor().execute(sql)

        if return_result:
            result = cursor.fetchall()
            
        connection.commit()
        connection.close()

        return result, False
    except Exception as e:
        # print(f"Ocurrió un error al obtener {name_error}: ", e)
        result.append({
            'error': 'true',
            'message': e
        })
        return result, True
