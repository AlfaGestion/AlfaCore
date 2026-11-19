from datetime import datetime, timedelta
from flask import request
from functions.responses import set_response
from routes.v2.master import MasterView
from functions.general_customer import get_customer_response, exec_customer_sql
from flask_classful import route
from functions.Product import Product
from functions.Alfa.ShareDb import ShareDb
from functions.Account import Account
class ViewProducts(MasterView):

    def index(self):
        """Obtener todos los productos"""
        return set_response(Product.search(token=self.token_global),200)

    def post(self):
        """
        Retorna la información de un producto o de todos si no se envian parametros.
        Tambien recibe el codigo de lista de precio
        Uso el metodo POST por que el codigo de articulo puede tener caracteres que rompan la url
        """

        data = request.get_json()
        code = data.get('code', '')
        customer = data.get('customer', '')

        response = Product.search(token=self.token_global,customer=customer,code=code)

        create_product_if_not_exists = False

        #Si esta configurado, consulto en la base compartida
        if response == [] and create_product_if_not_exists:
            response = ShareDb.getProductByBarcode(code,self.token_global)
            if response and customer != "":
                cuenta = Account(customer,self.token_global)
                if cuenta.price_class:
                    response[0]['precio'] = response[0][f'precio{cuenta.price_class}'] 
                else:
                    response[0]['precio'] = response[0]['precio1']

                product = {
                    'code': '',
                    'barcode': code,
                    'cost': 0,
                    'name': response[0]['descripcion'],
                    'price':response[0]['precio'],
                    'aliciva':response[0]['tasaiva'],
                    'exempt': 1 if response[0]['exento'] else 0,
                    'weighable':0,
                    'ud':'',
                    'category':'',
                    'brand':''
                }

                Product.create(self.token_global, product)

        return set_response(response,200)
    

    @route('/search', methods=['POST'])
    def search(self):
        """
        Busca productos, recibe un string que busca un like en la descripcion, y un idfamilia
        Tambien recibe el customer, para verificar que precios debo retornar
        """

        data = request.get_json()
        search_text = data['search']
        id_family = data['family']
        customer = data['customer']

        # print(data)

        return set_response(Product.search(token=self.token_global,search_text=search_text,family=id_family,customer=customer),200)

    # @route('/families/<string:customer>', methods=['GET'])
    @route('/families', methods=['GET'])
    def get_families(self):
        """
        GET
        api/v2/products/families
        Obtiene las familias de productos
        """

        # sql = f"""SELECT ltrim(idfamilia) as codigo,descripcion from V_TA_FAMILIAS
        # WHERE descripcion<>'' ORDER BY idfamilia
        # """
        sql = f"""
        sp_web_getFamiliasArticulos ''
        """

        result, error = get_customer_response(
            sql, f" al obtener las familias", True, self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response

    @route('/getPriceLists')
    @route('/getPriceLists/<string:seller_id>')
    def get_price_lists(self, seller_id: str = ''):
        """
        GET
        api/v2/products/getPriceLists
        Obtiene las listas de precios
        """

        if seller_id:
            sql = f"""
            SELECT ltrim(a.IdLista) as idlista,b.nombre FROM Vt_Clientes a
            LEFT JOIN V_MA_PreciosCab b on a.IdLista = b.IdLista
            WHERE ltrim(a.IdVendedor)='{seller_id}' and a.IdLista <>'' and b.TipoLista='V' GROUP BY a.IdLista,b.Nombre
            """
        else:
            sql = f"""
            SELECT ltrim(idlista) as idlista, nombre FROM V_MA_PRECIOSCAB WHERE TipoLista = 'V'
            """

        result, error = get_customer_response(
            sql, f" al obtener las listas de precios", True, self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response

    @route("/pricelist/<string:pricelist_id>/<int:days_range>")
    @route("/pricelist/<string:pricelist_id>/<int:days_range>/<string:customerCode>")
    def price_list(self, pricelist_id: str, days_range: int, customerCode: str = ''):
        """
        Method: GET \n
        End point api/v2/products/pricelist \n
        Retorna una lista de precios de un cliente/vendedor
        """
        date_u = datetime.now()
        date_f = date_u - timedelta(days=days_range)

        sql = f"""
        sp_web_getListaDePrecios '{pricelist_id}', '{date_f.strftime("%d/%m/%Y")}', '{date_u.strftime("%d/%m/%Y")}','{customerCode}'
        """

        result, error = get_customer_response(
            sql, " al obtener la lista de precios", True, self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response

    @route("/pricelist/query/<string:pricelist_id>/<int:days_range>")
    @route("/pricelist/query/<string:pricelist_id>/<int:days_range>/<string:customerCode>/<string:query>")
    def price_list_query(self, pricelist_id: str, days_range: int, customerCode: str = '*', query: str = ''):

        date_u = datetime.now()
        date_f = date_u - timedelta(days=days_range)

        if(customerCode == '*'):
            customerCode = ''

        if(query == '*'):
            query = ''

        where = ''
        list_search = query.split(' ')
        for word in list_search:
            if word:
                where += ("or " if where != '' else "") + f" descripcion like ''%{word}%'' "

        if where:
            where = "(" + where + ")"

        sql = f"""
        sp_web_getListaDePrecios_query '{pricelist_id}', '{date_f.strftime("%d/%m/%Y")}', '{date_u.strftime("%d/%m/%Y")}','{customerCode}','{where}'
        """

        result, error = get_customer_response(sql, " al obtener la lista de precios filtrada", True, self.token_global)

        response = set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response

    @route('/block', methods=["POST"])
    def block(self):
        data = request.get_json()
        code = data.get('code', '')

        if not code:
            return set_response([], 404, 'Debe informar el código de artíclo')

        product = Product(code, self.token_global)
        error = not product.block()

        response = set_response([], 200 if not error else 404, "" if not error else f"Error al bloquear el articulo {code}")
        return response


    @route('/unblock', methods=["POST"])
    def unblock(self):
        data = request.get_json()
        code = data.get('code', '')

        if not code:
            return set_response([], 404, 'Debe informar el código de artíclo')

        product = Product(code, self.token_global)
        error = not product.unblock()

        response = set_response([], 200 if not error else 404, "" if not error else f"Error al desbloquear el articulo {code}")
        return response