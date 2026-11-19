from routes.v2.master import MasterView
from functions.responses import set_response
from flask_classful import route
from datetime import datetime
from functions.Account import Customer,Account
from rich import print
from functions.Document import Document
from functions.general_customer import exec_customer_sql,get_customer_response

class AccountView(MasterView):
    def get(self, code: str):
        """Retorna el detalle de una cuenta"""

        result = []
        account = Customer(code, token=self.token_global)

        result.append({
            'codigo': account.code,
            'razon_social': account.name,
            'calle': account.address_street,
            'cuit': account.cuit,
            'idlista': account.id_list,
            'clase': account.price_class,
            'numero': account.address_number,
            'cpostal': account.address_cp,
            'localidad': account.address_location,
            'telefono': account.phone,
            'email': account.email,
            'observaciones': account.observations,
            'contacto': account.contact,
            'idvendedor':account.id_seller,
            'tipo_documento': account.document_type,
            'iva':account.iva,
            'exists': account.exists
        })

        if result:
            can_charge_invoice, list_invoices = self.get_expired_invoice(code)
            result[0]['puede_facturar'] = can_charge_invoice
            result[0]['facturas_vencidas'] = list_invoices

        return set_response(result,200,"")


    def get_expired_invoice(self, code):
        # Verifico si el sistema estÃ¡ configurado para facturar con vencidos
        sql = f"""
        SELECT valor,clave FROM TA_CONFIGURACION WHERE CLAVE='NoFacturaVencidos'
        """
        response = self.get_response(
            sql, f"OcurriÃ³ un error al obtener las configuraciÃ³n del sistema", True, False)

        can_charge_invoice = False
        if response:
            can_charge_invoice = response[0].get('valor', 'NO') == 'SI'

        if not can_charge_invoice:
            return True, []

        # Si el sistema estÃ¡ configurado para facturar con vencidos, verifico si el cliente puede facturar vencidos y si tiene facturas vencidas
        sql = f"""
        IF NOT Exists(Select * from ma_clavepin where Fecha is null and Cuenta = '{code}' and motivo = '10')
        SELECT tc,sucursal,numero,letra,convert(varchar(10),fecha,103) as fecha,convert(varchar(10),vencimiento,103) as vencimiento,convert(varchar,convert(decimal(15,2),importe)) as importe  FROM VE_CPTES_IMPAGOS 
        WHERE CUENTA = '{code}' and datediff(day,VENCIMIENTO,GETDATE())>7 and (TC='FP' OR TC='FC')
        ELSE
        SELECT null
        """

        response = self.get_response(
            sql, f"OcurriÃ³ un error al obtener las facturas vencidas", True, False)

        if response:
            if response[0].get('tc', '') == '':
                return True, []
            return False, response
        else:
            return True, response

    @route("/get")
    @route("/get/<string:type_account>")
    @route("/get/<string:type_account>/<string:id_seller>")
    def get_account(self, type_account: str = 'CL', id_seller: str = ''):
        """Retorna las cuentas """
        
        accounts = Account.get_accounts(self.token_global,type_account,id_seller)

        return set_response(accounts,200)

    @route("/card-payment-accounts")
    def get_card_payment_accounts(self):
        """Retorna las cuentas contables configuradas como tarjetas"""

        sql = """
        SELECT
            LTRIM(RTRIM(CODIGO)) AS codigo,
            LTRIM(RTRIM(DESCRIPCION)) AS descripcion,
            ISNULL(LTRIM(RTRIM(MedioDePago)), '') AS medio_pago
        FROM MA_CUENTAS
        WHERE ISNULL(MedioDePago, '') = 'TJ'
        ORDER BY DESCRIPCION
        """

        accounts = self.get_response(sql, " las cuentas contables de tarjetas", True, False)
        return set_response(accounts, 200)

    @route("/print/<string:cpte>/<string:tc>/<string:account>", methods=["POST"])
    def print_invoice(self, cpte: str, tc: str, account: str = ''):
        """ Retorna el detalle de un comprobante para imprimirlo"""

        response = Document.print(self.token_global,tc,cpte,account)
        return set_response(response,200)


    @route("/balances/<string:type_account>/<string:account>/<string:hide_zero>/<string:id_seller>")
    def get_balances(self, type_account: str = 'CL', account: str = '', hide_zero: str = '0', id_seller: str = ''):
        """ Retorna el saldo de las cuentas"""

        balances = Account.get_balances(self.token_global,type_account,account,hide_zero,id_seller)
        return set_response(balances,200)

    @route("/current/<string:account>/<string:datef>/<string:dateu>/<string:hide_zero>/<string:id_seller>")
    def get_current_account(self, account: str, datef: str = '', dateu: str = '', hide_zero: str = '0', id_seller: str = ''):
        """Retorna el detalle de la cuenta corriente """

        id_seller = '' if id_seller == '*' else id_seller

        datef = '' if datef == '*' else datef
        dateu = '' if dateu == '*' else dateu

        datef = datetime.strptime(datef, '%d%m%Y').strftime(
            '%d/%m/%Y') if datef else ''

        dateu = datetime.strptime(dateu, '%d%m%Y').strftime(
            '%d/%m/%Y') if dateu else ''

        datef = '' if datef == '*' else datef
        dateu = '' if dateu == '*' else dateu

        customer = Customer(account, self.token_global)
        result = customer.get_current(datef,dateu,hide_zero,id_seller)

        return set_response(result, 200, "")
        
    @route('/paginate/<int:page>')
    def paginate(self, page: int):

        page_size = 100

        sql = f"""
        DECLARE @PageNumber int
        DECLARE @PageSize int
        DECLARE @rs NVARCHAR(MAX)
        DECLARE @from NVARCHAR(10), @until NVARCHAR(10)

        set @PageNumber = {page}
        set @PageSize = {page_size}
        
        IF @PageNumber = 1
            SET @from = 1
        ELSE
            SET @from = (@PageNumber * @PageSize) - @PageSize + 1
            
        SET @until = (@PageNumber * @PageSize)  
        
        SET @rs = 'SELECT * FROM (
        SELECT ROW_NUMBER() OVER (ORDER BY codigo) as RowNr,
        codigo,isnull(CodigoOpcional,'''') as codigo_opcional,ltrim(RAZON_SOCIAL) as razon_social,
        ltrim(isnull(isnull(ltrim(calle),'''') + '' '' + isnull(numero,'''') + '' '' + isnull(piso,'''') +  '' '' + isnull(DEPARTAMENTO,''''),'''')) as calle, 
        ltrim(LOCALIDAD) as localidad,isnull(ltrim(NUMERO_DOCUMENTO),'''') as cuit,isnull(Clase,1) as clase,isnull(ltrim(iva),'''') as iva,
        isnull(descuento,0) as dto,''FC'' as cpte_default,isnull(ltrim(IdVendedor),'''') as idvendedor,isnull(ltrim(TELEFONO),'''') as telefono, isnull(LTRIM(MAIL),'''') as email,isnull(ltrim(idlista),'''') as lista
        FROM VT_PROVEEDORES WHERE DADA_DE_BAJA=0) g 
        WHERE RowNr BETWEEN ' + @from + ' AND ' + @until 
        
        EXEC(@rs)
        """

        result, error = get_customer_response(
            sql, f" al obtener los clientes en pagina {page}", True, self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response