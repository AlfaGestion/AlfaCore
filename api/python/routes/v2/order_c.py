from datetime import datetime
from flask import request
from functions.general_customer import exec_customer_sql
from functions.responses import set_response
from flask_classful import route
from .master import MasterView


class OrderCView(MasterView):

    def post(self):
        orders = request.get_json()
        result = []

        for order in orders:

            account = order.get('account', '')
            date = order.get('date', datetime.now().strftime('%d/%m/%Y'))
            seller = order.get('seller', '')
            lat = order.get('lat', '0')
            lng = order.get('lng', '0')
            tc_invoice = order.get('type', '')
            obs = order.get('obs', '')
            sale_condition = order.get('condition', '')
            tc = order.get('tc', 'NP')
            tc = (tc or 'NP').strip().upper()

            sql = f"""
            DECLARE @pRes INT
            DECLARE @pMensaje NVARCHAR(250)
            DECLARE @pIdCpte INT

            set nocount on; EXEC sp_web_C_MV_CPTE '{account}','{date}','{obs}','{tc}',@pRes OUTPUT, @pMensaje OUTPUT,@pIdCpte OUTPUT

            SELECT @pRes as pRes, @pMensaje as pMensaje, @pIdCpte pIdCpte
            """
            print(sql)
            try:
                result, error = exec_customer_sql(sql, " al grabar los pedidos", self.token_global, True)
            except Exception as r:
                error = True

            if error:
                self.log(str(result[0]['message']) + "\nSENTENCIA : " + sql)
                return set_response(None, 404, "Ocurrió un error al grabar el pedido.")

            result_code = result[0][0]
            if result_code != 11:
                self.log(str(result[0][1]) + "\nSENTENCIA : " + sql)
                return set_response(None, 404, result[0][1])

            result_id_invoice = result[0][2]

            if lat != '0' and lng != '0':
                sql_coords = f"UPDATE MA_CUENTASADIC SET X='{lat}', Y='{lng}' WHERE CODIGO='{account}'"
                # Usamos False en el último parámetro para que no espere un SELECT de vuelta
                exec_customer_sql(sql_coords, " al actualizar coordenadas del cliente", self.token_global, False)

            # Actualizo la condicion de venta y el tipo de comprobante a generar
            if tc_invoice or sale_condition:
                if sale_condition == 'contado': sale_condition = '   1'
                if sale_condition == 'ctacte': sale_condition = '  10'

                if tc_invoice == 'fp' : tc_invoice = 'Proforma'
                if tc_invoice == 'fc' : tc_invoice = 'Factura'

                if sale_condition:
                    sql = f"UPDATE C_MV_CPTE SET IDCOND_CPRA_VTA='{sale_condition}',comentarios='{tc_invoice}' WHERE ID={result_id_invoice}"
                else:
                    sql = f"UPDATE C_MV_CPTE SET comentarios='{tc_invoice}' WHERE ID={result_id_invoice}"

                result, error = exec_customer_sql(sql, " al actualizar los datos de condicion de venta y tipo de comprobante", self.token_global, False)

            for item in order.get('items', []):
                product = item.get('product', '')
                quantity = item.get('quantity', 0)
                amount = item.get('amount', 0)
                dto = item.get('dto', 0)
                bultos = item.get('bultos', 0)
                if bultos == 'None' or bultos ==  None:
                    bultos = 0

                if dto == 'None' or dto ==  None:
                    dto = 0

                sql = f"""
                DECLARE @pRes INT
                DECLARE @pMensaje NVARCHAR(250)
                DECLARE @pIdCpte INT
                EXEC sp_web_CpteInsumosC_V2 {result_id_invoice},'{product}',{quantity},{bultos},{amount},{dto},@pRes OUTPUT, @pMensaje OUTPUT,@pIdCpte OUTPUT
                SELECT @pRes as pRes, @pMensaje as pMensaje, @pIdCpte pIdCpte
                """

                try:
                    result, error = exec_customer_sql(sql, f" al grabar el detalle del pedido",  self.token_global)
                except Exception as f:
                    error = True

                if error:
                    self.log(str(result[0]['message']) + "\nSENTENCIA : " + sql)
                    self.__delete_order_on_error(result_id_invoice)
                    return set_response(None, 404, "Ocurrió un error al grabar el detalle del pedido. Intente nuevamente.")

        response = set_response([], 200, "Pedidos grabados correctamente.")
        return response

    def __delete_order_on_error(self, cpte_id: str):
        query = f"DELETE FROM C_MV_CPTE WHERE ID = {cpte_id}"

        response = self.get_response(query, f"Ocurrió un error al eliminar el comprobante", False, True)
