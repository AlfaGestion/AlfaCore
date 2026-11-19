
from functions.general_customer import  get_customer_response
from functions.responses import set_response
from functions.Log import Log

class ShareDb:
    
    @staticmethod
    def getProductByBarcode(barcode,token):
        try:
            query = f"""
            SELECT ltrim(idarticulo) as idarticulo,descripcion,codigobarra, convert(varchar,convert(decimal(15,2),precio1)) as precio1,
            convert(varchar,convert(decimal(15,2),precio2)) as precio2, convert(varchar,convert(decimal(15,2),precio3)) as precio3,
            convert(varchar,convert(decimal(15,2),precio4)) as precio4,exento,tasaiva 
            
            FROM V_MA_ARTICULOS WHERE LTRIM(CODIGOBARRA)='{barcode}'
            """
            result, error = get_customer_response(query, "", True,token, False, '', True)

        except Exception as r:
            Log.create(f"Error al consultar articulo por codigo de barras : {barcode}. ")
            Log.create(f"{r}")
            return None
        
        if error:
            return None

        return result
