from flask_classful import route
from flask import request

from functions.responses import set_response
from .master import MasterView
from datetime import datetime

from flask_mail import Mail, Message
from flask import render_template
from config import CTA_MAIL

from functions.Email import Email


class UtilsView(MasterView):

    @route('/sendemail_payment', methods=['POST'])
    def send_email_payment(self):

        data = request.get_json()

        to = data.get('to', None)
        account = data.get('account', '112010001')
        method = data.get('mp', '')
        amount = data.get('amount', 0)
        payment_id = data.get('payment_id', '')
        seller = data.get('seller', '')

        query = f"""
        SELECT ltrim(RAZON_SOCIAL) as nombre,isnull(ltrim(calle),'') as calle,
        isnull(ltrim(localidad),'') as localidad,
        isnull(ltrim(numero_documento),'') as documento 
        FROM Vt_Clientes where CODIGO='{account}'"""

        data_account = self.get_response(query, f"Ocurrió un error al obtener los datos de la cuenta {account}.", True)

        query = f"""
        SELECT 'nombre' as clave, isnull(valor,'') as valor FROM TA_CONFIGURACION WHERE CLAVE ='Nombre'
        UNION
        SELECT 'localidad' as clave,isnull(valor,'') as valor FROM TA_CONFIGURACION WHERE CLAVE ='Localidad'
        UNION
        SELECT 'cuit' as clave,isnull(valor,'') as valor FROM TA_CONFIGURACION WHERE CLAVE ='CUIT'
        UNION
        SELECT 'telefono' as clave,isnull(valor,'') as valor FROM TA_CONFIGURACION WHERE CLAVE ='Telefono'
        UNION
        SELECT 'calle' as clave,isnull(valor,'') as valor FROM TA_CONFIGURACION WHERE CLAVE ='Calle'
        UNION
        SELECT 'cpostal' as clave,isnull(valor,'') as valor FROM TA_CONFIGURACION WHERE CLAVE ='CPOSTAL'
        UNION
        SELECT 'iva' as clave,isnull(b.descripcion,'') as valor FROM TA_CONFIGURACION a LEFT JOIN TA_CONDIVA b on ltrim(a.valor) = ltrim(b.CODIGO) WHERE a.CLAVE ='CONDIVA'
        """

        data_company = self.get_response(query, f"Ocurrió un error al obtener los datos de la empresa.", True)

        company = {}
        for row in data_company:
            company[row['clave']] = row.get('valor', '')

        query = f"""SELECT isnull(nombre,'') as nombre FROM V_TA_VENDEDORES where ltrim(idvendedor)='{seller.strip()}'"""
        data_seller = self.get_response(query, f"Ocurrió un error al obtener los datos del vendedor.", True)
        if data_seller:
            seller = data_seller[0].get('nombre', '')

        date = datetime.now().strftime("%d/%m/%Y %H:%M:%S")

        query = f"SELECT descripcion FROM MA_CUENTAS WHERE CODIGO='{method}'"
        data_method = self.get_response(query, f"Ocurrió un error al obtener los datos del medio de pago.", True)
        if data_method:
            method = data_method[0].get('descripcion', '')

        # return render_template(
        #     'payment.html', seller=seller, date=date, company=company, account=data_account[0], method=method, amount=amount, payment_id=payment_id)

        mail = Mail()
        msg = Message('Aviso de cobranza recibida', sender=CTA_MAIL, recipients=[to])
        msg.html = render_template(
            'payment.html', seller=seller, date=date, company=company, account=data_account[0], method=method, amount=amount, payment_id=payment_id)

        mail.send(msg)

        return set_response(None, 200, 'OK')

    @route('/sendemail_invoice', methods=['POST'])
    def sendemail_invoice(self):
        data = request.get_json()

        to = data.get('to', None)
        idInvoice = data.get('idInvoice', None)

        query = f"""
        SELECT 'nombre' as clave, isnull(valor,'') as valor FROM TA_CONFIGURACION WHERE CLAVE ='Nombre'
        UNION
        SELECT 'localidad' as clave,isnull(valor,'') as valor FROM TA_CONFIGURACION WHERE CLAVE ='Localidad'
        UNION
        SELECT 'cuit' as clave,isnull(valor,'') as valor FROM TA_CONFIGURACION WHERE CLAVE ='CUIT'
        UNION
        SELECT 'telefono' as clave,isnull(valor,'') as valor FROM TA_CONFIGURACION WHERE CLAVE ='Telefono'
        UNION
        SELECT 'calle' as clave,isnull(valor,'') as valor FROM TA_CONFIGURACION WHERE CLAVE ='Calle'
        UNION
        SELECT 'cpostal' as clave,isnull(valor,'') as valor FROM TA_CONFIGURACION WHERE CLAVE ='CPOSTAL'
        UNION
        SELECT 'iva' as clave,isnull(b.descripcion,'') as valor FROM TA_CONFIGURACION a LEFT JOIN TA_CONDIVA b on ltrim(a.valor) = ltrim(b.CODIGO) WHERE a.CLAVE ='CONDIVA'
        """

        data_company = self.get_response(query, f"Ocurrió un error al obtener los datos de la empresa.", True)

        company = {}
        for row in data_company:
            company[row['clave']] = row.get('valor', '')

        query = f"""SELECT convert(varchar,convert(decimal(15,2),importe)) as importe, CONVERT(NVARCHAR(10),fecha,103) as fecha,isnull(nombre,'') as nombre,idcomprobante,tc FROM V_MV_CPTE WHERE ID={idInvoice}"""
        data_header = self.get_response(query, f"Ocurrió un error al obtener los datos del comprobante.", True)

        for row in data_header:
            tc = row.get('tc', '')
            idcomprobante = row.get('idcomprobante', '')

        query = f"""SELECT descripcion,cantidad,convert(varchar,convert(decimal(15,2),importe)) as importe,convert(varchar,convert(decimal(15,2),isnull(total,0))) as total FROM V_MV_CpteInsumos WHERE tc='{tc}' AND idcomprobante='{idcomprobante}'"""
        data_body = self.get_response(query, f"Ocurrió un error al obtener el detalle del comprobante.", True)

        query = f"""
        SELECT c.descripcion, convert(varchar,convert(decimal(15,2),b.importe)) as importe FROM MV_APLICACION a LEFT JOIN MV_ASIENTOS b
        on a.TC = b.TC and a.SUCURSAL=b.SUCURSAL and a.NUMERO=b.NUMERO and a.LETRA=b.LETRA
        LEFT JOIN MA_CUENTAS c on b.CUENTA=c.CODIGO
        WHERE a.TCO_ORIGEN='{tc}' AND a.IDComprobante_ORIGEN='{idcomprobante}' and b.[DEBE-HABER]='D'
        """

        data_payments = self.get_response(query, f"Ocurrió un error al obtener los medios de pago.", True)

        body = render_template('email_invoice.html', company=company, header=data_header, detail=data_body, payment=data_payments)

        Email.send_email(to, f"Su compra en {company['nombre']}", body)

        return set_response(None, 200, 'OK')
