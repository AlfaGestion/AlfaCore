from datetime import datetime
from functions.general_customer import get_customer_response, exec_customer_sql
from functions.responses import set_response
from flask_classful import route
from functions.Report import Report
from flask import request
from routes.v2.master import MasterView
import json



class ViewConsultas(MasterView):
    @route('/db')
    def index(self):
        query = f"""
        SELECT ROW_NUMBER() OVER (ORDER BY name) AS id,
        name AS table_name FROM sys.tables;
        """

        result = self.get_response(query, f"Ocurrió un error al obtener los datos.", True)

        return set_response(result, 200, "")

    @route('/tablas/<string:table>')
    def tablas(self, table: str):
        query = f"""
        SELECT
        ROW_NUMBER() OVER (ORDER BY column_name) AS id,
        column_name
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = '{table}'
        """

        result = self.get_response(query, f"Ocurrió un error al obtener los datos.", True)

        return set_response(result, 200, "")

    @route('/consulta', methods=['GET'])
    def consulta(self):
        data = request.get_json()
        query = data.get('query', '')

        print(query)

        result = self.get_response(query, f"Ocurrió un error al obtener los datos.", True)

        return set_response(result, 200, "")

    @route('/consultas', methods=['GET'])
    def get_consultas(self):
        query = f"""
        SELECT * 
        FROM V_TA_SCRIPT WHERE MARCA='CL' 
        AND NOT CLAVE IS NULL ORDER BY CLAVE;
        """
        # query = f"""
        # SELECT clave as code,grupo as name 
        # FROM V_TA_SCRIPT WHERE MARCA='CL' 
        # AND NOT CLAVE IS NULL ORDER BY CLAVE;
        # """

        result = self.get_response(query, f"Ocurrió un error al obtener los datos.", True)

        return set_response(result, 200, "")

    @route('/getPossiblesValues', methods=["POST"])
    def getPossiblesValues(self):
        data = request.get_json()
        field = data.get('field', '')
        table = data.get('table', '')

        if not field or not table:
            return set_response([], 404, 'Debe informar todos los datos')


        query = f"SELECT Distinct {field} FROM {table}"
        result, error = exec_customer_sql(query, f"No se pudo obtener los datos", self.token_global, True)
        
        response = set_response(result, 200, error)
        response['data'] = [tuple(row) for row in response['data']]

        finalResponse = json.dumps(response)

        return finalResponse