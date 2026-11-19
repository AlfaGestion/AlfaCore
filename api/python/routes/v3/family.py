from routes.v2.master import MasterView
# from flask_classful import route
from functions.general_customer import get_customer_response
from functions.responses import set_response


class ViewFamily(MasterView):

    def index(self):

        sql = f"""
        SELECT ltrim(idfamilia) as codigo, ltrim(Descripcion) as descripcion FROM V_TA_FAMILIAS 
        WHERE Descripcion<>'' and idfamilia<>'' ORDER BY descripcion
        """

        result, error = get_customer_response(
            sql, f" al obtener las familias", True, self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response
