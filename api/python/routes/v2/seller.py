
from .master import MasterView
from flask_classful import route
from functions.general_customer import get_customer_response
from functions.responses import set_response
from flask import request


class SellerView(MasterView):
    def index(self):
        sql = f"""
        SELECT ltrim(idvendedor) as idvendedor, ltrim(nombre) as nombre,isnull(ltrim(clave),'1') as clave
        FROM V_TA_VENDEDORES
        """

        result, error = get_customer_response(sql, f" al obtener los vendedores", True, self.token_global)

        response = set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response

    @route('/config/<string:id_seller>')
    def get_config_seller(self, id_seller: str):
        query = f"""
        DECLARE @MODIFICA_CLASE_PRECIO NVARCHAR(2)
        DECLARE @VISUALIZA_CLIENTES NVARCHAR(2)
        DECLARE @MUESTRA_IMPORTES NVARCHAR(2)
        DECLARE @DESCUENTO_POR_ARTICULOS NVARCHAR(2)
        DECLARE @CONSULTA_STOCK_PEDIDOS NVARCHAR(2)
        DECLARE @CARGA_COBRANZAS NVARCHAR(2)
        DECLARE @PERMITE_VER_CTACTE NVARCHAR(2)
        DECLARE @PIDE_BULTOS_APP NVARCHAR(2)
        DECLARE @PIDE_PRECIO_APP NVARCHAR(2)

        DECLARE @DIRECCION NVARCHAR(100)
        DECLARE @NOMBRE NVARCHAR(100)
        DECLARE @TELEFONO NVARCHAR(50)
        DECLARE @EMAIL NVARCHAR(50)
        
        DECLARE @BLOQUEA_NP_STK_REAL_NEGATIVO NVARCHAR(2)
        DECLARE @BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO NVARCHAR(2)
        DECLARE @DECIMALESEAN NVARCHAR(3)
        DECLARE @TIPOEAN NVARCHAR(50)

        SET @PIDE_BULTOS_APP = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'PIDEBULTOSAPP')
        IF @PIDE_BULTOS_APP IS NULL OR @PIDE_BULTOS_APP = 'NO' SET @PIDE_BULTOS_APP = '0'
        IF @PIDE_BULTOS_APP = 'SI' SET @PIDE_BULTOS_APP = '1'

        SET @PIDE_PRECIO_APP = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'PIDEPRECIOAPP')
        IF @PIDE_PRECIO_APP IS NULL OR @PIDE_PRECIO_APP = 'NO' SET @PIDE_PRECIO_APP = '0'
        IF @PIDE_PRECIO_APP = 'SI' SET @PIDE_PRECIO_APP = '1'


        SET @BLOQUEA_NP_STK_REAL_NEGATIVO = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'BLOQUEA_NP_STK_REAL_NEGATIVO')
        IF @BLOQUEA_NP_STK_REAL_NEGATIVO IS NULL OR @BLOQUEA_NP_STK_REAL_NEGATIVO = 'NO' SET @BLOQUEA_NP_STK_REAL_NEGATIVO = '0'
        IF @BLOQUEA_NP_STK_REAL_NEGATIVO = 'SI' SET @BLOQUEA_NP_STK_REAL_NEGATIVO = '1'

        SET @BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO')
        IF @BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO IS NULL OR @BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO = 'NO' SET @BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO = '0'
        IF @BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO = 'SI' SET @BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO = '1'


        SET @MODIFICA_CLASE_PRECIO = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'VDOR_WEB_MODIFICA_CLASE_PRECIO_{id_seller}')
        IF @MODIFICA_CLASE_PRECIO IS NULL OR @MODIFICA_CLASE_PRECIO = 'NO' SET @MODIFICA_CLASE_PRECIO = '0'
        IF @MODIFICA_CLASE_PRECIO = 'SI' SET @MODIFICA_CLASE_PRECIO = '1'

        SET @VISUALIZA_CLIENTES = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'VDOR_WEB_VISUALIZA_CLIENTES_PROPIOS_{id_seller}')
        IF @VISUALIZA_CLIENTES IS NULL OR @VISUALIZA_CLIENTES = 'NO' SET @VISUALIZA_CLIENTES = '0'
        IF @VISUALIZA_CLIENTES = 'SI' SET @VISUALIZA_CLIENTES = '1'

        SET @MUESTRA_IMPORTES = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'VDOR_WEB_MOSTRAR_TOTALES_{id_seller}')
        IF @MUESTRA_IMPORTES IS NULL OR @MUESTRA_IMPORTES = 'NO' SET @MUESTRA_IMPORTES = '0'
        IF @MUESTRA_IMPORTES = 'SI' SET @MUESTRA_IMPORTES = '1'

        SET @DESCUENTO_POR_ARTICULOS = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'VDOR_WEB_DESCUENTO_POR_ARTICULO_{id_seller}')
        IF @DESCUENTO_POR_ARTICULOS IS NULL OR @DESCUENTO_POR_ARTICULOS = 'NO' SET @DESCUENTO_POR_ARTICULOS = '0'
        IF @DESCUENTO_POR_ARTICULOS = 'SI' SET @DESCUENTO_POR_ARTICULOS = '1'

        SET @CONSULTA_STOCK_PEDIDOS = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'VDOR_WEB_CONSULTA_STOCK_PEDIDOS_{id_seller}')
        IF @CONSULTA_STOCK_PEDIDOS IS NULL OR @CONSULTA_STOCK_PEDIDOS = 'NO' SET @CONSULTA_STOCK_PEDIDOS = '0'
        IF @CONSULTA_STOCK_PEDIDOS = 'SI' SET @CONSULTA_STOCK_PEDIDOS = '1'

        SET @CARGA_COBRANZAS = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'VDOR_WEB_PERMITE_COBRANZAS_{id_seller}')
        IF @CARGA_COBRANZAS IS NULL OR @CARGA_COBRANZAS = 'NO' SET @CARGA_COBRANZAS = '0'
        IF @CARGA_COBRANZAS = 'SI' SET @CARGA_COBRANZAS = '1'

        SET @PERMITE_VER_CTACTE = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'VDOR_WEB_PERMITE_VERCTACTE_{id_seller}')
        IF @PERMITE_VER_CTACTE IS NULL OR @PERMITE_VER_CTACTE = 'NO' SET @PERMITE_VER_CTACTE = '0'
        IF @PERMITE_VER_CTACTE = 'SI' SET @PERMITE_VER_CTACTE = '1'

        SET @DIRECCION = (SELECT VALOR FROM TA_CONFIGURACION WHERE CLAVE ='CALLE')
        SET @DIRECCION = @DIRECCION + ' ' + (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE ='NUMERO')
        SET @DIRECCION = @DIRECCION + ', ' + (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE ='LOCALIDAD')
        SET @DIRECCION = @DIRECCION + ' (' + (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE ='CPOSTAL') + ')'
        SET @DIRECCION = @DIRECCION + ', ' + (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE ='PROVINCIA')

        SET @NOMBRE = (SELECT ISNULL(VALOR,'SU EMPRESA') FROM TA_CONFIGURACION WHERE CLAVE = 'NOMBRE')
        SET @TELEFONO = (SELECT ISNULL(VALOR,'.') FROM TA_CONFIGURACION WHERE CLAVE = 'TELEFONO')
        SET @EMAIL = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE = 'EMAIL_DE')


        SET @DECIMALESEAN = (SELECT ISNULL(VALOR,'1') FROM TA_CONFIGURACION WHERE CLAVE = 'DECIMALESEAN')
        SET @TIPOEAN = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE = 'TIPOEAN')


        SELECT 'MODIFICA_CLASE_PRECIO' AS [key],@MODIFICA_CLASE_PRECIO as value
        UNION
        SELECT 'SOLO_CLIENTES_VENDEDOR' AS [key],@VISUALIZA_CLIENTES as value
        UNION
        SELECT 'MOSTRAR_TOTALES_PEDIDOS' AS [key],@MUESTRA_IMPORTES as value
        UNION
        SELECT 'DESCUENTO_POR_ARTICULO' AS [key],@DESCUENTO_POR_ARTICULOS as value
        UNION
        SELECT 'CONSULTA_STOCK_PEDIDOS' AS [key],@CONSULTA_STOCK_PEDIDOS as value
        UNION
        SELECT 'PERMITE_COBRANZAS' AS [key],@CARGA_COBRANZAS as value
        UNION
        SELECT 'PERMITE_VER_CTACTE' AS [key],@PERMITE_VER_CTACTE as value
        UNION
        SELECT 'EMP_DOMICILIO' as [key], @DIRECCION as value
        UNION
        SELECT 'EMP_NOMBRE' as [key], @NOMBRE as value
        UNION
        SELECT 'EMP_TELEFONO' as [key], @TELEFONO as value
        UNION
        SELECT 'EMP_EMAIL' as [key], @EMAIL as value

        UNION
        SELECT 'BLOQUEA_STK_REAL_NEGATIVO' as [key], @BLOQUEA_NP_STK_REAL_NEGATIVO as value
        UNION
        SELECT 'BLOQUEA_STK_COMPROMETIDO_NEGATIVO' as [key], @BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO as value
        UNION

        SELECT 'PIDE_BULTOS' as [key], @PIDE_BULTOS_APP as value
        UNION
        SELECT 'PIDE_PRECIO' as [key], @PIDE_PRECIO_APP as value
        UNION

        SELECT 'DECIMALESEAN' as [key], @DECIMALESEAN as value
        UNION
        SELECT 'TIPOEAN' as [key], @TIPOEAN as value

        """

        if id_seller:
            response = self.get_response(query, f"Ocurrió un error al obtener la configuración del vendedor", True, False)
        else:
            response = []

        # print(response)
        return set_response(response, 200)

    @route('/visitas/<string:id>/<int:dia>')
    def get_visitas_vendedor(self, id: str, dia: int):
        dia = 1 if dia == 0 else dia


        # sql = f"""
        # SELECT a.cliente, a.observaciones, b.razon_social as nombre,isnull(b.calle,'') as calle,b.numero,b.localidad, SUBSTRING(frecuencia,1,1) as lunes,SUBSTRING(frecuencia,2,1) as martes,SUBSTRING(frecuencia,3,1) as miercoles,
        # SUBSTRING(frecuencia,4,1) as jueves, SUBSTRING(frecuencia,5,1) as viernes,SUBSTRING(frecuencia,6,1) as sabado,SUBSTRING(frecuencia,7,1) as domingo,isnull(a.orden,1) as orden
        # FROM V_TA_FRECUENCIA_VDOR a LEFT JOIN Vt_Clientes b on a.Cliente = b.CODIGO
        # WHERE SUBSTRING(frecuencia,{dia},1)=1 and ltrim(a.idVendedor)='{id}'
        # """

        sql = f"""
         SELECT b.cliente,b.observaciones,a.RAZON_SOCIAL as nombre, isnull(a.calle,'') as calle,a.numero,a.localidad,
        SUBSTRING(frecuencia,1,1) as lunes,SUBSTRING(frecuencia,2,1) as martes,SUBSTRING(frecuencia,3,1) as miercoles,
        SUBSTRING(frecuencia,4,1) as jueves, SUBSTRING(frecuencia,5,1) as viernes,SUBSTRING(frecuencia,6,1) as sabado,SUBSTRING(frecuencia,7,1) as domingo,isnull(b.orden,1) as orden
        FROM Vt_Clientes a LEFT JOIN V_TA_FRECUENCIA_VDOR b ON a.CODIGO=b.Cliente and a.idvendedor = b.idvendedor LEFT JOIN TA_ESTADOS c on a.provincia=c.codigo 
        WHERE ltrim(a.IdVendedor)='{id}' and SUBSTRING(frecuencia,{dia},1)=1  ORDER BY SUBSTRING(FRECUENCIA,{dia},1) desc, CAST(SUBSTRING(b.orden, PATINDEX('%[0-9]%', b.orden), PATINDEX('%[^0-9]%', SUBSTRING(b.orden, PATINDEX('%[0-9]%', b.orden), LEN(b.orden) - PATINDEX('%[0-9]%', b.orden) + 1) + 'a') - 1) AS INT) ,  RAZON_SOCIAL  
        """
        if id:
            result, error = get_customer_response(
                sql, f" al obtener las visitas del vendedor {id}", True, self.token_global)
        else:
            error = False
            result = []

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response

    @route('/visits', methods=['POST'])
    def set_visits(self):
        # data = request.get_json()

        # for visit in data:
        #     date = visit.get('date')
        #     id_seller = visit.get('seller')
        #     obs = visit.get('obs', '')
        #     account = visit.get('account')
        #     visited = visit.get('visited')

        #     query = f"""
        #     INSERT INTO V_MV_STATUS (UNegocio,TC,IdComprobante,IdTarea,IdEstado,FechaHora,Usuario,Cuenta,Observaciones,Secuencia,IdTecnico,NroMov)
        #     VALUES ('   1','VV','Vdor:{id_seller}|Vis:{visited}','','','{date}','Vendedor App','{account}','{obs}',1,'', (SELECT isnull(MAX(NROMOV),0)+1 FROM V_MV_STATUS))
        #     """

        #     response = self.get_response(query, f"Ocurrió un error al obtener la configuración del vendedor", False, True)

        # return set_response(response, 200)
        return set_response("Proceso omitido.", 200)
        
    @route('/location/<string:id>')
    def get_location(self, id: str):

        sql = f"""
        SELECT top 1 id,lat,long,idvendedor,convert(NVARCHAR(18),fechahora,103) + ' ' + convert(NVARCHAR(18),fechahora,108) as fechahora 
        FROM S_TA_UBICACIONES_VENDEDOR 
        WHERE ltrim(idVendedor)='{id}' AND (lat <>'0.0' AND lat<>'0' AND lat<>'' AND not lat is null)
		ORDER BY fechahora DESC
        """

        result, error = get_customer_response(
            sql, f" al obtener la ubicaciones del vendedor {id}", True, self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response
