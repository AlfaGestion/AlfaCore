from datetime import datetime
from urllib import request
from functions.general_customer import exec_customer_sql, get_customer_response
from functions.responses import set_response
from .master import MasterView


class ServiceView(MasterView):
    def index(self):
        sql = f"""
        SELECT ltrim(idtarea) as codigo,ltrim(descripcion) as descripcion from V_TA_TAREAS 
        where descripcion<>'' and idtarea<>'' ORDER BY descripcion
        """

        result, error = get_customer_response(
            sql, f" al obtener los servicios", True, self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response
