from .master import MasterView
# from flask_classful import route
from functions.general_customer import get_customer_response
from functions.responses import set_response
from flask_classful import route
from functions.MercadoLibre import MercadoLibre
import requests
from rich import print

class MercadoLibreView(MasterView):

    def index(self):

        sql = f"""
        SELECT ltrim(idfamilia) as codigo, ltrim(Descripcion) as descripcion FROM V_TA_FAMILIAS 
        WHERE Descripcion<>'' and idfamilia<>'' ORDER BY descripcion
        """

        result, error = get_customer_response(
            sql, f" al obtener las familias", True, self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response

    @route('/publicaciones', methods=['GET'])
    def publicaciones(self):

        try:
            ml = MercadoLibre(self.token_global)
            publicaciones = ml.get_user_products()

            print(publicaciones)
            return set_response(publicaciones)
        except Exception as e:
            print(e)
            return set_response(f"{e}",400)

    @route('/detalle/<string:id>', methods=['GET'])
    def detalle(self, id):
        producto = {
            'comments': None,
            'description': None
        }
        ml = MercadoLibre(self.token_global)

        # publicaciones = ml.get_user_products()

        # for publicacion in publicaciones["results"]:
        # publicacion['detail'] = ml.get_product_info(publicacion["id"])
        # publicacion['description'] = ml.get_product_info(publicacion["id"])
        # publicacion['comments'] = ml.get_product_comments(publicacion["id"])
        producto['comments'] = ml.get_product_comments(id)
        producto['description'] = ml.get_product_description(id)

        return set_response(producto)

    @route('/comentarios/<string:id>', methods=['GET'])
    def comentarios(self, id: str):
        ml = MercadoLibre(self.token_global)
        comentarios = ml.get_product_comments(id)

        return set_response(comentarios)

    @route('/agregar-descripcion/<string:id_product>', methods=['POST'])
    def agregar_descripcion(self, id: str):
        ml = MercadoLibre(self.token_global)
        descripcion = ml.add_product_description(id)

        return set_response(descripcion)

    @route('/ventas', methods=['GET'])
    def ventas(self):
        ml = MercadoLibre(self.token_global)
        ventas = ml.get_orders_by_seller()
        print(ventas)
        return set_response(ventas)

    @route('/publicar', methods=['POST'])
    def publicar(self):
        request_data = requests.request.get_json()

        ml = MercadoLibre(self.token_global)
        producto = ml.publish_product(self, request_data)

        return set_response(producto)
    
    @route('/etiqueta/<string:shipping_id>')
    def imprimir_etiqueta(self, shipping_id):
        ml = MercadoLibre(self.token_global)
        return ml.print_shipping_label(shipping_id)

    @route('/get-token')
    def obtener_token(self):
        ml = MercadoLibre(self.token_global)
        return set_response(ml.access_token)