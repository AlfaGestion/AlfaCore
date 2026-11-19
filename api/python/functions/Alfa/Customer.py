from functions.general_alfa import exec_alfa_sql
from functions.Log import Log
"""
Clase encargada de los clientes de Alfa (nuestros clientes)
NO SON LOS CLIENTES/USUARIOS DE LA APP
"""


class Customer:

    code = ""
    name = ""
    email = ""
    phone = ""
    password = ""
    cuit = ""
    iva = ""

    def load(self, code):
        query = f"""
        SELECT codigo,razon_social,isnull(mail,'') as mail,isnull(telefono,'') as telefono, ltrim(isnull(iva,'1')) as iva,
        isnull(numero_documento,'') as cuit FROM vt_clientes WHERE codigo='{code}'
        """
        result, error = exec_alfa_sql(query, "", True)

        if error:
            Log.create(result, '', 'ERROR')
            return

        if len(result) == 0:
            return

        self.code = result[0][0]
        self.name = result[0][1]
        self.email = result[0][2]
        self.phone = result[0][3]
        self.iva = result[0][4]
        self.cuit = result[0][5]

    def create(self, name: str, email: str, phone: str, cuit: str, iva: str) -> bool:
        #print(f"Customer.create({name}, {email}, {phone}, {cuit}, {iva})")
        query = f"""
        DECLARE @Codigo NVARCHAR(9)
        set nocount on;EXEC sp_web_altaClienteAlfa '{name}','{email}','{phone}','{cuit}','{iva}',@Codigo OUTPUT
        SELECT @Codigo as codigo
        """

        Log.create(query)

        result, error = exec_alfa_sql(query, "", True)

        if error:
            Log.create(result, '', 'ERROR')
            self.code = ""
            return False

        self.code = result[0][0]
        self.name = name
        self.email = email
        self.phone = phone
        self.cuit = cuit
        self.iva = iva

        return True
