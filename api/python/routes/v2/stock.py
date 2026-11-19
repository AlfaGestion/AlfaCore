from flask import request
from functions.general_customer import get_customer_response
from functions.responses import set_response
from functions.stock import get_depositos, get_saldo_query, get_product_stock_file
from routes.v2.master import MasterView
from flask_classful import route


class StockView(MasterView):

    @route('/depositos')
    def get_all_depositos(self):
        """
        Retorna los depositos
        """
        result = get_depositos(self.token_global)
        response = set_response(result, 200, "")

        return response

    @route('/query/<int:page>/<string:deposit>')
    @route('/query/<int:page>/<string:deposit>/<string:product>')
    def get_saldoquery(self, page: int = 1, deposit: str = '', product: str = ''):
        """
        Retorna el saldo
        """
        result = get_saldo_query(
            deposit=deposit, product=product, token=self.token_global, page=page)
        response = set_response(result, 200, "")

        return response

    @route('/file/<string:deposit>/<string:product>/<string:date>')
    def get_stock_product_file(self, deposit: str = '', product: str = '', date: str = ''):
        """
        Method: GET
        End point api/v2/stock/file/<string:deposito>/<string:product>/<string:date>
        Retorna la ficha de stock de un producto
        """

        result = get_product_stock_file(
            deposit=deposit, product=product, token=self.token_global, date=date)
        response = set_response(result, 200, "")

        return response

    @route('/product', methods=['POST'])
    def get_stock_product(self):
        data = request.get_json()

        # print(data)
        code = data.get('code', '')

        sql = f"""
        sp_web_getStockDepositos '{code}'
        """

        result, error = get_customer_response(
            sql, f" al obtener el stock del articulo {code}", True, self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response
