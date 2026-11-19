from functions.general_customer import exec_customer_sql, get_customer_response
from datetime import datetime
from functions.responses import set_response
from functions.Log import Log
from functions.Account import Customer
from rich import print


class Payment:

    type_payment = "CB"
    methods_data = []
    application_invoices = []
    internal_id = ""

    db_token = ""

    def __init__(self, type_payment: str, customer_account: str, date: str, seller: str, amount: float):
        self.type_payment = type_payment
        self.customer_account = customer_account
        self.created_date = date
        self.seller = seller
        self.amount = amount
        self.methods_data = []
        self.application_invoices = []
        self.internal_id = ""
        self.checks_data = []
        self.db_token = ""
        self.last_sql = ""
        self.last_stage = ""
        self.last_error = ""

    def __set_last_sql(self, stage: str, sql: str):
        self.last_stage = stage
        self.last_sql = sql
        self.last_error = ""

    def __set_last_error(self, error):
        self.last_error = str(error)

    def __disable_valida_fpef_trigger_sql(self) -> str:
        return """
        IF EXISTS (
            SELECT 1
            FROM sys.triggers tr
            INNER JOIN sys.objects obj ON tr.parent_id = obj.object_id
            WHERE tr.name = N'TRG_ValidaFPEF'
              AND obj.name = N'MV_ASIENTOS'
        )
        BEGIN
            ALTER TABLE MV_ASIENTOS DISABLE TRIGGER TRG_ValidaFPEF
        END
        """

    def __enable_valida_fpef_trigger_sql(self) -> str:
        return """
        IF EXISTS (
            SELECT 1
            FROM sys.triggers tr
            INNER JOIN sys.objects obj ON tr.parent_id = obj.object_id
            WHERE tr.name = N'TRG_ValidaFPEF'
              AND obj.name = N'MV_ASIENTOS'
        )
        BEGIN
            ALTER TABLE MV_ASIENTOS ENABLE TRIGGER TRG_ValidaFPEF
        END
        """

    def __format_error(self, result):
        if isinstance(result, list) and result:
            item = result[0]
            if isinstance(item, dict):
                return str(item.get("message", item))
        return str(result)

    def set_db_token(self, token: str):
        self.db_token = token

    def set_internal_id(self, internal_id: str):
        self.internal_id = internal_id

    def add_check(self, number: str, expiration: str = '', bank: str = ''):
        self.checks_data.append({
            'number': number,
            'expiration': expiration,
            'bank': bank
        })

    def status(self):
        print(self.methods_data)
        print(self.application_invoices)

    def add_method(self, account: str, amount: float, check_number: str, check_expiration: str = '', check_bank: str = ''):
        self.methods_data.append({
            'account': account,
            'amount': amount,
            'check_number': check_number,
            'check_expiration': check_expiration,
            'check_bank': check_bank
        })

    def add_application_invoice(self, tc: str, number: str, amount: float):
        self.application_invoices.append({
            'tc': tc,
            'number': number,
            'amount': amount
        })

    def save(self):
        payment_id = 0
        if not self.customer_account:
            self.__set_last_sql("VALIDAR_CLIENTE", "")
            self.__set_last_error("La cobranza no informa cuenta de cliente.")
            Log.create(self.last_error)
            return set_response(None, 404, "La cobranza no informa cuenta de cliente.")

        customer = Customer(self.customer_account, self.db_token)
        if not customer.exists:
            self.__set_last_sql(
                "VALIDAR_CLIENTE",
                f"SELECT codigo FROM VT_CLIENTES WHERE codigo='{self.customer_account}'"
            )
            self.__set_last_error(
                f"No existe el cliente {self.customer_account} en VT_CLIENTES."
            )
            Log.create(self.last_error + "\nSENTENCIA : " + self.last_sql)
            return set_response(
                None,
                404,
                f"No existe el cliente {self.customer_account}. Debe darse de alta o sincronizarse antes de grabar la cobranza."
            )
        """
        Genero la cobranza
        """
        query = f"""
        DECLARE @pRes INT
        DECLARE @pMensaje NVARCHAR(250)
        DECLARE @pIdCpte INT

        set nocount on; EXEC sp_web_CreaCobranza '{self.type_payment}','{self.customer_account}','{self.seller}','{self.created_date}',{self.amount},'','{self.internal_id}',@pRes OUTPUT, @pMensaje OUTPUT,@pIdCpte OUTPUT

        SELECT @pRes as pRes, @pMensaje as pMensaje, @pIdCpte as pIdCpte
        """

        self.__set_last_sql("CREAR_COBRANZA", query)

        try:
            result, error = exec_customer_sql(query, "", self.db_token, True)
        except Exception as r:
            error = True
            result = [{'message': r}]

        if error:
            self.__set_last_error(self.__format_error(result))
            Log.create(self.last_error + "\nSENTENCIA : " + query)
            return set_response(None, 404, "Ocurrió un error al grabar la cobranza en V_MV_CPTE")

        payment_id = result[0][2]

        """
        Genero la primer linea del asiento, la secuencia 1 con la cuenta del cliente
        """
        if not self.__create_line(payment_id, self.amount, self.customer_account, True):
            self.__onerror_delete(payment_id)
            return set_response(None, 404, "Ocurrió un error al grabar la cobranza")

        """
        Genero una linea por cada medio de pago
        """
        for item in self.methods_data:
            if not self.__create_line(payment_id, item['amount'], item['account'], False, item['check_number'], item['check_expiration'], item['check_bank']):
                self.__onerror_delete(payment_id)
                return set_response(None, 404, "Ocurrió un error al grabar la cobranza")

        """
        Genero las aplicaciones
        """
        current_amount = self.amount
        amount_applicate = 0

        query = ""
        for item in self.application_invoices:
            if current_amount > 0:
                if current_amount > item['amount']:
                    amount_applicate = item['amount']
                    current_amount = current_amount - item['amount']
                else:
                    amount_applicate = current_amount
                    current_amount = 0

                result = self.__create_application(payment_id, item['tc'], item['number'], amount_applicate)
                if isinstance(result, dict) and result.get('error', False):
                    return result

        if not self.application_invoices:
            result = self.__auto_application(self.type_payment, self.customer_account, self.internal_id)
            if isinstance(result, dict) and result.get('error', False):
                return result

    def __auto_application(self, tc: str, account: str, paymentWebId: str = ''):
        sql = f"""
        DECLARE @pRes INT
        DECLARE @pMensaje NVARCHAR(250)

        {self.__disable_valida_fpef_trigger_sql()}
        set nocount on; EXEC sp_web_AplicacionCobranzaAutomatica '{tc}','{account}','{paymentWebId}',@pRes OUTPUT, @pMensaje OUTPUT
        {self.__enable_valida_fpef_trigger_sql()}
        SELECT @pRes as pRes, @pMensaje as pMensaje
        """
        self.__set_last_sql("APLICACION_AUTOMATICA", sql)
        result = []

        try:
            result, error = exec_customer_sql(sql, f" al generar la aplicación automática",  self.db_token)
        except Exception as r:
            error = True

        if error:
            self.__set_last_error(self.__format_error(result))
            Log.create(self.last_error + "\nSENTENCIA : " + sql)
            return set_response(None, 404, "Ocurrió un error al generar la aplicación automática. Intente nuevamente.")

    def __create_application(self, payment_id: str, tc_invoice: str, number_invoice: str, amount: float):
        sql = f"""
        SET NOCOUNT ON;
        DECLARE @pResultado INT
        DECLARE @pMensaje NVARCHAR(250)

        EXEC sp_web_CreaAplicacion {payment_id},'{tc_invoice}','{number_invoice}',{amount},@pResultado OUTPUT,@pMensaje OUTPUT

        SELECT @pResultado as pResultado, @pMensaje as pMensaje
        """
        self.__set_last_sql("CREAR_APLICACION", sql)
        result = []

        try:
            result, error = exec_customer_sql(sql, f".No se pudo generar la aplicación. \n {sql}", self.db_token, True)
        except Exception as f:
            error = True

        if error:
            self.__set_last_error(self.__format_error(result))
            self.__onerror_delete(payment_id)
            Log.create(self.last_error + "\nSENTENCIA : " + sql)
            return set_response(None, 404, "No se pudo generar la aplicación")

    def __create_line(self, payment_id, amount: float, account: str, first_record=True, check_number: str = '', check_expiration: str = '', check_bank: str = '') -> bool:
        sql = f"""
        SET NOCOUNT ON;
        DECLARE @pResultado INT
        DECLARE @pMensaje NVARCHAR(250)


        ALTER TABLE MV_ASIENTOS DISABLE TRIGGER ALL
        EXEC sp_web_creaLineaAsiento {payment_id},{amount},'{account}',{1 if first_record else 0},'{check_number}',@pResultado OUTPUT,@pMensaje OUTPUT
        ALTER TABLE MV_ASIENTOS ENABLE TRIGGER ALL

        SELECT @pResultado as pResultado, @pMensaje as pMensaje
        """
        self.__set_last_sql("CREAR_LINEA_ASIENTO", sql)
        result = []
        try:
            result, error = exec_customer_sql(sql, f".No se pudo grabar el medio de pago. \n {sql}", self.db_token, True)
        except Exception as f:
            error = True

        if error:
            self.__set_last_error(self.__format_error(result))
            self.__onerror_delete(payment_id)
            Log.create(self.last_error + "\nSENTENCIA : " + sql)
            # return set_response(None, 404, "Ocurrió un error al grabar el medio de pago")

        return not error

    def __onerror_delete(self, payment_id):
        # TODO hacer que elimine asiento tambien
        query = f"""DELETE FROM V_MV_CPTE WHERE ID={payment_id}"""

        try:
            result, error = exec_customer_sql(query, f".No se pudo grabar la cobranza. \n {query}", self.db_token, True)
        except Exception as f:
            error = True

        if error:
            Log.create("\nSENTENCIA : " + query)
            return set_response(None, 404, "Ocurrió un error al grabar la cobranza")
