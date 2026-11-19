import requests
import json
from rich import print
from functions.general_customer import get_customer_response, exec_customer_sql
from functions.Company import Company
from config import ML_API_ID,ML_API_SECRET_KEY
from datetime import datetime, timedelta

ML_CODE_VERIFIER = 're2_9A4UJDF8NeK6yCz88g-1MlBskSCPK3VsyEmg43g'

REDIRECT_URI = 'https%3A%2F%2Fwww.google.com%2F'

class MercadoLibre:

    error_message = None

    nickname = None
    site_id = None
    seller_id = None
    auth_code = None
    access_token = None
    refresh_token = None
    expires_in = None
    app_id = None
    client_secret = None
    ml_enabled = False

    db_token = None

    def __init__(self, db_token:str):
        self.db_token = db_token
        self.__get_user_data()

    def __get_user_data(self):
        company = Company(self.db_token)
        # print(dir(company))

        self.seller_id = company.APP_WEB_ML_USER_ID
        self.auth_code = company.APP_WEB_ML_CODE
        self.access_token = company.APP_WEB_ML_ACCESS_TOKEN
        self.refresh_token = company.APP_WEB_ML_REFRESH_TOKEN
        self.expires_in = company.APP_WEB_ML_EXPIRES_IN
        self.app_id = company.APP_WEB_ML_APP_ID
        self.client_secret = company.APP_WEB_ML_CLIENT_SECRET
        self.ml_enabled = company.APP_WEB_ML_ENABLED

        # print(self.access_token)

        # if self.access_token == '' or self.access_token == None:
        #     self.__get_access_token()

        # if self.expires_in:
        #     if datetime.strptime(self.expires_in, '%y-%m-%d %H:%M:%S') < now:
        #         self.__refresh_token()

        # if self.access_token is None:
        #     self.__get_access_token()

        #     if self.access_token is None:
        #         raise Exception(self.error_message)
        
    def __get_access_token(self):
        print("OBTENER TOKEN")
        payload = f"grant_type=authorization_code&client_id={self.app_id}&client_secret={self.client_secret}&code={self.auth_code}&redirect_uri={REDIRECT_URI}&code_verifier={self.auth_code}"
        # payload = f"grant_type=authorization_code&client_id={ML_API_ID}&client_secret={ML_API_SECRET_KEY}&code={self.auth_code}&redirect_uri={REDIRECT_URI}&code_verifier={ML_CODE_VERIFIER}"


        headers = {
            "accept": "application/json",
            "content-type": "application/x-www-form-urlencoded",
        }

        response = requests.request("POST", "https://api.mercadolibre.com/oauth/token", headers=headers, data=payload)

        data = json.loads(response.text)

        try:
            if data['error'] != '':
                self.access_token = None
                self.refresh_token = None
                self.expires_in = None

                self.error_message = data['error_description']
                return
        except Exception as e:
            print(e)
            pass

        now = datetime.now()
        expires = now + timedelta(seconds=data['expires_in'])

        self.access_token = data['access_token']
        self.refresh_token = data['refresh_token']
        self.expires_in = expires.strftime("%y-%m-%d %H:%M:%S")

        self.__save_value('ML_EXPIRES_IN',self.expires_in)
        self.__save_value('ML_REFRESH_TOKEN',self.refresh_token)
        self.__save_value('ML_ACCESS_TOKEN',self.access_token)
        
    def __save_value(self, key,value):
        query = f"""
            DELETE FROM TA_CONFIGURACION WHERE CLAVE='{key}'
            INSERT INTO TA_CONFIGURACION (GRUPO,CLAVE,VALOR)
            VALUES ('WEB','{key}','{value}')
            """

        result, error = exec_customer_sql(query, " al insertar",self.db_token,False)

        print(error)

    def __refresh_token(self):
        print("REFRESH TOKEN")
        headers = {
            "accept": "application/json",
            "content-type": "application/x-www-form-urlencoded",
        }

        payload = f"grant_type=refresh_token&client_id={ML_API_ID}&client_secret={ML_API_SECRET_KEY}&code={self.auth_code}&redirect_uri={REDIRECT_URI}&refresh_token={self.refresh_token}"

        response = requests.request("POST", "https://api.mercadolibre.com/oauth/token", headers=headers, data=payload)

        data = json.loads(response.text)
        
        try:
            if data['error'] != '':
                self.access_token = None
                self.refresh_token = None
                self.expires_in = None

                self.error_message = data['error_description']
                return
        except Exception as e:
            pass

        now = datetime.now()
        expires = now + timedelta(seconds=data['expires_in'])

        self.access_token = data['access_token']
        self.refresh_token = data['refresh_token']
        self.expires_in = expires.strftime("%y-%m-%d %H:%M:%S")

        self.__save_value('ML_EXPIRES_IN',self.expires_in)
        self.__save_value('ML_REFRESH_TOKEN',self.refresh_token)
        self.__save_value('ML_ACCESS_TOKEN',self.access_token)


    def get_user_info(self):
        headers = {"Authorization": f"Bearer {self.access_token}"}
        response = requests.request(
            "GET", "https://api.mercadolibre.com/users/me", headers=headers)

        return json.loads(response.text)

    def get_user_products(self):
        # print(f"https://api.mercadolibre.com/sites/MLA/search?seller_id={self.seller_id}")
        # response = requests.request("GET", f"https://api.mercadolibre.com/sites/MLA/search?seller_id={self.seller_id}")
        headers = {"Authorization": f"Bearer {self.access_token}"}
        response = requests.request("GET", f"https://api.mercadolibre.com/users/{self.seller_id}/items/search", headers=headers)

        data = json.loads(response.text)
        results = []

        for result in data['results']:
            results.append(self.get_product_info(result))

        return {"results":results}

    def get_product_info(self, product_id):
        headers = {"Authorization": f"Bearer {self.access_token}"}
        response = requests.request(
            "GET", f"https://api.mercadolibre.com/items/{product_id}", headers=headers)

        return json.loads(response.text)

    def get_product_description(self, product_id):
        headers = {"Authorization": f"Bearer {self.access_token}"}
        response = requests.request(
            "GET", f"https://api.mercadolibre.com/items/{product_id}/description", headers=headers)

        return json.loads(response.text)

    def get_product_comments(self, product_id):
        headers = {"Authorization": f"Bearer {self.access_token}"}
        response = requests.request(
            "GET", f"https://api.mercadolibre.com/questions/search?item={product_id}&api_version=4", headers=headers)

        comments = json.loads(response.text)
        # for comment in comments:
        #     comment
        return comments

    def get_answer_to_question(self, question_id):
        headers = {"Authorization": f"Bearer {self.access_token}"}
        response = requests.request(
            "GET", f"https://api.mercadolibre.com/questions/{question_id}?api_version=4", headers=headers)

        return json.loads(response.text)

    def publish_product(self, payload):

        headers = {
            "Authorization": f"Bearer {self.access_token}",
            "Content-Type": "application/json",
        }

        # payload = json.dumps(
        #     {
        #         "title": "Item de test - No Ofertar",
        #         "category_id": "MLA3530",
        #         "price": 2500,
        #         "currency_id": "ARS",
        #         "available_quantity": 10,
        #         "buying_mode": "buy_it_now",
        #         "condition": "new",
        #         "listing_type_id": "gold_special",
        #         "sale_terms": [
        #             {"id": "WARRANTY_TYPE", "value_name": "Garantía del vendedor"},
        #             {"id": "WARRANTY_TIME", "value_name": "90 días"},
        #         ],
        #         "pictures": [
        #             {
        #                 "source": "https://cdnx.jumpseller.com/libreria-micky/image/17987549/resize/635/635?1627599410"
        #             }
        #         ],
        #         "attributes": [
        #             {"id": "BRAND", "value_name": "Marca del producto"},
        #             {"id": "EAN", "value_name": "7898095297749"},
        #             {
        #                 "id": "MODEL",
        #                 "name": "Modelo",
        #                 "value_id": None,
        #                 "value_name": "EQ2122",
        #                 "value_struct": None,
        #                 "values": [{"id": None, "name": "EQ2122", "struct": None}],
        #                 "attribute_group_id": "OTHERS",
        #                 "attribute_group_name": "Otros",
        #                 "value_type": "string",
        #             },
        #         ],
        #     }
        # )

        response = requests.request(
            "POST", "https://api.mercadolibre.com/items", headers=headers, data=payload)

        return json.loads(response.text)

    # debería ejecutarse apenas se publica si response es ok (falta mandar el id del articulo)
    def add_product_description(self):
        payload = json.dumps(
            {
                "plain_text": "Descripción con Texto Plano  \n No se deben cambiar las fuentes \n Se debe mantener un texto plano"
            }
        )

        id_articulo = 0

        headers = {
            "Authorization": f"Bearer {self.access_token}",
            "Content-Type": "application/json",
        }

        response = requests.request(
            "POST", f"https://api.mercadolibre.com/items/{id_articulo}/description", headers=headers, data=payload)

        return json.loads(response.text)

    def get_orders_by_seller(self):
        headers = {"Authorization": f"Bearer {self.access_token}"}
        response = requests.request("GET", f"https://api.mercadolibre.com/orders/search?seller={self.seller_id}", headers=headers)

        return json.loads(response.text)
    

    def get_order_detail(self,order_id):
        headers = {"Authorization": f"Bearer {self.access_token}"}
        response = requests.request("GET", f"https://api.mercadolibre.com/orders/search?seller={self.seller_id}&q={order_id}", headers=headers)

        return json.loads(response.text)

    def print_shipping_label(self,shipping_id):
        headers = {"Authorization": f"Bearer {self.access_token}"}
        # print( f"https://api.mercadolibre.com/shipment_labels?shipment_ids={shipping_id}&savePdf=Y")
        response = requests.request("GET", f"https://api.mercadolibre.com/shipment_labels?shipment_ids={shipping_id}&savePdf=Y", headers=headers)

        return json.loads(response.text)
