from functions.general_customer import exec_customer_sql,get_customer_response
from functions.Log import Log
from flask import jsonify, abort

class Product:

    TOKEN = None

    code = ""
    name = ""


    def __init__(self, code:str, token:str):
        self.code = code

        self.TOKEN = token

    def block(self) -> bool:
        """Bloquea un product"""

        query = f"UPDATE V_MA_ARTICULOS SET desdetrigger=0, suspendido=1 WHERE ltrim(idarticulo)='{self.code}'"
        result, error = exec_customer_sql(query, f" al bloquear el articulo {self.code}", self.TOKEN)

        return not error

    def unblock(self) -> bool:
            """Desbloquea un product"""

            query = f"UPDATE V_MA_ARTICULOS SET desdetrigger=0, suspendido=0 WHERE ltrim(idarticulo)='{self.code}'"
            result, error = exec_customer_sql(query, f" al desbloquear el articulo {self.code}", self.TOKEN)

            return not error
    
    @staticmethod
    def search(token:str, search_text:str='',family:str='', customer:str='', code: str=''):
        """Busca productos"""

        where = ''
        where_art = ''

        if search_text:
            list_search = search_text.split(' ')
            for word in list_search:
                if word:
                    where += ("and " if where != '' else "") + f" descripcion like ''%{word}%'' "
                    where_art += ("and " if where_art != '' else "") + f" codigo like ''%{word}%'' "

            if where:
                where = "(" + where + ")"

            if where_art:
                where_art = "(" + where_art + ")"

            if where and where_art:
                where = where + " or " + where_art

        query = f"sp_web_getArticulosPrecios '{customer}', '{where}', '{family}','{code}'"

        # print(query)
        result, error = get_customer_response(query, f" al obtener los articulos", True,token)

        if error:
            Log.create(result, '', 'ERROR')
            abort(jsonify(result), 500)

        return result
    
    @staticmethod
    def create(token:str, data):

        query = f"""
        DECLARE @pIdArticulo NVARCHAR(25)
        set nocount on;EXEC sp_web_AltaArticulo '{data['code']}','{data['barcode']}','{data['name']}','{data['price']}','{data['cost']}','{data['aliciva']}','{data['exempt']}','{data['weighable']}','{data['ud']}','{data['category']}','{data['brand']}',@pIdArticulo OUTPUT
        SELECT ltrim(@pIdArticulo) as codigo
        """
        try:
            response = exec_customer_sql(query, f"Ocurrió un error al crear el artículo", token, True)
            #print(response[0][0][0])
            return response[0][0][0]
        except Exception as e:
            return None
        
        # try:
        #     response = exec_customer_sql(query, "Ocurrió un error al crear el artículo", token, True)
        #     #print(response)
        #     if response:
        #         return response[0]
        #     return None
        
        # except Exception as e:
        #     print(e)
        #     return None



