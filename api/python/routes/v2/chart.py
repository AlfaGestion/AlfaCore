
from .master import MasterView
# from flask_classful import route
from functions.general_customer import get_customer_response, exec_customer_sql
from functions.responses import set_response
from flask import request
from functions.Chart import Chart
from flask_classful import route


class ChartView(MasterView):

    @route('/sales_purchases_by_months', methods=['POST'])
    def get_sales_and_purchases_data(self):
        data = request.get_json()

        type_chart = data.get('type', 'bar')
        months = data.get('months', 6)

        c = Chart(self.token_global, type_chart)
        response = c.sales_purchases_info(months)

        # print(response)

        return set_response(response, 200, "")
