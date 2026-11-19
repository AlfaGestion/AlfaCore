from flask import request
from functions.general_customer import get_customer_response

# from configs.customer_connection import get_conn

from functions.session import get_dbases, set_db, get_info_session
from functions.responses import set_response
from routes.v2.master import MasterView
from flask_classful import route


class ViewSession(MasterView):

    @route('/getdbases')
    def getDBAccount(self):
        """
        Retorna las bases del cliente
        """
        result = get_dbases(self.code_account)
        response = set_response(result, 200, "")

        return response

    @route('/setdb/<int:iddb>')
    def setDBAccount(self, iddb: int):
        if set_db(self.code_account, iddb, self.token_global):
            result = get_info_session(self.token_global)
            return set_response(result, 200, "")

        return set_response([], 404, "Error. No se pudo ingresar a la base de datos seleccionada")

    @route("/get_auth", methods=["POST"])
    def get_auth(self):

        data = request.get_json()
        seller_name = data["seller_name"]
        result = []

        sql = f"""
            SELECT usuario,sistema,tarea FROM TA_TAREAS
            WHERE usuario='{seller_name}'
            AND (tarea='D75' OR tarea='D7515' OR tarea='D80' OR tarea='D8079' OR tarea='D60' OR tarea='D6010' OR tarea='D6009' OR tarea='D50' OR tarea='D5004' OR tarea='D600101' OR tarea='D6016')
        """

        result, error = get_customer_response(
            sql, f" el menu del vendedor {seller_name}", True, token=self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])

        return response
