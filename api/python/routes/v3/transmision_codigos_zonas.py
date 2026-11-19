from flask import request
from functions.responses import set_response
from routes.v2.master import MasterView
from functions.general_customer import exec_customer_sql


class ViewTransmisionCodigosZonas(MasterView):

    def post(self):
        data = request.get_json()
           
        #return set_response(data,200)

        query = ''
        for item in data:
            #return set_response(item['zona'],200)
            query = query + f"""
            INSERT INTO MV_TransmisionesCodigos (codigo,zona)
            VALUES ('{item['codigo']}','{item['zona']}')
            """
            
        #return set_response(self.code_account,200)
        try:
            exec_customer_sql(query, " al insertar los códigos de zona", self.token_global)
            #return set_response()
        except Exception as r:
            error = True
        
        #return set_response(result,200)
        
        #if error:
        #    self.log(str(result) + "\nSENTENCIA : " + query)
        #    return set_response(None, 404, "Ocurrió un error al registrar el codigo/zona.")
        
        return set_response([],200)