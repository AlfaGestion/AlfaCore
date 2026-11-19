from flask_classful import FlaskView
from flask import jsonify, request, abort
from configs.customer_connection import get_conn
from functions.Log import Log
from functions.jwt import validate_token
from functions.session import get_info_session
from config import CTA_MAIL
from functions.DataBase import DataBase


class MasterView(FlaskView):

    code_account = ''
    token_global = ''

    def after_request(self, name, response):
        response.headers.add('Access-Control-Allow-Origin', '*')
        response.headers.add('Access-Control-Allow-Headers',
                             'Content-Type, Authorization')
        response.headers.add('Access-Control-Allow-Methods',
                             'GET, PUT, POST, DELETE,OPTIONS ')
        response.status_code = 200
        return response

    def before_request(self, name, *args, **kwargs):
        valid = False

        if 'Authorization' in request.headers:
            token = request.headers['Authorization'].split(" ")[1]
            valid, result = validate_token(token, output=True)
            if valid:
                try:
                    session = get_info_session(token)
                    # session = session[0]
                    if not session:
                        valid = False
                        response = jsonify(
                            {"message": "Token expired", "status_code": 401})
                        # response.status_code = 401
                except Exception as e:
                    valid = False
                    response = jsonify(
                        {"message": "Token expired", "status_code": 401})
                    # response.status_code = 401

        if not valid:
            response = jsonify(
                {"message": "Invalid Token", "status_code": 401})
            response.status_code = 401
            return response
        else:
            self.code_account = result['account']
            self.token_global = token

    def log(self, data, type="WARNING"):
        Log.create(data, self.code_account, type, token=self.token_global)

    def get_response(self, query: str, error_message: str, return_list: bool = False, is_query: bool = False):
        if is_query:
            return self.__exec_customer_sql(query, error_message, return_list)
        else:
            return self.__get_customer_response(query, error_message, return_list)

    def __exec_customer_sql(self, query: str, name_error: str,  return_result=False):
        result = []
        error = False
        error_message = ''

        try:
            connection = get_conn(self.token_global)
            if connection == '':
                error_message = 'No se pudo obtener la conexión con el servidor'
                error = True
            else:
                cursor = connection.cursor().execute(query)

                if return_result:
                    result = cursor.fetchall()
                connection.commit()
                connection.close()
        except Exception as e:
            error = True
            error_message = f'Error: {e}\nSQL: {query}'
        finally:
            if error:
                result = []
                result.append({
                    "error": True,
                    "message": name_error,
                    "data": None,
                    "status_code": 500
                })

                self.log(error_message, "ERROR")
                abort(jsonify(result[0]), 500)
            else:
                return result

    def __get_customer_response(self, query: str, name_error: str = '', return_list: bool = False):
        """
        Retorna un diccionario con los elementos de la consulta
        """

        result = []
        error = False
        error_message = ''
        try:

            connection = get_conn(self.token_global)

            if connection == '':
                error_message = 'No se pudo obtener la conexión con el servidor'
                error = True
            else:
                cursor = connection.cursor().execute(query)
                columns = [column[0] for column in cursor.description]
                for row in cursor.fetchall():
                    result.append(dict(zip(columns, row)))

        except Exception as e:
            error = True
            error_message = f'Error: {e}\nSQL: {query}'

        finally:

            if error:
                result = []
                result.append({
                    "error": True,
                    "message": name_error,
                    "data": None,
                    "status_code": 500
                })

                self.log(error_message, "ERROR")
                abort(jsonify(result[0]), 500)

            else:
                if return_list:
                    return result
                response = jsonify({"data": result})

            return response

    def update_database(self):
        session = get_info_session(self.token_global)
        # print(session[0].get('dbserver'))
        if session:
            update_ok, update_error = DataBase.update(session[0].get('dbserver', ''), session[0].get('dbname', ''))
            if not update_ok:
                self.log(
                    (
                        f"ERROR UPDATE_DATABASE ROUTE: {session[0].get('dbserver', '')}\n"
                        f"BASE: {session[0].get('dbname', '')}\n"
                        f"DETALLE ERROR: {update_error}"
                    ),
                    "ERROR",
                )

    def send_email(self):
        pass
        # to = ''
        # body_task = ''

        # if to == '':
        #     return

        # mail = Mail()
        # msg = Message('Notificación web', sender=CTA_MAIL, recipients=[to])
        # msg.html = render_template('mail_response.html', body=body_task)

        # mail.send(msg)
