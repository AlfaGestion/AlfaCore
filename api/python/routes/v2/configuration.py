from .master import MasterView
# from flask_classful import route
from functions.general_customer import get_customer_response, exec_customer_sql, get_config
from functions.responses import set_response
from flask import request
from flask_classful import route
from rich import print
from functions.Company import Company
import base64
import os
from functions.general_alfa import mkdir
from functions.session import get_info_session
from functions.Log import Log


FIELDS_IN_VALOR_AUX = [
    'APP_WEB_COMENTARIOS_DASHBOARD'
]


class ConfigurationView(MasterView):

    def post(self):

        data = request.get_json()
        query = ""

        for item in data:
            field = "VALOR"
            if item in FIELDS_IN_VALOR_AUX:
                field = "VALORAUX"

            query = query + f"""
            DELETE FROM TA_CONFIGURACION WHERE CLAVE='{item}'
            INSERT INTO TA_CONFIGURACION (GRUPO,CLAVE,{field}) VALUES ('WEB','{item}','{data[item]}')
            """
        try:
            exec_customer_sql(query, " al grabar la configuración", self.token_global)
            return set_response([])
        except Exception as e:
            return set_response(f"{e}", 400)
    
    @route('/upload_file', methods=['POST'])
    def upload_file(self):

        data = request.get_json()
        response = []

        if data:
            for item in data:
                b64_string = item['content']

                session = get_info_session(self.token_global)

                dirname = os.path.join('./files', f"{self.code_account}_{session[0].get('dbname')}" )
                try:
                    if not mkdir(dirname):
                        return set_response([], 404, f"No se pudo crear el directorio {dirname}.")
                    
                    file_data = base64.b64decode(b64_string)
                    document_filename = os.path.join(dirname, os.path.splitext(item['name'])[0]  + os.path.splitext(item['name'])[1])

                    with open(document_filename, "wb") as fh:
                        fh.write(file_data)

                    response.append({
                        'name': os.path.splitext(item['name'])[0],
                        'b64': b64_string
                    })
                    
                except Exception as e:
                    self.log(e)
                    return set_response([], 500, f"{e}.")
                #Log.create(f"Directorio : {dirname}")

        return set_response(response)


    def get(self):

        try:
            data = get_config(self.token_global)
            Log.create(f"Data1: {data}")

            company = Company(self.token_global)
            data.append({
                'key': 'CRT_LOCAL',
                'value': os.path.exists(company.e_crt)
            })

            data.append({ 
                'key': 'KEY_LOCAL',
                'value': os.path.exists(company.e_key)
            })

            image1 = ""
            image2 = ""
            image3 = ""
            logo = ""
            favicon = ""

            if os.path.exists(company.customer_favicon):
                with open(company.customer_favicon, "rb") as image_file:
                    favicon = base64.b64encode(image_file.read())
                    favicon = favicon.decode("utf-8")

            if os.path.exists(company.customer_logo):
                with open(company.customer_logo, "rb") as image_file:
                    logo = base64.b64encode(image_file.read())
                    logo = logo.decode("utf-8")
            
            if os.path.exists(company.customer_image1):
                with open(company.customer_image1, "rb") as image_file:
                    image1 = base64.b64encode(image_file.read())
                    image1 = image1.decode("utf-8")

            if os.path.exists(company.customer_image2):
                with open(company.customer_image2, "rb") as image_file:
                    image2 = base64.b64encode(image_file.read())
                    image2 = image2.decode("utf-8")

            if os.path.exists(company.customer_image3):
                with open(company.customer_image3, "rb") as image_file:
                    image3 = base64.b64encode(image_file.read())
                    image3 = image3.decode("utf-8")

            data.append({'key': 'C_FAVICON','value': favicon})
            data.append({'key': 'C_LOGO','value': logo})
            data.append({'key': 'C_IMAGE1','value': image1})
            data.append({'key': 'C_IMAGE2','value': image2})
            data.append({'key': 'C_IMAGE3','value': image3})

            Log.create(f"Data2: {data}")

            return set_response(data)
        except Exception as e:
            return set_response(f"{e}", 400)

    @route('/get_branch/<string:tc>')
    def get_branch(self, tc:str):
        if tc== "FC" or tc=="NC" or tc == "ND":
            company = Company(self.token_global)

            #Si usa electronica, cargo solo los pdv electronicos
            if company.enable_efc:
                query = f"""
                SELECT VALOR  pdv FROM TA_CONFIGURACION WHERE (CLAVE = 'PV_EFACTURA' OR CLAVE = 'PV_EFACTURA1' OR  CLAVE = 'PV_EFACTURA2'  OR CLAVE = 'PV_EFACTURA3'  OR CLAVE = 'PV_EFACTURA4'  OR CLAVE = 'PV_EFACTURA5') and VALOR<>''
                """
            else:
                query = f"SELECT X_SUC_DEFAULT as pdv FROM V_TA_Cpte WHERE CODIGO='{tc}' AND X_SUC_DEFAULT<>0 GROUP BY X_SUC_DEFAULT"
        else:
            query = f"""
            SELECT X_SUC_DEFAULT as pdv FROM V_TA_Cpte WHERE CODIGO='{tc}' AND X_SUC_DEFAULT<>0 GROUP BY X_SUC_DEFAULT
            """

        try:
            result, error = get_customer_response(query, f" al obtener los puntos de venta", True, self.token_global)

            return set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])

        except Exception as f:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + query)

        return set_response(None, 404, f"Ocurrió un error al obtener los puntos de venta de {tc}")

    @route('/delete_ml_configuration', methods=['POST'])
    def delete_ml_configuration(self):

        query = f"""
        DELETE FROM TA_CONFIGURACION
        WHERE CLAVE LIKE '%APP_WEB_ML%'
        AND CLAVE NOT IN ('APP_WEB_ML_CLIENT_SECRET', 'APP_WEB_ML_APP_ID', 'APP_WEB_ML_ENABLED', 'APP_WEB_ML_LOGIN_TIME');
        """
        response = self.get_response(query, f"Ocurrió un error al eliminar la configuración.", False, True)

        return set_response(response, 200)
