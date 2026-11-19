from .master import MasterView
# from flask_classful import route
from functions.general_customer import get_customer_response, exec_customer_sql
from functions.responses import set_response
from flask import request


class CategoryView(MasterView):

    def post(self):

        data = request.get_json()

        code = data.get('code', '')
        name = data.get('name', '')

        sql = f"""

        DECLARE @pCodRespuesta NVARCHAR(250)
        set nocount on; EXEC sp_web_AltaTabla 'v_ta_rubros','{code}','{name}', @pCodRespuesta OUTPUT

        SELECT @pCodRespuesta as codigo, '{name}' as descripcion
        """

        try:
            result, error = exec_customer_sql(sql, " al dar de alta el rubro", self.token_global, True)
            result = [{
                'codigo': result[0][0],
                'descripcion': result[0][1],
            }]
        except Exception as r:
            error = True

        if error:
            self.log(str(result) + "\nSENTENCIA : " + sql)
            return set_response(None, 404, "Ocurrió un error al dar de alta el rubro.")
        return set_response(result, 200)

    def index(self):
        sql = f"""
        SELECT ltrim(idrubro) as codigo, ltrim(Descripcion) as descripcion FROM v_ta_rubros 
        WHERE Descripcion<>'' and IdRubro<>'' ORDER BY descripcion
        """
        result = []
        result, error = get_customer_response(
            sql, f" al obtener los rubros", True, self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response
