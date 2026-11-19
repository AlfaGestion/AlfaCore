from routes.v2.master import MasterView
from functions.general_customer import exec_customer_sql
from functions.responses import set_response
from functions.Report import Report
from flask import request
from flask_classful import route
from functions.Company import Company

class ViewReport(MasterView):

    def index(self):
        try:
            report = Report(self.token_global)
            reports = report.getAll()
            return set_response(reports, 200)
        except Exception as e:
            self.log(e)
            return set_response(None, 404, f"{e}")

    def get(self, code:str):
        try:
            report = Report(self.token_global)
            reports = report.get(code)
            return set_response(reports, 200)
        except Exception as e:
            self.log(e)
            return set_response(None, 404, f"{e}")
    
    def delete(self, code:str):
        try:
            report = Report(self.token_global)
            reports = report.delete(code)
            return set_response(reports, 200)
        except Exception as e:
            self.log(e)
            return set_response(None, 404, f"{e}")

    @route('/execute', methods=['POST'])
    def execute(self):
        data = request.get_json()

        code = data.get('code', '')

        if not code:
            return set_response(None, 404, "No se informo el código.")

        try:
            report = Report(self.token_global)
            results = report.execute(code)

            return set_response(results, 200)
        except Exception as e:
            self.log(e)
            return set_response(None, 404, f"{e}")
        
    @route('/test_execute', methods=['POST'])
    def test_execute(self):
        data = request.get_json()

        query = data.get('query', '')

        if not query:
            return set_response(None, 404, "No se informó una consulta.")

        try:
            report = Report(self.token_global)
            results = report.testExecute(query)

            return set_response(results, 200)
        except Exception as e:
            self.log(e)
            return set_response(None, 404, f"{e}")

    def post(self):
        data = request.get_json()
        
        try:
            report = Report(self.token_global)
            results = report.save(data)
            return set_response(results, 200)
        except Exception as e:
            self.log(e)
            return set_response(None, 404, f"{e}")


    @route('/levels/')
    def levels(self):
        try:
            levels = Company.getPNLevels(self.token_global,True)
            return set_response(levels, 200)
        except Exception as e:
            self.log(e)
            return set_response(None, 404, f"{e}")
        
    @route('/tables/')
    def tables(self):
        try:
            tables = Report.get_availables_tables(self.token_global)
            return set_response(tables, 200)
        except Exception as e:
            self.log(e)
            return set_response(None, 404, f"{e}")
    
    @route('/table_fields/<string:table_name>/')
    def table_fields(self, table_name:str):
        try:
            tables = Report.get_fields_from_table(table_name,self.token_global)
            return set_response(tables, 200)
        except Exception as e:
            self.log(e)
            return set_response(None, 404, f"{e}")
        

    @route('/getViews/<string:code>/')
    def getViews(self, code:str):
        try:
            report = Report(self.token_global)
            reports = report.getViews(code)
            return set_response(reports, 200)
        except Exception as e:
            self.log(e)
            return set_response(None, 404, f"{e}")