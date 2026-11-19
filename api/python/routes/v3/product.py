from functions.general_customer import get_customer_response, exec_customer_sql
from functions.responses import set_response
from flask import request
from flask_classful import route
from routes.v2.master import MasterView
from functions.Product import Product
import decimal


class ViewProduct(MasterView):
    def index(self):
        sql = f"""
        SELECT
        ltrim(a.idarticulo) as idarticulo, CODIGOBARRA as codigobarras, ltrim(descripcion) as descripcion, isnull(ltrim(IDRUBRO),'') as idrubro,isnull(IdFamilia ,'') as idfamilia,
        convert(varchar,convert(decimal(15,2),isnull(IMPUESTOS,0))) as imp_internos,
        convert(varchar,convert(decimal(15,2),isnull(tasaiva,0))) as iva,
        convert(varchar,convert(decimal(15,2),isnull(EXENTO,0))) as exento,
        convert(varchar,convert(decimal(15,2),precio1 * isnull(d.coeficiente,1))) as precio1,
        convert(varchar,convert(decimal(15,2),precio2 * isnull(d.coeficiente,1))) as precio2,
        convert(varchar,convert(decimal(15,2),precio3 * isnull(d.coeficiente,1))) as precio3,
        convert(varchar,convert(decimal(15,2),precio4 * isnull(d.coeficiente,1))) as precio4,
        convert(varchar,convert(decimal(15,2),precio5 * isnull(d.coeficiente,1))) as precio5,
        convert(varchar,convert(decimal(15,2),precio6 * isnull(d.coeficiente,1))) as precio6,
        convert(varchar,convert(decimal(15,2),precio7 * isnull(d.coeficiente,1))) as precio7,
        convert(varchar,convert(decimal(15,2),precio8 * isnull(d.coeficiente,1))) as precio8,
        '0' as precio9, '0' as precio10
        FROM V_MA_ARTICULOS a
        LEFT JOIN S_TA_EQUIV d ON d.IDUNIDAD = a.UD_TTE and a.IDARTICULO = d.IDARTICULO
		and d.IDUNIDAD_EQUIV = a.IDUNIDAD
        WHERE SUSPENDIDO=0 AND SUSPENDIDOV=0
        """

        result, error = get_customer_response(
            sql, f" al obtener los articulos", True, self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response

    @route('/paginate/<int:page>')
    def paginate(self, page: int):

        page_size = 100

        sql = f"""
        DECLARE @PageNumber int
        DECLARE @PageSize int
        DECLARE @rs NVARCHAR(MAX)
        DECLARE @from NVARCHAR(10), @until NVARCHAR(10)
        DECLARE @precioDefecto NVARCHAR(2)

        set @PageNumber = {page}
        set @PageSize = {page_size}

        SET @precioDefecto = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE='APP_WEB_CLASEDEFECTO_APP_MOVIL')

		IF @precioDefecto = '' or @precioDefecto is null
			SET @precioDefecto = '1'
        
        IF @PageNumber = 1
			SET @from = 1
		ELSE
			SET @from = (@PageNumber * @PageSize) - @PageSize + 1
			
		SET @until = (@PageNumber * @PageSize)	
		
        SET @rs = 'SELECT * FROM (
        SELECT ROW_NUMBER() OVER (ORDER BY a.IDARTICULO) as RowNr,ltrim(a.idarticulo) as idarticulo, CODIGOBARRA as codigobarras, ltrim(descripcion) as descripcion, isnull(ltrim(IDRUBRO),'''') as idrubro,isnull(IdFamilia ,'''') as idfamilia,
        convert(varchar,convert(decimal(15,2),isnull(IMPUESTOS,0))) as imp_internos,
        convert(varchar,convert(decimal(15,2),isnull(tasaiva,0))) as iva,
        convert(varchar,convert(decimal(15,2),isnull(EXENTO,0))) as exento, 
        convert(varchar,convert(decimal(15,2),precio' + @precioDefecto + ' * isnull(d.coeficiente,1))) as precio1,
        convert(varchar,convert(decimal(15,2),precio2 * isnull(d.coeficiente,1))) as precio2,
        convert(varchar,convert(decimal(15,2),precio3 * isnull(d.coeficiente,1))) as precio3,
        convert(varchar,convert(decimal(15,2),precio4 * isnull(d.coeficiente,1))) as precio4,
        convert(varchar,convert(decimal(15,2),precio5 * isnull(d.coeficiente,1))) as precio5,
        convert(varchar,convert(decimal(15,2),precio6 * isnull(d.coeficiente,1))) as precio6,
        convert(varchar,convert(decimal(15,2),precio7 * isnull(d.coeficiente,1))) as precio7,
        convert(varchar,convert(decimal(15,2),precio8 * isnull(d.coeficiente,1))) as precio8,
        ''0'' as precio9, ''0'' as precio10
        FROM V_MA_ARTICULOS a
        LEFT JOIN S_TA_EQUIV d ON d.IDUNIDAD = a.UD_TTE and a.IDARTICULO = d.IDARTICULO
		and d.IDUNIDAD_EQUIV = a.IDUNIDAD
        WHERE SUSPENDIDO=0 AND SUSPENDIDOV=0
        ) g 
        WHERE RowNr BETWEEN ' + @from + ' AND ' + @until 
        
        EXEC(@rs)
        """

        result, error = get_customer_response(
            sql, f" al obtener los clientes en pagina {page}", True, self.token_global)

        total = 0
        pages = 0
        try:
            count_sql = """
            SELECT COUNT(*) FROM V_MA_ARTICULOS a
            WHERE SUSPENDIDO=0 AND SUSPENDIDOV=0
            """
            count_rows, count_error = exec_customer_sql(count_sql, " al contar articulos", self.token_global, True)
            if not count_error and count_rows:
                total = int(count_rows[0][0])
                pages = int((total + page_size - 1) / page_size) if total > 0 else 0
        except Exception:
            pass

        payload = {
            "rows": result,
            "total": total,
            "pages": pages,
            "page": page,
            "page_size": page_size
        }

        response = set_response(
            payload, 200 if not error else 404, "" if not error else result[0]['message'])
        return response
    
    @route('/paginate/listas/<int:page>')
    def paginateListas(self, page: int):

        page_size = 500

        sql = f"""
        DECLARE @PageNumber int
        DECLARE @PageSize int
        DECLARE @rs NVARCHAR(MAX)
        DECLARE @from NVARCHAR(10), @until NVARCHAR(10)
        DECLARE @precioDefecto NVARCHAR(2)

        set @PageNumber = {page}
        set @PageSize = {page_size}

        SET @precioDefecto = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE='APP_WEB_CLASEDEFECTO_APP_MOVIL')

		IF @precioDefecto = '' or @precioDefecto is null
			SET @precioDefecto = '1'
        
        IF @PageNumber = 1
			SET @from = 1
		ELSE
			SET @from = (@PageNumber * @PageSize) - @PageSize + 1
			
		SET @until = (@PageNumber * @PageSize)	
		
        SET @rs = 'SELECT * FROM (
        SELECT ROW_NUMBER() OVER (ORDER BY a.IDARTICULO) as RowNr,ltrim(p.idarticulo) as idarticulo, ltrim(p.descripcionarticulo) as descripcion, 
        ltrim(p.idlista) as lista,
        convert(varchar,convert(decimal(15,2),p.precio' + @precioDefecto + ' * isnull(d.coeficiente,1))) as precio1,
        convert(varchar,convert(decimal(15,2),p.precio2 * isnull(d.coeficiente,1))) as precio2,
        convert(varchar,convert(decimal(15,2),p.precio3 * isnull(d.coeficiente,1))) as precio3,
        convert(varchar,convert(decimal(15,2),p.precio4 * isnull(d.coeficiente,1))) as precio4,
        convert(varchar,convert(decimal(15,2),p.precio5 * isnull(d.coeficiente,1))) as precio5,
        convert(varchar,convert(decimal(15,2),p.precio6 * isnull(d.coeficiente,1))) as precio6,
        convert(varchar,convert(decimal(15,2),p.precio7 * isnull(d.coeficiente,1))) as precio7,
        convert(varchar,convert(decimal(15,2),p.precio8 * isnull(d.coeficiente,1))) as precio8,
        ''0'' as precio9, ''0'' as precio10
        FROM V_MA_PRECIOS p
		LEFT JOIN V_MA_ARTICULOS a on p.idarticulo = a.idarticulo
        LEFT JOIN S_TA_EQUIV d ON d.IDUNIDAD = a.UD_TTE and a.IDARTICULO = d.IDARTICULO
		and d.IDUNIDAD_EQUIV = a.IDUNIDAD
        WHERE SUSPENDIDO=0 AND SUSPENDIDOV=0
        ) g 
        WHERE RowNr BETWEEN ' + @from + ' AND ' + @until 
        
        EXEC(@rs)
        """

        result, error = get_customer_response(
            sql, f" al obtener los clientes en pagina {page}", True, self.token_global)

        total = 0
        pages = 0
        try:
            count_sql = """
            SELECT COUNT(*) FROM V_MA_PRECIOS p
            LEFT JOIN V_MA_ARTICULOS a on p.idarticulo = a.idarticulo
            WHERE SUSPENDIDO=0 AND SUSPENDIDOV=0
            """
            count_rows, count_error = exec_customer_sql(count_sql, " al contar listas", self.token_global, True)
            if not count_error and count_rows:
                total = int(count_rows[0][0])
                pages = int((total + page_size - 1) / page_size) if total > 0 else 0
        except Exception:
            pass

        payload = {
            "rows": result,
            "total": total,
            "pages": pages,
            "page": page,
            "page_size": page_size
        }

        response = set_response(
            payload, 200 if not error else 404, "" if not error else result[0]['message'])
        return response

    def post(self):
        data = request.get_json()

        name = data.get('name', '')
        barcode = data.get('barcode', '')
        price = data.get('price', 0)
        cost = data.get('cost', 0)
        aliciva = data.get('aliciva', '21')
        exempt = data.get('exempt', 0)
        weighable = data.get('weighable', 0)
        ud = data.get('ud', '1')
        category = data.get('category', '1')
        brand = data.get('brand', '1')
        code = data.get('code', '')

        if code == 'None' or code == None:
            code = ''
        if barcode == 'None' or barcode == None:
            barcode = ''
        if cost == 'None' or cost == 'NaN' or cost == '':
            cost = 0
        if price == 'None' or price == 'NaN' or price == '':
            price = 0
        if brand == 'None' or brand == 'null' or brand is None:
            brand = ''
        if category == 'None' or category == 'null' or category is None:
            category = ''
        if ud == 'None' or ud == 'null' or ud is None:
            ud = ''

        product = {
            'code': code,
            'barcode': barcode,
            'cost': cost,
            'name': name,
            'price':price,
            'aliciva':aliciva,
            'exempt': exempt,
            'weighable':weighable,
            'ud':ud,
            'category':category,
            'brand':brand
        }

        print(product)
    
        response = Product.create(self.token_global, product)
        return set_response(response, 200)

    @route('/dbFields')
    def dbFields(self):
        query = f"""
        SELECT
        ROW_NUMBER() OVER (ORDER BY column_name) AS id,
        column_name
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = 'V_MA_ARTICULOS';
        """

        result = self.get_response(query, f"Ocurrió un error al obtener los datos.", True)

        return set_response(result, 200, "")
    
    @route('/product', methods=['POST'])
    def getProduct(self):
        data = request.get_json()
        code = data.get('code', '')
        
        query = f"""
        SELECT * FROM V_MA_ARTICULOS WHERE ltrim(IDARTICULO) = '{code}'
        """

        result = get_customer_response(query, f"Ocurrió un error al obtener los datos.",return_list=True, token=self.token_global)

        def convert_decimal_to_float(d):
            if isinstance(d, decimal.Decimal):
                return float(d)
            return d

        if result and isinstance(result[0], list) and result[0]:
            item = result[0][0]
            product = {key: convert_decimal_to_float(value) for key, value in item.items()}

        else:
            print("No se encontraron datos válidos.")


        return set_response(product,200)
