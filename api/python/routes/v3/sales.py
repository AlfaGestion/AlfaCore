from flask import request
from functions.general_customer import exec_customer_sql, get_customer_response
from datetime import datetime
from functions.responses import set_response
from routes.v2.master import MasterView
from flask_classful import route
from functions.Email import Email
from functions.Company import Company
from functions.Account import Customer
from functions.Document import Document


class ViewSales(MasterView):

    @route('/receipt', methods=['POST'])
    def save_receipt(self):

        htmlBodyEmail = ''
        htmlProductsEmail = ''
        data = request.get_json()

        date = data.get('date', '')
        customer_code = data.get('customer', '')
        seller = data.get('seller', '')
        products = data.get('products', '')
        observations = data.get('observations', '')

        name = data.get('name', '')
        email = data.get('email', '')
        phone = data.get('phone', '')
        tc = data.get('tc', 'NP')
        branch = data.get('branch', '')

        document_customer = data.get('customer_document', '')
        document_customer_type = data.get('customer_document_type', '')

        try:
            customer = Customer(code=customer_code, token=self.token_global)
            document = Document(code=tc, customer=customer, token=self.token_global, branch=branch, date=date)

            document.set_custom_email(email)
            document.set_custom_name(name)
            document.set_custom_phone(phone)

            document.set_seller(seller)
            document.set_observations(observations)

            if document_customer:
                document.set_document_customer(document_customer)

            if document_customer_type:
                document.set_document_type_customer(document_customer_type)

            for item in products:
                document.add_item(code=item['code'], name=item['name'], quantity=item['quantity'], price=item['price'], discount=item['discount'], iva=item['alicIva'], neto=item['neto'], iva_amount=item['iva'])

                htmlProductsEmail = htmlProductsEmail + f"""
                <tr>
                    <td style='padding:5px;text-align:left;border: 1px solid black;border-collapse: collapse;'>{item['code']}</td>
                    <td style='padding:5px;text-align:left;border: 1px solid black;border-collapse: collapse;'>{item['name']}</td>
                    <td style='padding:5px;text-align:right;border: 1px solid black;border-collapse: collapse;'>{item['quantity']}</td>
                    <td style='padding:5px;text-align:right;border: 1px solid black;border-collapse: collapse;'>{item['price']}</td>
                    <td style='padding:5px;text-align:right;border: 1px solid black;border-collapse: collapse;'>{item['price']*item['quantity']}</td>
                </tr>
                """

            document.save()

        except Exception as e:
            self.log(e)
            return set_response(None, 404, f"{e}")

        htmlBodyEmail = f"""
        <div>
            <h3>Nuevo pedido desde la web</h3>
            <span>Recibiste un nuevo pedido cargado desde la web</span><br />

            <div>
                <span>Cliente: {customer_code} - {name if name != '' else customer.name}</span><br />
                <span>Fecha: {date}</span><br />
                <span>Email: {email}</span><br />
                <span>Tel: {phone}</span><br />
                <span>Pedido: {document.code} {document.branch}{document.number}{document.letter}</span>
            </div>

            <div>
                <table style='border: 1px solid black;border-collapse: collapse;margin-top:10px;'>
                    <thead>
                        <tr>
                            <th style='padding:5px;text-align:left;border: 1px solid black;border-collapse: collapse;'>Código</th>
                            <th style='padding:5px;text-align:left;border: 1px solid black;border-collapse: collapse;'>Descripción</th>
                            <th style='padding:5px;text-align:right;border: 1px solid black;border-collapse: collapse;'>Cant.</th>
                            <th style='padding:5px;text-align:right;border: 1px solid black;border-collapse: collapse;'>Precio</th>
                            <th style='padding:5px;text-align:right;border: 1px solid black;border-collapse: collapse;'>Total</th>
                        </tr>
                    </thead>
                    <tbody>
                        {htmlProductsEmail}
                    </tbody>
                </table>
            </div>
        </div>
        """

        company = Company(self.token_global)

        if(company.send_email_np == 'SI' and company.email_np != ''):
            Email.send_email(company.email_np, 'Nuevo pedido desde la web', htmlBodyEmail)

        return set_response([{
            'receipt_id': document.receipt_id,
            'tc': document.code,
            'number': document.branch + document.number + document.letter
        }], 200, "Comprobante creado con éxito")

    @route('/receipt/pay', methods=['POST'])
    def save_payments(self):
        data = request.get_json()

        receipt_id = data.get('receiptId', None)
        payments = data.get('payments', None)

        if not receipt_id:
            return set_response(None, 404, "No se informo el id del comprobante")

        sql = f"""
        DECLARE @pRes INT
        DECLARE @pMensaje NVARCHAR(250)
        DECLARE @pIdCpte INT

        set nocount on; EXEC sp_web_CreaCobPorFactura {receipt_id},@pRes OUTPUT, @pMensaje OUTPUT,@pIdCpte OUTPUT

        SELECT @pRes as pRes, @pMensaje as pMensaje, @pIdCpte as pIdCpte
        """

        try:
            result, error = exec_customer_sql(sql, "", self.token_global, True)
        except Exception as r:
            error = True

        if error:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + sql)
            return set_response(None, 404, "Ocurrió un error al grabar la cobranza.")

        result_code = result[0][0]
        result_message = result[0][1]
        payment_id = result[0][2]

        if result_code != 11:
            self.log(result_message)
            return set_response(None, 404, result_message)

        for payment in payments:
            sql = f"""
            SET NOCOUNT ON;
            DECLARE @pResultado INT
            DECLARE @pMensaje NVARCHAR(250)

            ALTER TABLE MV_ASIENTOS DISABLE TRIGGER ALL
            EXEC sp_web_creaLineaAsiento {payment_id},{payment['amount']},{payment['account']},'{payment['checkNumber']}',@pResultado OUTPUT,@pMensaje OUTPUT
            ALTER TABLE MV_ASIENTOS ENABLE TRIGGER ALL

            SELECT @pResultado as pResultado, @pMensaje as pMensaje
            """
            try:
                result, error = exec_customer_sql(sql, f".No se pudo grabar el medio de pago. \n {sql}", self.token_global, True)
            except Exception as f:
                error = True

            if error:
                self.log(str(result[0]['message']) + "\nSENTENCIA : " + sql)
                return set_response(None, 404, "Ocurrió un error al grabar el medio de pago")

        self.create_application(payment_id, receipt_id)

        return set_response([], 200, "Comprobante creado con éxito")

    def create_application(self, payment_id, receipt_id):
        sql = f"""
        SET NOCOUNT ON;
        DECLARE @pResultado INT
        DECLARE @pMensaje NVARCHAR(250)

        ALTER TABLE MV_ASIENTOS DISABLE TRIGGER ALL
        EXEC sp_web_CreaAplicacionCobranzaFactura {payment_id},{receipt_id},@pResultado OUTPUT,@pMensaje OUTPUT
        ALTER TABLE MV_ASIENTOS ENABLE TRIGGER ALL

        SELECT @pResultado as pResultado, @pMensaje as pMensaje
        """
        try:
            result, error = exec_customer_sql(sql, f".No se pudo crear la aplicacion. \n {sql}", self.token_global, True)
        except Exception as f:
            error = True

        if error:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + sql)
            return set_response(None, 404, "Ocurrió un error al crear la aplicacion")

    def generate_first_record(self, payment_id):
        sql = f"""
        SET NOCOUNT ON;
        DECLARE @pResultado INT
        DECLARE @pMensaje NVARCHAR(250)

        ALTER TABLE MV_ASIENTOS DISABLE TRIGGER ALL
        EXEC sp_web_creaLineaAsiento {payment_id},0,'',1,Null,@pResultado OUTPUT,@pMensaje OUTPUT
        ALTER TABLE MV_ASIENTOS ENABLE TRIGGER ALL

        SELECT @pResultado as pResultado, @pMensaje as pMensaje
        """
        try:
            result, error = exec_customer_sql(sql, f".No se pudo grabar el medio de pago. \n {sql}", self.token_global, True)
        except Exception as f:
            error = True

        if error:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + sql)
            return set_response(None, 404, "Ocurrió un error al grabar el medio de pago")

    @route('/orders', methods=['POST'])
    def get_order_list(self):
        data = request.get_json()

        customer = data['customer']
        seller = data['seller']
        datef = data['datef']
        dateu = data['dateu']

        datef = '' if datef == '*' else datef
        dateu = '' if dateu == '*' else dateu

        datef = datetime.strptime(datef, '%d%m%Y').strftime(
            '%d/%m/%Y') if datef else ''

        dateu = datetime.strptime(dateu, '%d%m%Y').strftime(
            '%d/%m/%Y') if dateu else ''

        sql = f"""
            sp_web_getPedidos '{customer}','{datef}','{dateu}','{seller}'
        """

        try:
            result, error = get_customer_response(sql, " al obtener los pedidos", True, self.token_global)

            response = set_response(
                result, 200 if not error else 404, "" if not error else result[0]['message'])
            return response
        except Exception as f:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + sql)

        return set_response(None, 404, f"Ocurrió un error al obtener los pedidos")

    @route('/pending_invoices/<string:customer>')
    def get_pending_invoices(self, customer: str = None):
        """Retorna los comprobantes pendientes de pago de una cuenta"""
        if not customer:
            return set_response(None, 404, "No informo la cuenta a consultar")

        query = f"""
        SELECT CONVERT(NVARCHAR(10),fecha,103) as fecha,tc,sucursal+numero+letra as idcomprobante,convert(varchar,convert(decimal(15,2),saldo)) as saldo,ltrim(rtrim(detalle)) as detalle 
        FROM VE_CPTES_SALDOS_TODOS WHERE CUENTA='{customer}' AND SALDO > 0 AND TC IN ('FP','FC','TK','TKFC')
        """

        try:
            result, error = get_customer_response(query, f" al obtener los comprobantes pendientes", True, self.token_global)

            return set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])

        except Exception as f:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + query)

        return set_response(None, 404, f"Ocurrió un error al obtener los comprobantes pendientes de la cuenta {customer}")
