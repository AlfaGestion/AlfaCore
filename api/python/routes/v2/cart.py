from .master import MasterView
# from flask_classful import route
from functions.general_customer import get_customer_response, exec_customer_sql
from functions.responses import set_response
from flask import request
from flask_classful import route


class CartView(MasterView):

    @route('/add', methods=['POST'])
    def add(self):

        data = request.get_json()

        products = data.get('products', [])
        customer = data.get('customer', '')

        query = ''

        for product in products:
            code = product.get('code', '')
            name = product.get('name', '')
            price = product.get('price', '')
            qty = product.get('quantity', '')
            discount = product.get('discount', '')
            presentation = product.get('presentation', '')

            query = query + f"""
            INSERT INTO AUX_WEB_CARRITO (idarticulo,descripcion,precio,cantidad,descuento,presentacion,idcliente)
            values ('{code}','{name}',{price},{qty},{discount},'{presentation}','{customer}')
            """

        query = f"DELETE FROM AUX_WEB_CARRITO WHERE idcliente='{customer}'; " + query

        try:
            result, error = exec_customer_sql(query, " al crear el carrito", self.token_global, False)
        except Exception as r:
            error = True

        if error:
            self.log(str(result) + "\nSENTENCIA : " + query)
            return set_response(None, 404, "Ocurrió un error al dar de alta el carrito.")
        return set_response(result, 200)

    def get(self, customer: str):
        # sql = f"""
        # SELECT idarticulo,descripcion,precio,cantidad,descuento,presentacion,idcliente FROM AUX_WEB_CARRITO
        # WHERE idcliente='{customer}'
        # """

        sql = f"""
        SELECT a.idarticulo,a.descripcion,a.precio,a.cantidad,a.descuento,a.presentacion,a.idcliente,b.tasaiva,b.exento FROM AUX_WEB_CARRITO a LEFT JOIN V_MA_ARTICULOS b on ltrim(a.idarticulo) = LTRIM(b.idarticulo)
        WHERE idcliente='{customer}'
        """
        
        result = []
        result, error = get_customer_response(sql, f" al obtener el carrito", True, self.token_global)

        response = set_response(result, 200 if not error else 404, "")
        return response

    # def post(self):

    #     data = request.get_json()

    #     code = data.get('code', '')
    #     name = data.get('name', '')

    #     sql = f"""

    #     DECLARE @pCodRespuesta NVARCHAR(250)
    #     set nocount on; EXEC sp_web_AltaTabla 'v_ta_rubros','{code}','{name}', @pCodRespuesta OUTPUT

    #     SELECT @pCodRespuesta as codigo, '{name}' as descripcion
    #     """

    #     try:
    #         result, error = exec_customer_sql(sql, " al dar de alta el rubro", self.token_global, True)
    #         result = [{
    #             'codigo': result[0][0],
    #             'descripcion': result[0][1],
    #         }]
    #     except Exception as r:
    #         error = True

    #     if error:
    #         self.log(str(result) + "\nSENTENCIA : " + sql)
    #         return set_response(None, 404, "Ocurrió un error al dar de alta el rubro.")
    #     return set_response(result, 200)

    # def index(self):
    #     sql = f"""
    #     SELECT ltrim(idrubro) as codigo, ltrim(Descripcion) as descripcion FROM v_ta_rubros
    #     WHERE Descripcion<>'' and IdRubro<>'' ORDER BY descripcion
    #     """
    #     result = []
    #     result, error = get_customer_response(
    #         sql, f" al obtener los rubros", True, self.token_global)

    #     response = set_response(
    #         result, 200 if not error else 404, "" if not error else result[0]['message'])
    #     return response
