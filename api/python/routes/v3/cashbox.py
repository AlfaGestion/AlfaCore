from flask import request
from functions.caja import *
from functions.responses import set_response
from routes.v2.master import MasterView
from flask_classful import route


class ViewCashBox(MasterView):

    @route('/reporte_cierre', methods=['POST'])
    def getReporteCaja(self):
        """
        GET
        api/v2/cashbox/reporte_cierre
        Retorna los datos para generar el cierre de caja
        """

        data = request.json
        date: str = data.get('date', '')
        cashbox: str = data.get('cashbox', '')

        show_cancel: bool = data.get('show_cancel', False)
        category_detail: bool = data.get('category_detail', False)
        products_sale: bool = data.get('products_sale', False)
        sale_detail: bool = data.get('sale_detail', False)
        daily_detail: bool = data.get('daily_detail', False)
        monthly_detail: bool = data.get('monthly_detail', False)
        payment_detail: bool = data.get('payment_detail', False)
        initial_balance: bool = data.get('initial_balance', False)

        config = dict({
            'show_cancel': show_cancel,
            'category_detail': category_detail,
            'products_sale': products_sale,
            'sale_detail': sale_detail,
            'daily_detail': daily_detail,
            'monthly_detail': monthly_detail,
            'payment_detail': payment_detail,
            'initial_balance': initial_balance
        })

        result = get_cierre_caja(
            date, cashbox, config, token=self.token_global)

        response = set_response(result, 200, "")

        return response

    @route('/cajas_fhoperativa/<string:fecha>')
    def getCajasFhOperativa(self, fecha: str = ''):
        """
        GET
        api/v2/cashbox/cajas_fhoperativa
        Retorna las cajas utilizadas en una fecha especifica
        """
        result, error = get_cajas_segun_fh_operativa(
            fecha, token=self.token_global)
        response = set_response(result, 200 if not error else 404, "")

        return response

    @route('/medios_pago')
    def get_medios_pago(self):
        # SELECT ltrim(b.codigo) as codigo,ltrim(b.descripcion) as descripcion,ltrim(b.MedioDePago) as mediodepago,ltrim(b.moneda) as moneda
        # FROM TA_CONFIGURACION a LEFT JOIN MA_CUENTAS b on a.valor = b.codigoopcional
        # WHERE a.CLAVE LIKE '%MPMASUTILIZADOS%' AND a.valor<>''
        sql = f"""
        SELECT ltrim(b.codigo) as codigo,ltrim(b.descripcion) as descripcion,ltrim(b.MedioDePago) as mediodepago,ltrim(b.moneda) as moneda FROM MA_CUENTAS b
        WHERE b.MedioDePago<>''
        """

        result, error = get_customer_response(
            sql, f" al obtener los medios de pago", True, self.token_global)

        if result.__len__() == 0:
            sql = f"""
            SELECT ltrim(b.codigo) as codigo,ltrim(b.descripcion) as descripcion,ltrim(b.MedioDePago) as mediodepago ,ltrim(b.moneda) as moneda  
            FROM TA_CONFIGURACION a LEFT JOIN MA_CUENTAS b on a.valor = b.codigo 
            WHERE a.CLAVE ='CUENTA_CAJA'
            """

            result, error = get_customer_response(
                sql, f" al obtener la cuenta efectivo", True, self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response

    @route('/banks')
    def get_banks(self):
        query = f"""
        SELECT idbanco as codigo,isnull(descripcion,'') as descripcion FROM V_TA_Bancos
        """
        response = self.get_response(query, f"OcurriÃ³ un error al obtener los bancos", True)

        return set_response(response, 200)

    @route('/movement', methods=['POST'])
    def set_movement(self):
        """
        Genera un movimiento de caja
        """
        data = request.get_json()

        type = data.get('type', 'I')
        amount = data.get('amount', 0)
        detail = data.get('detail', '')
        account = data.get('account', '')

        query = f"""
        DECLARE @pRes INT
        DECLARE @pMensaje NVARCHAR(250)
        ALTER TABLE MV_ASIENTOS DISABLE TRIGGER ALL
        set nocount on; EXEC sp_web_CreaAsientoIngresoEgreso '{type}',{amount},'{detail}','{account}',@pRes OUTPUT, @pMensaje OUTPUT
         ALTER TABLE MV_ASIENTOS ENABLE TRIGGER ALL
        SELECT @pRes as pRes, @pMensaje as pMensaje
        """

        try:
            result, error = exec_customer_sql(query, " al generar el movimiento de caja", self.token_global, True)
        except Exception as r:
            error = True

        if error:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + query)
            return set_response(None, 404, "OcurriÃ³ un error al generar el movimiento de caja.")

        result_code = result[0][0]
        if result_code != 11:
            self.log(str(result[0][1]) + "\nSENTENCIA : " + query)
            return set_response(None, 404, result[0][1])

        return set_response([], 200, "Movimiento de caja grabado correctamente.")

    @route('/get_imputation_accounts')
    def get_imputation_accounts(self):
        """
        Retorna las cuentas de imputaciÃ³n de caja
        """
        query = f"""
        SELECT codigo,descripcion FROM MA_CUENTAS WHERE (MEDIODEPAGO='' or MEDIODEPAGO IS NULL) and titulo = 0 AND CAJAYBANCO = 1
        """
        response = self.get_response(query, f"OcurriÃ³ un error al obtener las cuentas de imputaciÃ³n", True)

        return set_response(response, 200)
