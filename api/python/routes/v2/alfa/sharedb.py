from functions.general_customer import  get_customer_response
from ..master import MasterView
from flask_classful import route
from functions.responses import set_response
from rich import print
from functions.Alfa.ShareDb import ShareDb

class AlfaShareDBView(MasterView):

    @route('/productByBarcode/<string:barcode>')
    def getProductByBarcode(self, barcode: str):
        result = []
        try:
            result = ShareDb.getProductByBarcode(barcode,self.token_global)
        except Exception as r:
            result = []

        # try:
        #     query = f"""
        #     SELECT ltrim(idarticulo) as idarticulo,descripcion,codigobarra FROM V_MA_ARTICULOS WHERE LTRIM(CODIGOBARRA)='{barcode}'
        #     """
        #     result, error = get_customer_response(query, "", True, self.token_global, False, '', True)

        # except Exception as r:
        #     error = True

        # if error:
        #     self.log("\nSENTENCIA : " + query)
        #     return set_response(result, 404, "No se pudo obtener información. Intente nuevamente.")

        return set_response(result, 200, "")
