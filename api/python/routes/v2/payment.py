from datetime import datetime
from flask import request
from functions.general_customer import exec_customer_sql, get_customer_response
from functions.responses import set_response
from flask_classful import route
from .master import MasterView
from functions.Payment import Payment
from rich import print
from functions.Log import Log

class PaymentView(MasterView):
    @route('/save', methods=['POST'])
    def save(self):
        """Utilizada para las cobranzas del movil. 
        Asegura la continuidad del procesamiento aunque falle el guardado de un pago."""
        
        payments = request.get_json()
        
        failed_payments = []

        for payment in payments:
            paymentId = payment.get('paymentId', 'NO_ID')
            
            try:
                tc = payment.get('tc', '')
                account = payment.get('account', '')
                date = payment.get('date', datetime.now().strftime('%d/%m/%Y')) 
                seller = payment.get('seller', '')
                amount = payment.get('amount', 0)

                invoices = payment.get('invoices', None)
                methods = payment.get('methods', None)

                pay = Payment(tc, account, date, seller, amount)
                pay.set_db_token(self.token_global)
                pay.set_internal_id(paymentId)

                for method in methods:
                    pay.add_method(method['account'], method['amount'], method['checkNumber'], 'Null', 'Null')

                for invoice in invoices:
                    pay.add_application_invoice(invoice['tc'], invoice['idcomprobante'], invoice['amount'])

                pay.save()
                Log.create(f"INFO: Pago ID {paymentId} guardado correctamente.")

            except Exception as e:
                Log.create(f"ERROR: Falló el guardado del pago ID {paymentId}. Error: {e}")
                failed_payments.append(paymentId)
                
        if failed_payments:
            message = f"Cobranzas grabadas. Hubo fallos en {len(failed_payments)} pagos. IDs fallidos: {', '.join(failed_payments)}"
        else:
            message = "Cobranzas grabadas correctamente."
            
        response = set_response(failed_payments, 200, message)

        return response

    # @route('/save', methods=['POST'])
    # def save(self):
    #     """Utilizada para las cobranzas del movil"""
    #     payments = request.get_json()

    #     for payment in payments:

    #         tc = payment.get('tc', '')
    #         account = payment.get('account', '')
    #         date = payment.get('date', datetime.now().strftime('%d/%m/%Y'))
    #         seller = payment.get('seller', '')
    #         amount = payment.get('amount', 0)
    #         paymentId = payment.get('paymentId', '')

    #         invoices = payment.get('invoices', None)
    #         methods = payment.get('methods', None)

    #         pay = Payment(tc, account, date, seller, amount)
    #         pay.set_db_token(self.token_global)
    #         pay.set_internal_id(paymentId)

    #         for method in methods:
    #             pay.add_method(method['account'], method['amount'], method['checkNumber'], 'Null', 'Null')

    #         for invoice in invoices:
    #             pay.add_application_invoice(invoice['tc'], invoice['idcomprobante'], invoice['amount'])

    #         pay.save()

    #     # self.__auto_application(tc, account, paymentId)
    #     response = set_response([], 200, "Cobranzas grabadas correctamente.")

    #     return response

    def post(self):
        payments = request.get_json()

        for payment in payments:

            tc = payment.get('tc', '')
            account = payment.get('account', '')
            date = payment.get('date', datetime.now().strftime('%d/%m/%Y'))
            seller = payment.get('seller', '')
            amount = payment.get('amount', 0)
            mp = payment.get('mp', '')
            obs = payment.get('observation', '')
            paymentId = payment.get('paymentId', '')

            check = payment.get('check', '')

            if(check != ''):
                check_number = check.get('nro', '')
                check_expiration = check.get('expiration', '')
                check_idbank = check.get('idBank', '')
            else:
                check_number = ''
                check_expiration = ''
                check_idbank = ''

            sql = f"""
            DECLARE @pRes INT
            DECLARE @pMensaje NVARCHAR(250)

            ALTER TABLE MV_ASIENTOS DISABLE TRIGGER TRG_ValidaFPEF
            set nocount on; EXEC sp_web_setCobranza '{tc}','{account}','{seller}','{date}',{amount},'{obs}','{mp}','{paymentId}','{check_number}','{check_expiration}','{check_idbank}',@pRes OUTPUT, @pMensaje OUTPUT
            ALTER TABLE MV_ASIENTOS ENABLE TRIGGER TRG_ValidaFPEF
            SELECT @pRes as pRes, @pMensaje as pMensaje
            """

            try:
                result, error = exec_customer_sql(sql, f" al grabar la cobranza",  self.token_global)
            except Exception as r:
                error = True

            if error:
                self.log(str(result) + "\nSENTENCIA : " + sql)
                return set_response(None, 404, "Ocurrió un error al grabar la cobranza. Intente nuevamente.")

            # una vez grabada la cobranza, ejecuto la aplicación automática.

        self.__auto_application(tc, account, paymentId)
        response = set_response([], 200, "Cobranzas grabadas correctamente.")

        return response

    def __auto_application(self, tc: str, account: str, paymentWebId: str = ''):
        sql = f"""
        DECLARE @pRes INT
        DECLARE @pMensaje NVARCHAR(250)

        ALTER TABLE MV_ASIENTOS DISABLE TRIGGER TRG_ValidaFPEF
        set nocount on; EXEC sp_web_AplicacionCobranzaAutomatica '{tc}','{account}','{paymentWebId}',@pRes OUTPUT, @pMensaje OUTPUT
        ALTER TABLE MV_ASIENTOS ENABLE TRIGGER TRG_ValidaFPEF
        SELECT @pRes as pRes, @pMensaje as pMensaje
        """

        try:
            result, error = exec_customer_sql(sql, f" al generar la aplicación automática",  self.token_global)
        except Exception as r:
            error = True

        if error:
            self.log(str(result) + "\nSENTENCIA : " + sql)
            return set_response(None, 404, "Ocurrió un error al generar la aplicación automática. Intente nuevamente.")

    @route('/search', methods=['POST'])
    def search_payments(self):
        data = request.get_json()

        seller = data.get('seller', '')
        fhd = data.get('dateFrom', datetime.now().strftime('%Y%m%d'))
        fhh = data.get(
            'dateUntil', datetime.now().strftime('%Y%m%d'))

        fecha_desde = datetime.strptime(fhd, '%Y%m%d').strftime('%d/%m/%Y')
        fecha_hasta = datetime.strptime(fhh, '%Y%m%d').strftime('%d/%m/%Y')

        sql = f"""
        sp_web_getComprobantes 'CB','{seller}','{fecha_desde}','{fecha_hasta}','',0
        """

        result, error = get_customer_response(
            sql, f" al obtener las cobranzas", True, self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response
