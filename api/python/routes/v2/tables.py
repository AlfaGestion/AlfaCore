from .master import MasterView
from functions.general_customer import exec_customer_sql
from functions.responses import set_response
from flask import request


class TablesView(MasterView):

    def post(self):

        data = request.get_json()

        code = data.get('code', '')
        name = data.get('name', '')
        table = data.get('table', '')

        sql = f"""

        DECLARE @pCodRespuesta NVARCHAR(250)
        set nocount on; EXEC sp_web_AltaTabla '{table}','{code}','{name}', @pCodRespuesta OUTPUT

        SELECT @pCodRespuesta as codigo, '{name}' as descripcion
        """

        try:
            result, error = exec_customer_sql(sql, " al dar de alta la tabla", self.token_global, True)
            result = [{
                'codigo': result[0][0],
                'descripcion': result[0][1],
            }]
        except Exception as r:
            error = True

        if error:
            self.log(str(result) + "\nSENTENCIA : " + sql)
            return set_response(None, 404, "Ocurrió un error al dar de alta la tabla.")

        return set_response(result, 200)
