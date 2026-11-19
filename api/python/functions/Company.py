import base64
from functions.general_customer import exec_customer_sql,get_customer_response
from functions.Log import Log
from functions.session import get_info_session


PN_MAX_LEVEL = 10
class Company:
    TOKEN = None

    code = ""
    e_crt = None
    e_key = None
    e_cuit = None
    e_branch = None
    vat_condition = None
    enable_efc = False
    final_prices = False

    customer_favicon = None
    customer_logo = None
    customer_image1 = None
    customer_image2 = None
    customer_image3 = None

    #Mercado Libre
    APP_WEB_ML_USER_ID = None
    APP_WEB_ML_CODE = None
    APP_WEB_ML_ACCESS_TOKEN = None
    APP_WEB_ML_REFRESH_TOKEN = None
    APP_WEB_ML_EXPIRES_IN = None
    APP_WEB_ML_APP_ID = None
    APP_WEB_ML_CLIENT_SECRET = None

    e_production = False
    e_type_print = ""
    e_format_print = ""

    name = ""
    address = ""
    location = ""
    state = ""
    phone = ""
    cuit = ""
    iibb = ""
    activity_start = ""
    session_data = []

    pn_levels = None

    include_print_logo = {
        'FC': False,
        'NC': False,
        'ND': False,
        'FP': False,
        'NP': False
    }

    logo = None

    branchs_default = {}

    format_print_default = ""

    def __init__(self, token:str) -> None:
        self.TOKEN = token

        self.session_data = get_info_session(token)
        if self.session_data:
            self.code = self.session_data[0]['idcliente']
        self.__load_config()

    def __load_logo(self) -> None:
        query = f"""
        SELECT IMAGEN FROM TA_LOGOS WHERE LTRIM(RTRIM(IDLOGO)) = 'LOGOEMPRESA'
        """

        result, error = exec_customer_sql(query, " al obtener la configuración de la empresa: Logo", self.TOKEN, True)

        if error:
            Log.create(result, '', 'ERROR')
            return

        if len(result) == 0:
            return

        if result[0][0] != '':
            #Convierto el logo de binario a base64
            try:
                logo = result[0][0]
                self.logo = base64.b64encode(logo).decode('ascii')
            except Exception as e:
                Log.create(f"Error al convertir el logo de la empresa : {e}")
                self.logo = ""

    def __load_config(self) ->None:
        query = f"""
        DECLARE @RUTA_CRT NVARCHAR(MAX)
        DECLARE @RUTA_KEY NVARCHAR(MAX)
        DECLARE @CUIT_WSFE NVARCHAR(20)
        DECLARE @PV_EFACTURA NVARCHAR(5)
        DECLARE @IVA NVARCHAR(4)
        DECLARE @EFC_PRODUCCION BIT
        DECLARE @USA_EFC NVARCHAR(2)
        DECLARE @WSAA NVARCHAR(MAX)

        DECLARE @APP_WEB_BRANCH_DEFAULT_NP NVARCHAR(4)
        DECLARE @APP_WEB_BRANCH_DEFAULT_FP NVARCHAR(4)
        DECLARE @APP_WEB_BRANCH_DEFAULT_FC NVARCHAR(4)

        DECLARE @APP_WEB_FORCE_BRANCH_NP NVARCHAR(4)
        DECLARE @APP_WEB_FORCE_BRANCH_FP NVARCHAR(4)
        DECLARE @APP_WEB_FORCE_BRANCH_FC NVARCHAR(4)

        DECLARE @PRECIOS_FINALES NVARCHAR(2)

        DECLARE @TIPO_FORMATO_IMPRESION_EFC NVARCHAR(20)
        DECLARE @FORMATO_IMPRESION_EFC NVARCHAR(250)
        DECLARE @INCLUIR_LOGO_FC NVARCHAR(2)
        DECLARE @INCLUIR_LOGO_FP NVARCHAR(2)
        DECLARE @INCLUIR_LOGO_OTROS NVARCHAR(2)

        DECLARE @nombre NVARCHAR(100)
        DECLARE @calle NVARCHAR(100)
        DECLARE @localidad NVARCHAR(50)
        DECLARE @provincia NVARCHAR(50)
        DECLARE @telefono NVARCHAR(100)
        DECLARE @cuit NVARCHAR(20)
        DECLARE @nro_iibb NVARCHAR(50)
        DECLARE @inicio_actividades NVARCHAR(50)

        DECLARE @ML_USER_ID NVARCHAR(50)
        DECLARE @ML_CODE NVARCHAR(250)
        DECLARE @ML_ACCESS_TOKEN NVARCHAR(250)
        DECLARE @ML_REFRESH_TOKEN NVARCHAR(250)
        DECLARE @ML_EXPIRES_IN NVARCHAR(100)
        DECLARE @ML_APP_ID NVARCHAR(250)
        DECLARE @ML_CLIENT_SECRET NVARCHAR(250)
        DECLARE @ML_ENABLED BIT

        DECLARE @SEND_EMAIL_NP NVARCHAR(50)
        DECLARE @EMAIL_NP NVARCHAR(50)


        SET @RUTA_CRT = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE = 'RUTA_CRT') IF @RUTA_CRT IS NULL SET @RUTA_CRT = ''
        SET @RUTA_KEY = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE = 'RUTA_ARCHIVOPRIVADA') IF @RUTA_KEY IS NULL SET @RUTA_KEY = ''
        SET @CUIT_WSFE = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE = 'WSFE_CUIT') IF @CUIT_WSFE IS NULL SET @CUIT_WSFE = ''
        SET @PV_EFACTURA = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE = 'PV_EFACTURA') IF @PV_EFACTURA IS NULL SET @PV_EFACTURA = ''
        SET @IVA = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE = 'CONDIVA') IF @IVA IS NULL SET @IVA = ''
        SET @WSAA = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE = 'WSAA_URL') IF @WSAA IS NULL SET @WSAA = 0
        SET @USA_EFC = (SELECT ISNULL(VALOR,'NO') FROM TA_CONFIGURACION WHERE CLAVE = 'UsaFCElectronica') IF @USA_EFC IS NULL SET @USA_EFC = 'NO'
        SET @PRECIOS_FINALES = (SELECT ISNULL(VALOR,'NO') FROM TA_CONFIGURACION WHERE CLAVE = 'PRECIOSFINALES') IF @PRECIOS_FINALES IS NULL SET @PRECIOS_FINALES = 'NO'

        SET @INCLUIR_LOGO_FC = (SELECT ISNULL(VALOR,'NO') FROM TA_CONFIGURACION WHERE CLAVE = 'PRINTLOGO_FC_NC_ND') IF @INCLUIR_LOGO_FC IS NULL SET @INCLUIR_LOGO_FC = 'NO'

        SET @INCLUIR_LOGO_FP = (SELECT ISNULL(VALOR,'NO') FROM TA_CONFIGURACION WHERE CLAVE = 'PRINTLOGO_PROFORMA') IF @INCLUIR_LOGO_FP IS NULL SET @INCLUIR_LOGO_FP = 'NO'

        SET @INCLUIR_LOGO_OTROS = (SELECT ISNULL(VALOR,'NO') FROM TA_CONFIGURACION WHERE CLAVE = 'PRINTLOGO_OTROS') IF @INCLUIR_LOGO_OTROS IS NULL SET @INCLUIR_LOGO_OTROS = 'NO'
        

        SET @TIPO_FORMATO_IMPRESION_EFC = (SELECT ISNULL(VALOR,'OTROS') FROM TA_CONFIGURACION WHERE CLAVE = 'FormatoImpresionEFactura') IF @TIPO_FORMATO_IMPRESION_EFC IS NULL SET @TIPO_FORMATO_IMPRESION_EFC = 'OTROS'

        SET @FORMATO_IMPRESION_EFC = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE = 'RUTA_FORMULARIOS_EFACTURA') IF @FORMATO_IMPRESION_EFC IS NULL SET @FORMATO_IMPRESION_EFC = ''
        

        SET @EFC_PRODUCCION = CHARINDEX('homo',@WSAA)


        SET @APP_WEB_BRANCH_DEFAULT_NP = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE = 'APP_WEB_BRANCH_DEFAULT_NP')
        IF @APP_WEB_BRANCH_DEFAULT_NP IS NULL SET @APP_WEB_BRANCH_DEFAULT_NP = ''

        SET @APP_WEB_BRANCH_DEFAULT_FP = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE = 'APP_WEB_BRANCH_DEFAULT_FP')
        IF @APP_WEB_BRANCH_DEFAULT_FP IS NULL SET @APP_WEB_BRANCH_DEFAULT_FP = ''

        SET @APP_WEB_BRANCH_DEFAULT_FC = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE = 'APP_WEB_BRANCH_DEFAULT_FC')
        IF @APP_WEB_BRANCH_DEFAULT_FC IS NULL SET @APP_WEB_BRANCH_DEFAULT_FC = ''


        SET @APP_WEB_FORCE_BRANCH_NP = (SELECT ISNULL(VALOR,'NO') FROM TA_CONFIGURACION WHERE CLAVE = 'APP_WEB_FORCE_BRANCH_NP')
        IF @APP_WEB_FORCE_BRANCH_NP IS NULL SET @APP_WEB_FORCE_BRANCH_NP = 'NO'

        SET @APP_WEB_FORCE_BRANCH_FP = (SELECT ISNULL(VALOR,'NO') FROM TA_CONFIGURACION WHERE CLAVE = 'APP_WEB_FORCE_BRANCH_FP')
        IF @APP_WEB_FORCE_BRANCH_FP IS NULL SET @APP_WEB_FORCE_BRANCH_FP = 'NO'

        SET @APP_WEB_FORCE_BRANCH_FC = (SELECT ISNULL(VALOR,'NO') FROM TA_CONFIGURACION WHERE CLAVE = 'APP_WEB_FORCE_BRANCH_FC')
        IF @APP_WEB_FORCE_BRANCH_FC IS NULL SET @APP_WEB_FORCE_BRANCH_FC = 'NO'


        SET @calle = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='CALLE')
        SET @localidad = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='LOCALIDAD')
        SET @provincia = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='PROVINCIA')
        SET @telefono = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='TELEFONO')
        SET @cuit = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='CUIT')
        SET @nro_iibb = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='NROINGRESOSBRUTOS')
        SET @inicio_actividades = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='INICIOACTIVIDADES')
        SET @nombre = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='NOMBRE')
        
        SET @ML_USER_ID = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='APP_WEB_ML_USER_ID')
        SET @ML_CODE = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='APP_WEB_ML_CODE')
        SET @ML_ACCESS_TOKEN = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='APP_WEB_ML_ACCESS_TOKEN')
        SET @ML_REFRESH_TOKEN = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='APP_WEB_ML_REFRESH_TOKEN')
        SET @ML_EXPIRES_IN = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='APP_WEB_ML_EXPIRES_IN')
        SET @ML_APP_ID = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='APP_WEB_ML_APP_ID')
        SET @ML_CLIENT_SECRET = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='APP_WEB_ML_CLIENT_SECRET')
        SET @ML_ENABLED = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='APP_WEB_ML_ENABLED')

        SET @SEND_EMAIL_NP = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='APP_WEB_SEND_EMAIL_NP')
        SET @EMAIL_NP = (select ISNULL(VALOR,'') from TA_CONFIGURACION where CLAVE='APP_WEB_EMAIL_NP')


        SELECT @RUTA_CRT, @RUTA_KEY,@CUIT_WSFE,@PV_EFACTURA,@IVA,@EFC_PRODUCCION,@USA_EFC,@APP_WEB_BRANCH_DEFAULT_NP,@APP_WEB_FORCE_BRANCH_NP,@APP_WEB_BRANCH_DEFAULT_FC,@APP_WEB_FORCE_BRANCH_FC,@APP_WEB_BRANCH_DEFAULT_FP,@APP_WEB_FORCE_BRANCH_FP,@PRECIOS_FINALES,@TIPO_FORMATO_IMPRESION_EFC,@FORMATO_IMPRESION_EFC,@INCLUIR_LOGO_FC,@INCLUIR_LOGO_FP,@INCLUIR_LOGO_OTROS,
        @calle,@localidad,@provincia,@telefono,@cuit,@nro_iibb,@inicio_actividades,@nombre,@ML_USER_ID,@ML_CODE,@ML_ACCESS_TOKEN ,@ML_REFRESH_TOKEN,@ML_EXPIRES_IN,@ML_APP_ID,@ML_CLIENT_SECRET,@ML_ENABLED,@SEND_EMAIL_NP,@EMAIL_NP
        """

        result, error = exec_customer_sql(query, " al obtener la configuración de la empresa", self.TOKEN, True)
        

        if error:
            Log.create(result, '', 'ERROR')
            return

        if len(result) == 0:
            return

        self.e_crt = result[0][0] #Esto es el configurado en Alfa, no me sirve para la web. Tengo que tomar el subido
        self.e_key = result[0][1] #Esto es el configurado en Alfa, no me sirve para la web. Tengo que tomar el subido

        db_name = self.session_data[0]['dbname']

        self.e_crt = f'./files/{self.code}_{db_name}/certificado.crt'
        self.e_key = f'./files/{self.code}_{db_name}/privada.key'

        self.customer_favicon = f'./files/{self.code}_{db_name}/favicon.png'
        self.customer_logo = f'./files/{self.code}_{db_name}/logo.png'
        self.customer_image1 = f'./files/{self.code}_{db_name}/image1.png'
        self.customer_image2 = f'./files/{self.code}_{db_name}/image2.png'
        self.customer_image3 = f'./files/{self.code}_{db_name}/image3.png'

        self.e_cuit = result[0][2]
        self.e_branch = result[0][3]
        self.vat_condition = result[0][4]
        self.e_production = int(result[0][5]) == 0 
        self.enable_efc =  result[0][6] == "SI" 

        self.e_cuit = self.e_cuit.replace("-", "")
        
        self.branchs_default = {
                'NP': {
                    'branch': result[0][7],
                    'force': result[0][8] == "SI"
                },
                'FC': {
                    'branch': result[0][9],
                    'force': result[0][10] == "SI"
                },
                'FP': {
                    'branch': result[0][11],
                    'force': result[0][12] == "SI"
                },
            }
        
        
        self.final_prices = result[0][13] == "SI" 
        self.e_type_print = result[0][14]

        if self.e_type_print == "OTROS":
            if '80' in result[0][15]:
                self.e_format_print = "80"
            elif '58' in result[0][15]:
                self.e_format_print = "58"
            else:
                self.e_format_print = self.format_print_default
        else:
            self.e_format_print = self.format_print_default

        self.__load_logo()

        self.include_print_logo = {
            'FC': result[0][16] == "SI",
            'NC': result[0][16] == "SI",
            'ND': result[0][16] == "SI",
            'FP': result[0][17] == "SI",
            'NP': result[0][18] == "SI",
            'CB': result[0][16] == "SI",
            'CBCT': result[0][16] == "SI",
            'CBFP': result[0][17] == "SI",
            'CC': False
        }
        
        self.address = result[0][19]
        self.location = result[0][20]
        self.state = result[0][21]
        self.phone = result[0][22]
        self.cuit = result[0][23]
        self.iibb = result[0][24]
        self.activity_start = result[0][25]
        self.name = result[0][26]

        self.APP_WEB_ML_USER_ID = result[0][27]
        self.APP_WEB_ML_CODE = result[0][28]
        self.APP_WEB_ML_ACCESS_TOKEN = result[0][29]
        self.APP_WEB_ML_REFRESH_TOKEN = result[0][30]
        self.APP_WEB_ML_EXPIRES_IN = result[0][31]
        self.APP_WEB_ML_APP_ID = result[0][32]
        self.APP_WEB_ML_CLIENT_SECRET = result[0][33]
        self.APP_WEB_ML_ENABLED = result[0][34]

        self.send_email_np = result[0][35]
        self.email_np = result[0][36]


        # self.pn_levels = Company.getPNLevels(self.TOKEN, True)


    @staticmethod
    def getPNLevels(token:str, only_data: bool=False) ->list:
        """Retorna los niveles válidos para los codigos de reporte personalizados"""

        result = []

        query = f"""
        SELECT replace(clave,'PN_NIVEL','') as clave,valor FROM TA_CONFIGURACION WHERE CLAVE LIKE 'PN_NIVEL%' ORDER BY LEN(CLAVE), replace(CLAVE,'PN_NIVEL','')
        """
        result, error = get_customer_response(query, " al obtener la configuración de la empresa",  True,token)

        if error:
            Log.create(result, '', 'ERROR')
            return
        
        if not only_data:
            acumValue = 0
            for item in result:
                acumValue = acumValue + int(item['valor'])
                item['valor'] = acumValue

        
        return result