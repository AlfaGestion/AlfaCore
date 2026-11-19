import datetime
from dateutil.relativedelta import relativedelta
import calendar
from routes.v2.master import MasterView
# from rich import print
# Clase utilizada para obtener distintos tipos de graficos
from functions.general_customer import get_customer_response, exec_customer_sql


class Chart():

    chart_type = 'bar'

    def __init__(self, token: str, chart_type: str):
        self.chart_type = chart_type
        self.token = token

    def sales_purchases_info(self, last_monts: int = 2):
        # Obtengo la información de compras y ventas de los últimos meses estipulados
        start_month = 1
        end_month = 1

        now = datetime.datetime.now()
        now = now.replace(day=1)

        end_month = now.month

        start_date = now - relativedelta(months=last_monts)
        start_month = start_date.month

        start_date = start_date.strftime("%d/%m/%Y")

        res = calendar.monthrange(now.year, now.month)
        last_day_of_month = res[1]

        current_date = now.replace(day=last_day_of_month).strftime("%d/%m/%Y")

        date_filter = f" AND (FECHA BETWEEN '{start_date}' AND '{current_date}') "

        query = f"""
        SELECT ROW_NUMBER() OVER(ORDER BY YEAR(FECHA),MONTH(FECHA)) AS orden,YEAR(FECHA) as ano,MONTH(FECHA) as mes,convert(varchar,convert(decimal(15,2),SUM(IMPORTE))) as importe FROM V_MV_Cpte WHERE (TC='FP' OR TC='FC' OR TC='TK' OR TC='TKFC') 
        AND ANULADA=0 {date_filter} GROUP BY YEAR(FECHA),MONTH(FECHA) ORDER BY YEAR(FECHA),MONTH(FECHA)
        """

        # print(query)

        data, error = get_customer_response(query, "", True, self.token)

        sale_data = self.__complete_months(data, start_month, end_month)

        query = f"""
        SELECT ROW_NUMBER() OVER(ORDER BY YEAR(FECHA),MONTH(FECHA)) AS orden,YEAR(FECHA) as ano,MONTH(FECHA) as mes,convert(varchar,convert(decimal(15,2),SUM(IMPORTE))) as importe FROM C_MV_Cpte WHERE (TC='FCC')
        AND ANULADA=0 {date_filter} GROUP BY YEAR(FECHA),MONTH(FECHA) ORDER BY YEAR(FECHA),MONTH(FECHA) 
        """

        data, error = get_customer_response(query, "", True, self.token)
        purchase_data = self.__complete_months(data, start_month, end_month)

        response = {'sales': sale_data, 'purchases': purchase_data}

        return response

    def __complete_months(self, data, start_month: int, last_month: int):

        months = [month for month in range(start_month, last_month + 1)]

        data_months = []
        response = data
        for value in data:
            data_months.append(value['mes'])

        # print(data_months)

        for month in months:
            if not month in data_months:
                # print(f"No existe mes {month}")
                response.append({
                    'mes': month,
                    'importe': 0,
                    'ano': 0
                })

        return sorted(response, key=lambda i: i['mes'])
