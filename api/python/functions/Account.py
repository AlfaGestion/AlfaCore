from functions.general_customer import exec_customer_sql,get_customer_response
from functions.Log import Log
from flask import jsonify, abort

TABLES = {
    'CL': 'VT_CLIENTES',
    'PR': 'VT_PROVEEDORES'
}

VAT_CONDITIONS = {
    'RI': '1',
    'RNI': '2',
    'CF': '3',
    'EX': '4',
    'MT': '5',
    'SNC': '6',
    'EXE': '7',
    'NR': '8'
}

class Account:
    TOKEN = None
    code = ""
    name = ""
    email = ""
    phone = ""
    password = ""
    cuit = ""
    iva = ""
    address = ""
    document_type = ""
    account_type = ""
    id_list = ""
    table = ""
    address_street =""
    address_number =""
    address_location =""
    address_cp =""
    id_list =""
    price_class =""
    observations =""
    contact =""
    id_seller =""
    exists=False

    def __init__(self, code: str, token: str, account_type:str = 'CL') -> None:
        self.code = code
        self.TOKEN = token

        self.account_type = account_type

        self.table = TABLES[account_type]
        
        self.__load()

    def __customer_query(self, where_clause: str) -> str:
        return f"""
        SELECT codigo,razon_social,isnull(mail,'') as mail,isnull(telefono,'') as telefono, isnull(ltrim(iva),'1') as iva,
        isnull(ltrim(numero_documento),'') as cuit,isnull(documento_tipo,'1') as documento_tipo,
        isnull(CALLE + ' ' + NUMERO + ' ' + LOCALIDAD,'') as domicilio,
        isnull(calle,'') as calle, isnull(numero,'') as numero, isnull(localidad,'') as localidad
        ,isnull(ltrim(idlista), '') as idlista, ltrim(clase) as clase,
        isnull(cpostal,'') as cpostal, isnull(observaciones,'') as observaciones,
        isnull(contacto,'') as contacto, isnull(ltrim(idvendedor),'') as idvendedor
        from {self.table}
        {where_clause}
        """

    def __load_from_result(self, result):
        if len(result) == 0:
            return False

        self.exists = True
        self.code = result[0][0]
        self.name = result[0][1]
        self.email = result[0][2]
        self.phone = result[0][3]
        self.iva = result[0][4]
        self.cuit = result[0][5]
        self.document_type = result[0][6]
        self.address = result[0][7]

        self.document_type = int(self.document_type) if self.document_type != 'None' else 1
        self.cuit = self.cuit.replace("-", "")

        self.address_street = result[0][8]
        self.address_number = result[0][9]
        self.address_location = result[0][10]
        self.id_list = result[0][11]
        self.price_class = result[0][12]
        self.address_cp = result[0][13]
        self.observations = result[0][14]
        self.contact = result[0][15]
        self.id_seller = result[0][16]
        return True

    def __load_by_web_marker(self) -> bool:
        if not self.code:
            return False

        web_marker = f"id_app {self.code}".replace("'", "''")
        query = f"""
        SELECT TOP 1 a.CODIGO
        FROM MA_CUENTASADIC a
        INNER JOIN VT_CLIENTES b ON a.CODIGO = b.CODIGO
        WHERE LTRIM(RTRIM(a.WEB)) = '{web_marker}'
          AND b.DADA_DE_BAJA = 0
          AND b.BLOQUEO = 0
        """

        result, error = exec_customer_sql(query, " al buscar el cliente por web", self.TOKEN, True)
        if error or len(result) == 0:
            return False

        self.code = result[0][0]
        return self.__load()

    def __load(self):
        query = self.__customer_query(
            f"where codigo='{self.code}' and dada_de_baja=0 and bloqueo=0"
        )
        
        result, error = exec_customer_sql(query, " al obtener la cuenta", self.TOKEN, True)

        if error:
            Log.create(result, '', 'ERROR')
            return

        if not self.__load_from_result(result):
            self.__load_by_web_marker()
            return
        

    def get_balance(self) -> float:
        """Retorna el saldo de una cuenta"""

        query = f"""
        exec sp_web_getSaldosCuentas '{self.account_type}','{self.code}','0',''
        """

        result, error = exec_customer_sql(query, " al obtener el saldo de la cuenta", self.TOKEN, True)

        if error:
            Log.create(result, '', 'ERROR')
            return 0
        
        try:
            return result[0][4]
        except Exception as e:
            return 0
    
    def get_current(self, date_from:str = '', date_until:str = '', hide_zero:str='0', id_seller:str='') -> list:
        """Retorna el detalle de la cuenta corriente"""

        response = []
        query = f"""
        EXEC sp_web_getCuentaCorriente '{self.code}','{date_from}','{date_until}','{hide_zero}','{id_seller}'
        """

        result, error = get_customer_response(query, f" al obtener la cuenta corriente de la cuenta {self.code}", True,self.TOKEN)

        if error:
            Log.create(result, '', 'ERROR')
            abort(jsonify(result), 500)

        # print(type(result))

        response.append({
            'balance': self.get_balance(),
            'current': result
        })

        return response
    
    @staticmethod
    def get_accounts(token:str,accounts_type:str,  id_seller:str =''):
        where = ''

        id_seller = '' if id_seller == '*' else id_seller

        where = " WHERE dada_de_baja=0 "
        where = where + f" and ltrim(idvendedor)='{id_seller}' " if id_seller != '' else where
        
        query = f"""
        SELECT codigo,razon_social from {TABLES[accounts_type]} {where}
        """

        result, error = get_customer_response(query, " al obtener las cuentas", True,token)


        if error:
            Log.create(result, '', 'ERROR')
            return []
        
        return result
    
    @staticmethod
    def get_balances(token:str,accounts_type:str, account:str = '',hide_zero:str = '0',id_seller:str =''):
        account = '' if account == '*' else account
        id_seller = '' if id_seller == '*' else id_seller

        query = f"""
        EXEC sp_web_getSaldosCuentas '{accounts_type}','{account}','{hide_zero}','{id_seller}'
        """

        result, error = get_customer_response(query, " al obtener los saldos de cuenta", True,token)

        if error:
            Log.create(result, '', 'ERROR')
            return []
        
        return result
        

class Customer(Account):

    def __init__(self, code: str, token: str) -> None:
        super().__init__(code, token, 'CL')
