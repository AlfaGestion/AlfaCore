from flask import Blueprint, jsonify, request
from functions.jwt import validate_token
from functions.general_customer import get_customer_response
from datetime import datetime
from functions.responses import set_response

from routes.v2.master import MasterView
from flask_classful import route


class ManagementView(MasterView):

    @route("/vatbook/<string:type>/<string:datef>/<string:dateu>")
    def vatbook(self, type: str, datef: str, dateu: str):
        """
        GET
        api/v2/sales/vatbook/<string:datef>/<string:dateu>
        Retorna el libro de iva en las fechas seleccionadas
        """

        datef = '' if datef == '*' else datef
        dateu = '' if dateu == '*' else dateu
        type = 'V' if type == '' else type

        datef = datetime.strptime(datef, '%d%m%Y').strftime(
            '%d/%m/%Y') if datef else ''

        dateu = datetime.strptime(dateu, '%d%m%Y').strftime(
            '%d/%m/%Y') if dateu else ''

        sql = f"""
        EXEC sp_web_getLibroIva '{type}','{datef}','{dateu}'
        """

        result, error = get_customer_response(
            sql, f" al obtener el libro de iva ventas", True, self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response
