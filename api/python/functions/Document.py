from flask import jsonify
from functions.general_customer import exec_customer_sql, get_customer_response
from functions.Log import Log
from functions.Account import Customer, VAT_CONDITIONS
from datetime import datetime
from functions.AfipInvoice import Afip
from functions.Company import Company
import os
from rich import print

LETTER_A = "A"
LETTER_B = "B"
LETTER_C = "C"
LETTER_X = "X"

DOC_CUIT = 1
DOC_DNI = 3

AFIP_DOC_CUIT = 80
AFIP_DOC_DNI = 99

AFIP_AMOUNT_MAX_DECLARE_DOCUMENT = 61534

PRODUCTION = False

DOCUMENTS_CODE = {
    'PROFORMA': 'FP',
    'FACTURA': 'FC',
    'CREDITO': 'NC',
    'DEBITO': 'ND',
    'PEDIDO': 'NP'
}

class Document:

    customer = None
    electronic_document = None
    is_electronic = False
    code = ''
    letter = ''
    branch = ''
    number = ''
    total = 0
    subtotal = 0
    neto = 0
    date = None
    items = []
    TOKEN = None
    seller = None
    observations = ''
    receipt_id = None
    vat_condition = None
    company = None
    custom_customer_name = None
    custom_customer_phone = None
    custom_customer_email = None


    def __init__(self, code: str, customer: Customer, token: str,branch: str='',date: str = '') -> None:

        self.items = []
        self.TOKEN = token
        self.code = code
        self.branch = branch
        self.customer = customer
        
        self.company = Company(token)

        if not self.company.enable_efc:
            self.is_electronic = False
        else:
            # Si es FC, NC o ND y usa factura electronica, entonces es electronica
            self.is_electronic = (self.code == DOCUMENTS_CODE["CREDITO"] or self.code == DOCUMENTS_CODE["DEBITO"] or self.code == DOCUMENTS_CODE["FACTURA"]) and self.company.enable_efc

        if not date:
            date = datetime.now()
            date = date.strftime('%d/%m/%Y')
        else:
            date = datetime.strptime(date, '%Y-%m-%d')
            date = date.strftime('%d/%m/%Y')

        self.date = date

        self.__validate_customer()
        self.__get_letter()

        if self.is_electronic:
            self.electronic_document = ElectronicDocument(token=self.TOKEN, document=self, production=PRODUCTION)
        
        self.__validate_branch()

    def __validate_branch(self):
        """Obtiene el punto de venta por defecto configurado para la web, del comprobante seleccionado"""
        #Verifico los puntos de venta por defecto, si se informo y no lo recibo, lo tomo
        #Si se informo, y se forza el usar, lo tomo, si no, respeto el que trae.
        data = self.company.branchs_default.get(self.code)
        branch_def = data.get('branch')
        force = data.get('force')

        #Si tengo un forzado
        if force:
            self.branch = str(branch_def).zfill(4)
            return
        else:
            #Si no forza y tengo una branch default, verifico que hayan informado la branch, si no tomo la default
            if branch_def != '' and (self.branch == "" or self.branch == None):
                self.branch = str(branch_def).zfill(4)

        #Si no tengo punto de venta, lo obtengo directamente desde la base
        if self.branch == "" or self.branch is None:
            self.__get_branch_from_document()

    def set_custom_name(self, name:str):
        self.custom_customer_name = name

    def set_custom_phone(self, phone:str):
        self.custom_customer_phone = phone

    def set_custom_email(self, email:str):
        self.custom_customer_email = email

    def set_seller(self, seller: str):
        self.seller = seller

    def set_observations(self, observations: str):
        self.observations = observations

    def add_item(self, code: str, name: str, quantity: float, price: float, discount: float = 0, iva: float = 21, neto: float = 0, iva_amount: float= 0, umed:int = 7) -> None:
        """Agrega un articulo al comprobante"""

        #Verifico si son precios finales
        # if self.company.final_prices:
        #     neto = 2

        self.items.append({
            'code': code,
            'name': name,
            'qty': quantity,
            'price': float(neto), #float(price),
            'iva': iva,
            'neto': float(neto),
            'iva_amount': float(iva_amount),
            'total': float(price),
            'discount': discount,
            "umed": umed
        })

        self.total +=  float(price) * quantity

    def get_next_number(self) -> int:
        """Obtiene el próximo número del comprobante"""
        if self.is_electronic:
            self.number = self.electronic_document.get_next_number()
        else:
            self.number = self.__get_next_number()

    def save(self) -> bool:

        error = False
        result = []
        product_result = []

        if self.is_electronic:
            #Genero la electrónica
            try:
                self.electronic_document.generate()

                if(self.electronic_document.cae == ""):
                    raise Exception(f"No se pudo generar la factura electrónica.")
                
                Log.create(self.electronic_document.result,type="INFO")

                self.number = str(self.electronic_document.number).zfill(8)
                self.branch = str(self.electronic_document.branch).zfill(4)
                # self.letter = self.electronic_document.letter

            except TypeError as t:
                Log.create(f"Ocurrió un error al generar la factura electrónica: {t}")
                raise Exception(f"Ocurrió un error al generar la factura electrónica. Comuniquese con el encargado de sistemas.")
            except Exception as e:
                Log.create(f"Ocurrió un error al generar la factura electrónica: {e}")
                raise Exception(f"Factura Electrónica: {e}")
        else:
            self.get_next_number()

        sql = f"""
        DECLARE @pRes INT
        DECLARE @pMensaje NVARCHAR(250)
        DECLARE @pIdCpte INT

        set nocount on; EXEC sp_web_Alta_Comprobante '{self.customer.code}','{self.seller}','{self.date}','{self.observations}','0','0','{self.code}','{self.branch}','{self.number}','{self.letter}',@pRes OUTPUT, @pMensaje OUTPUT,@pIdCpte OUTPUT
        

        SELECT @pRes as pRes, @pMensaje as pMensaje, @pIdCpte pIdCpte
        """

        Log.create(sql)
        try:
            result, error = exec_customer_sql(sql, "", self.TOKEN, True)
        except Exception as r:
            error = True

        if error:
            Log.create(str(result[0]['message']) + "\nSENTENCIA : " + sql)
            raise Exception("Ocurrió un error al grabar el comprobante")

        response_code = result[0][0]
        response_message = result[0][1]
        receipt_id = result[0][2]

        if response_code == 11:
            for item in self.items:
                error = False


                sql = f"""
                SET NOCOUNT ON;
                DECLARE @pRes INT
                DECLARE @pMensaje NVARCHAR(250)
                DECLARE @pIdCpte INT

                EXEC sp_web_CpteInsumos {receipt_id},'{item['code']}',{item['qty']},{item['total']},{item['discount']},@pRes OUTPUT, @pMensaje OUTPUT,@pIdCpte OUTPUT

                SELECT @pRes as pRes, @pMensaje as pMensaje, @pIdCpte pIdCpte
                """
                
                try:
                    product_result, error = exec_customer_sql(sql, f".No se pudo grabar el producto. \n {sql}", self.TOKEN)
                except Exception as f:
                    error = True

                if error:
                    Log.create(f"{str(result[0]['message'])}\nSENTENCIA : {sql}")
                    raise Exception("Ocurrió un error al grabar el producto")
        else:
            Log.create(response_message)
            raise Exception(response_message)

        if self.custom_customer_name != "" or self.custom_customer_email != "" or self.custom_customer_phone != "":

            sql = f"""UPDATE V_MV_CPTE SET NOMBRE = '{self.custom_customer_name}', SOLICITANTE = '{self.custom_customer_email}', TELEFONO = '{self.custom_customer_phone}' WHERE ID = {receipt_id}"""

            try:
                result, error = exec_customer_sql(sql, "", self.TOKEN, False)
            except Exception as e:
                error = True

            if error:
                Log.create(str(result[0]['message']) + "\nSENTENCIA : " + sql)
                raise Exception("Ocurrió un error al grabar el comprobante. Datos extra.")

        self.receipt_id = receipt_id

        if self.is_electronic:
            #Inserto en V_MV_CPTE_ELECTRONICOS
            query = f"""
            INSERT INTO V_MV_CPTE_ELECTRONICOS (IdRequerimiento,CantReg,Presta_serv,TC,IDCOMPROBANTE,Tipo_doc,Nro_doc,Tipo_cpte,Punto_vta,Cpte_desde,Cpte_Hasta,Imp_total,imp_noGrav,Imp_netoGrav,Imp_impuestoLiquidado,imp_impuestos_rni,imp_opExentas, fecha_cpte, Resultado,Motivo,CAE,Error,VtoCae,CodigoBarraCae,archivado)

            VALUES ('{self.electronic_document.cae}',1,0,'{self.code}','{self.branch}{self.number}{self.letter}',{self.electronic_document.customer_document_type},'{self.electronic_document.customer_document_number}',{self.electronic_document.afip_code},'{self.branch}','{self.number}','{self.number}',{self.electronic_document.result['imp_total']},0,{self.electronic_document.result['imp_neto']},{self.electronic_document.result['imp_iva']},0,{self.electronic_document.result['imp_op_ex']},'{self.date}','A','','{self.electronic_document.cae}','','{self.electronic_document.cae_expiration}','',0)
            """

            try:
                result, error = exec_customer_sql(query, "", self.TOKEN, False)
            except Exception as e:
                error = True

            if error:
                Log.create(str(result[0]['message']) + "\nSENTENCIA : " + sql)
                raise Exception("Ocurrió un error al grabar el comprobante en la tabla V_MV_CPTE_ELECTRONICOS.")

        self.items = []

    def __get_next_number(self) -> str:
        """Obtiene el próximo número de un comprobante no electrónico"""
        error = False
        result = []

        query = f"""
        DECLARE @idComprobante NVARCHAR(13) 
        DECLARE @numero INT
        DECLARE @nvoNumero NVARCHAR(8)
        DECLARE @numeroComprobante NVARCHAR(13)
        DECLARE @tc NVARCHAR(4)
        DECLARE @sucursal NVARCHAR(4)
        DECLARE @letra NVARCHAR(4)
        
        SET @tc = '{self.code}'
        SET @sucursal = '{self.branch}'
        SET @letra = '{self.letter}'
            
        SELECT @idComprobante = ISNULL(MAX(IDCOMPROBANTE),@sucursal + '00000000' + @letra) 
        FROM V_MV_CPTE WHERE TC = @tc AND SUBSTRING(IDCOMPROBANTE,13,1) = @letra AND SUBSTRING(IDCOMPROBANTE,1,4) = @sucursal
        SET @numero = CAST(SUBSTRING(@idComprobante,5,8) AS INT)
        SET @numero = @numero + 1
        SET @nvoNumero = dbo.FN_FMT_LEERCODIGO(CAST (@numero as NVARCHAR(8)),8)
        SET @nvoNumero = REPLACE(@nvoNumero,' ','0')
        SELECT @nvoNumero
        """

        try:
            result = exec_customer_sql(
                query, " al obtener el próximo número", self.TOKEN, True)

            return result[0][0][0]
        except Exception as f:
            error = True

        if error:
            Log.create(str(result[0]['message']) + "\nSENTENCIA : " + query)

    def __validate_customer(self) -> None:

        if self.customer is None:
            raise Exception("Debe informar el cliente")

        if self.is_electronic:
            if self.customer.iva == "" or self.customer.iva is None:
                raise Exception(
                    "El cliente seleccionado no tiene informada la condición de IVA")

            if (self.customer.cuit == "" or self.customer.cuit is None) and (self.customer.iva != VAT_CONDITIONS['CF']):
                raise Exception(
                    "El cliente seleccionado no tiene informada la CUIT")

    def __get_letter(self) -> None:
        
        if self.code == DOCUMENTS_CODE['PROFORMA'] or self.code == DOCUMENTS_CODE['PEDIDO']:
            self.letter = LETTER_X
            return
        
        #Responsable inscripto
        if self.company.vat_condition == VAT_CONDITIONS['RI']:
            
                #Si es monotributo o responsable inscripto
                if self.customer.iva == VAT_CONDITIONS['MT'] or self.customer.iva == VAT_CONDITIONS['RI']:
                    self.letter = LETTER_A
                
                #Si es consumidor final o exento
                if self.customer.iva == VAT_CONDITIONS['CF'] or self.customer.iva == VAT_CONDITIONS['EX']:
                    self.letter = LETTER_B

        elif self.company.vat_condition == VAT_CONDITIONS['MT']:
            #Monotributo
            self.letter = LETTER_C

    def __get_branch_from_document(self) -> str:
        query = f"""
        SELECT TOP 1 {self.letter}_SUC_DEFAULT FROM V_TA_CPTE WHERE CODIGO ='{self.code}' AND LETRAS LIKE '%{self.letter}%'
        """

        result, error = exec_customer_sql(query, " al obtener la sucursal del comprobante", self.TOKEN, True)

        if error:
            Log.create(result, '', 'ERROR')
            raise Exception(f"No se pudo obtener la sucursal del comprobante")

        if len(result) == 0:
            raise Exception(f"No hay talonarios configurados para el comprobante {self.code}, letra {self.letter}.")
        
        self.branch = str(result[0][0]).zfill(4)

    def set_document_customer(self, document_number):
        if self.is_electronic:
            self.electronic_document.set_customer_document_number(document_number)

    def set_document_type_customer(self, document_type):
        if self.is_electronic:
            self.electronic_document.set_customer_document_type(document_type)

    @staticmethod
    def print(token:str, tc:str, cpte:str,account:str):
        """
        Retorna un diccionario con el detalle de un comprobante
        OPTIMIZAR
        """
        cptes_contables = ['CC']
        result = []
        insumos = []
        datos_empresa = []
        medios_pago = []
        cptes_aplicados = []
        retenciones = []
        error = False

        where = f" AND cuenta='{account}'" if account != '' and account != '*' else ''
        
        company = Company(token)

        if error:
            return jsonify({"error": f"ocurrió un error al obtener los datos (empresa) del comprobante {tc} {cpte}"})

        if tc in cptes_contables:

            sql = f"""
            SELECT TOP 1 '' as empresa, '' as detalle,CONVERT(NVARCHAR(10),a.fecha,103) as fecha,a.tc,a.sucursal,a.numero,a.letra,a.sucursal + a.numero + a.letra as idcomprobante,a.detalle as concepto,a.unegocio,b.Descripcion as unegocio_desc, a.idcajas,c.Descripcion as caja,a.mes_operativo,a.[NUMERO ASIENTO] as asiento 
            FROM MV_ASIENTOS a 
            LEFT JOIN V_TA_UnidadNegocio b on a.UNEGOCIO = b.Codigo
            LEFT JOIN V_Ta_Cajas c on a.IdCajas = c.IdCajas
            WHERE a.TC='{tc}' AND a.SUCURSAL + a.NUMERO + a.LETRA = '{cpte}'
            """ 

            result, error = get_customer_response(
            sql, "Cabecera comprobante", True, token)

            if error:
                return jsonify({"error": f"ocurrió un error al obtener la cabecera del comprobante {tc} {cpte}"})
            

            # Detalle asiento
            sql = f"""
            SELECT a.cuenta,b.DESCRIPCION as nombre ,a.idcajas,c.Descripcion as caja,CASE WHEN a.[DEBE-HABER]='D' THEN convert(varchar,convert(decimal(18,2),a.importe)) ELSE '0' END as imported, CASE WHEN a.[DEBE-HABER]='H' THEN convert(varchar,convert(decimal(18,2),a.importe)) ELSE '0' END as importeh,a.[DEBE-HABER] as dh
            FROM MV_ASIENTOS a 
            LEFT JOIN MA_CUENTAS b on a.CUENTA= b.Codigo
            LEFT JOIN V_Ta_Cajas c on a.IdCajas = c.IdCajas
            WHERE a.TC='{tc}' AND a.SUCURSAL + a.NUMERO + a.LETRA = '{cpte}' order by a.SECUENCIA
            """

            insumos, error = get_customer_response(sql, "detalle asiento comprobante", True, token)
            if error:
                return jsonify({"error": f"ocurrió un error al obtener el detalle de asiento del comprobante {tc} {cpte}"})

            for items in result:
                items["detalle"] = {k: insumos for (
                    k, v) in items.items() if k == "detalle"}
                

        else:
            
                
            # Cabecera
            sql = f"""
            SELECT '' as empresa,'' as insumos,'' as electronico,'' as cptes_aplicados,'' as medios_pago,'' as retenciones, cuenta,tc,sucursal,numero,letra,idcomprobante,fecha,fechaestfin as vencimiento,nombre,domicilio,telefono,localidad,codigopostal,
            documentonumero,observaciones,isnull(comentarios,'') as comentarios,
            convert(varchar,convert(decimal(18,2),netonogravado)) as netonogravado,
            convert(varchar,convert(decimal(18,2),netogravado)) as netogravado,
            convert(varchar,convert(decimal(18,2),importe)) as importe,
            convert(varchar,convert(decimal(18,2),importe_s_iva)) as importe_s_iva,
            convert(varchar,convert(decimal(18,2),porcdescuento1)) as porcdescuento1,
            convert(varchar,convert(decimal(18,2),impdescuento1)) as impdescuento1,
            convert(varchar,convert(decimal(18,2),importeiva)) as importeiva,
            convert(varchar,convert(decimal(18,2),importeiva2)) as importeiva2,
            convert(varchar,convert(decimal(18,2),aliciva)) as aliciva,
            convert(varchar,convert(decimal(18,2),aliciva2)) as aliciva2,
            convert(varchar,convert(decimal(18,2),RETIBR_Importe)) as iibb_importe,
            ta_condiva.descripcion as condiva, v_ta_cpra_vta.descripcion as condvta from v_mv_cpte
            LEFT JOIN TA_CONDIVA ON v_mv_Cpte.condicioniva = ta_condiva.codigo LEFT JOIN V_TA_Cpra_Vta ON V_MV_CPTE.idcond_cpra_vta = V_TA_Cpra_Vta.IDCond_Cpra_Vta
            where tc='{tc}' AND IDCOMPROBANTE='{cpte}' {where}
            """

            result, error = get_customer_response(
                sql, "Cabecera comprobante", True, token)

            if error:
                return jsonify({"error": f"ocurrió un error al obtener la cabecera del comprobante {tc} {cpte}"})

            # Insumos
            sql = f"""
            SELECT ltrim(idarticulo) as idarticulo,descripcion,idunidad,
            convert(varchar,convert(decimal(18,2),cantidadud)) as cantidadud,
            convert(varchar,convert(decimal(18,2),cantidad)) as cantidad,
            convert(varchar,convert(decimal(18,2),importe)) as importe,
            convert(varchar,convert(decimal(18,2),importe_s_iva)) as importe_s_iva,
            convert(varchar,convert(decimal(18,2),isnull(aliciva,21))) as aliciva,
            convert(varchar,convert(decimal(18,2),total)) as total from v_mv_cpteinsumos
            where tc='{tc}' AND IDCOMPROBANTE='{cpte}'
            UNION
            SELECT '' as idarticulo,CONVERT(VARCHAR(MAX),observacion) as descripcion,'' as idunidad,
            '1' as cantidadud,
            '1' as cantidad,
            convert(varchar,convert(decimal(18,2),importe)) as importe,
            convert(varchar,convert(decimal(18,2),importe_s_iva)) as importe_s_iva,
            '' as aliciva,
            convert(varchar,convert(decimal(18,2),importe)) as total from V_MV_CPTE_OBSERV
            where tc='{tc}' AND IDCOMPROBANTE='{cpte}'
            """
            insumos, error = get_customer_response(
                sql, "insumos comprobante", True, token)
            if error:
                return jsonify({"error": f"ocurrió un error al obtener los insumos del comprobante {tc} {cpte}"})

            for items in result:
                items["insumos"] = {k: insumos for (
                    k, v) in items.items() if k == "insumos"}

            # Datos electronicos
            sql = f"""
            SELECT tc,idcomprobante,cae,vtocae,codigobarracae FROM V_MV_CPTE_ELECTRONICOS where tc='{tc}' AND IDCOMPROBANTE='{cpte}'
            """
            electronico, error = get_customer_response(
                sql, "datos electornicos comprobante", True, token)
            if error:
                return jsonify({"error": f"ocurrió un error al obtener los datos electronicos del comprobante {tc} {cpte}"})

            for items in result:
                items["electronico"] = {k: electronico for (
                    k, v) in items.items() if k == "electronico"}
                
            #Si es cobranza veo los comprobantes aplicados
            if tc == "CB" or tc=="CBCT" or tc=="CBFP":
                sql = f"""
                
                SELECT convert(varchar,convert(decimal(8,2),sum(c.saldo))) as saldo, convert(varchar,convert(decimal(8,2),sum(b.importe))) as importe,CONVERT(NVARCHAR(10),b.fecha,103) as fecha, a.cuenta,a.TCO_ORIGEN as tc, a.IDCOMPROBANTE_ORIGEN as idcomprobante, convert(varchar,convert(decimal(8,2),SUM(a.IMPORTE))) AS aplicado 
                From MV_APLICACION a
                LEFT JOIN V_MV_CPTE b on a.TCO_ORIGEN=b.TC and a.IDComprobante_Origen=b.IDCOMPROBANTE
                LEFT JOIN VE_SALDOAPLICACION c on a.TCO_ORIGEN = c.TCO_ORIGEN and a.SUCURSAL_ORIGEN=c.SUCURSAL_ORIGEN and a.NUMERO_ORIGEN=c.NUMERO_ORIGEN and a.LETRA_ORIGEN=c.LETRA_ORIGEN
                WHERE a.TC_PRINT = '{tc}' AND a.IDCOMPROBANTE_PRINT = '{cpte}' 
                GROUP BY a.CUENTA, a.TCO_ORIGEN, a.IDCOMPROBANTE_ORIGEN, a.TC_PRINT, a.IDCOMPROBANTE_PRINT,a.SUCURSAL_ORIGEN, a.NUMERO_ORIGEN, a.LETRA_ORIGEN ,b.FECHA
                ORDER BY a.TCO_ORIGEN, a.IDCOMPROBANTE_ORIGEN
                """
                cptes_aplicados, error = get_customer_response(
                    sql, "datos comprobantes aplicados", True, token)
                if error:
                    return jsonify({"error": f"ocurrió un error al obtener los datos de comprobantes aplicados de {tc} {cpte}"})

                for items in result:
                    items["cptes_aplicados"] = {k: cptes_aplicados for (
                        k, v) in items.items() if k == "cptes_aplicados"}
                    
                #Tambien miro los medios de pago
                sql = f"""
                SELECT convert(varchar,convert(decimal(8,2),a.importe)) as importe,b.descripcion,a.nrocomprobantebancario as cheque,CONVERT(NVARCHAR(10),a.vencimiento,103) as vencimiento,a.idbanco,c.descripcion as banco 
                FROM MV_ASIENTOS a 
                LEFT JOIN MA_CUENTAS b on a.CUENTA = b.CODIGO
                LEFT join V_TA_Bancos c on a.IdBanco = c.IdBanco
                WHERE a.TC = '{tc}' AND a.SUCURSAL + a.NUMERO + a.LETRA = '{cpte}'
                and b.MedioDePago<>'' and b.MedioDePago<>'RI' and b.MedioDePago<>'RO' and b.MedioDePago<>'RB' and b.MedioDePago<>'RG'
                """
                medios_pago, error = get_customer_response(sql, "datos medios de pago", True, token)
                if error:
                    return jsonify({"error": f"ocurrió un error al obtener los datos de medios de pago de {tc} {cpte}"})

                for items in result:
                    items["medios_pago"] = {k: medios_pago for (
                        k, v) in items.items() if k == "medios_pago"}
                    
                #Obtengo retenciones y descuentos

                sql = f"""
                SELECT isnull(b.MedioDePago,'DO') as mp, convert(varchar,convert(decimal(8,2),SUM(a.importe))) as importe from MV_ASIENTOS a
                left join MA_CUENTAS b on a.CUENTA = b.CODIGO
                where a.TC='{tc}' and a.SUCURSAL+a.NUMERO+a.LETRA='{cpte}'
                and (b.MedioDePago is null or b.MedioDePago = '' or b.MedioDePago = 'RI' or b.MedioDePago = 'RO' or b.MedioDePago = 'RB'  or b.MedioDePago = 'RG') and (b.TipoVista = '' or b.TipoVista is null)
                GROUP BY b.MedioDePago
                """

                retenciones, error = get_customer_response(sql, "datos retenciones y descuentos", True, token)
                if error:
                    return jsonify({"error": f"ocurrió un error al obtener los datos de retenciones y descuentos de {tc} {cpte}"})

                for items in result:
                    items["retenciones"] = {k: retenciones for (
                        k, v) in items.items() if k == "retenciones"}

        if result:
            for items in result:
                items["empresa"] = {
                    "empresa":[{
                        'calle':company.address,
                        'localidad': company.location,
                        'provincia': company.state,
                        'telefono': company.phone,
                        'cuit': company.cuit,
                        'nro_iibb': company.iibb,
                        'inicio_actividades': company.activity_start,
                        'nombre': company.name
                    }]
                }

            if company.enable_efc and (tc == 'FC' or tc == 'NC' or tc=='ND'):
                result[0]['print_format'] = company.e_format_print
            else:
                result[0]['print_format'] = company.format_print_default

            if company.include_print_logo[tc]:
                result[0]['logo'] = company.logo
            else:
                result[0]['logo'] = ""
        # print(result)
        return result
class ElectronicDocument:

    production = False
    afip_code = 0
    cert = None
    key = None
    TOKEN = None
    document = None
    cae = None
    cae_expiration = None
    cuit = ""
    branch = None
    afip = None
    number = ""
    letter = ""

    result = None

    customer_document_type = None
    customer_document_number = None

    def __init__(self, document: Document,token:str, production: bool = False) -> None:
        self.TOKEN = token
        self.document = document
        self.production = production

        self.customer_document_number = self.document.customer.cuit

        self.__validate_company_data()
        self.__get_document_type()
        self.__get_afip_code()

        self.afip = Afip(self.cert, self.key, self.cuit, self.production)
    
    def set_customer_document_type(self, document_type):
        self.customer_document_type = document_type
    
    def set_customer_document_number(self, document_number):
        self.customer_document_number = document_number

    def __validate_company_data(self):
        """Valida datos para factura electroncia de la empresa"""

        #Valido el certificado
        cert = self.document.company.e_crt

        if not cert:
            self.cert = None
        else:
            self.cert = None if not os.path.isfile(cert) else cert
            
        if self.cert is None:
            raise Exception("El certificado de AFIP no existe o no está configurado correctamente.")


        #Valido la key
        key = self.document.company.e_key

        if not key:
            self.key = None
        else:
            self.key = None if not os.path.isfile(key) else key

        if self.key is None:
            raise Exception("La clave privada de AFIP no existe o no está configurada correctamente.")
        

        #Valido el cuit
        self.cuit = self.document.company.e_cuit

        if self.cuit is None or self.cuit == "":
            raise Exception("No está informado el CUIT para el servicio WSFE.")

        #Valido el punto de venta electronico
        data = self.document.company.branchs_default.get(self.document.code)

        branch_def = data.get('branch')
        force = data.get('force')

        #Si tengo un forzado
        if force:
            self.branch = branch_def
        else:
            #Si no forza y tengo una branch default, verifico que hayan informado la branch, si no tomo la default
            self.branch = branch_def if branch_def != '' else self.document.company.e_branch
        
        self.document.branch = self.branch
        

        if self.branch is None or self.branch == "":
            raise Exception("No está informado el punto de venta electrónico.")
        

        #Valido ela condición de venta de la empresa
        self.document.vat_condition = self.document.company.vat_condition

        if self.document.vat_condition is None or self.document.vat_condition == "":
            raise Exception("No está informada la condición de IVA de la empresa.")
        
        
    def generate(self) -> None:
        """Obtiene el CAE de un comprobante electronico"""

        #Si es A, el tipo de documento debe ser CUIT 80
        if self.document.letter == LETTER_A and self.customer_document_type != DOC_CUIT:
            self.customer_document_type = AFIP_DOC_CUIT
        
        #Si es B y el importe es menor al establecido, y el tipo de documento es 99, documento debe ser 0
        if self.document.letter == LETTER_B and self.document.total < AFIP_AMOUNT_MAX_DECLARE_DOCUMENT:
            if self.customer_document_type == AFIP_DOC_DNI:
                self.customer_document_number = "0"
        
        #Si es B y el importe es mayor al establecido, debe informar tipo y número de documento
        if self.document.letter == LETTER_B and self.document.total >= AFIP_AMOUNT_MAX_DECLARE_DOCUMENT and self.customer_document_type == AFIP_DOC_DNI:
            raise Exception(f"Para comprobantes B mayores a $ {AFIP_AMOUNT_MAX_DECLARE_DOCUMENT}, debe indicar el tipo y número de documento del cliente.")

        
        #Si es A tiene que tener informado el CUIT
        if self.document.letter == LETTER_A and (self.customer_document_type != AFIP_DOC_CUIT or len(self.customer_document_number) <11):
            raise Exception("El cuit del cliente no es válido")
        
        response = self.afip.generate_electronic_invoice(
            tipo_cbte=self.afip_code,
            registros=[{
                "documento": self.customer_document_number,
                "tipo_documento": self.customer_document_type,
                "nombre": self.document.customer.name,
                "domicilio": self.document.customer.address,
                "pto_vta": self.branch,
                "items": self.document.items,
                "letra": self.document.letter
            }]
        )

        if response:
            if response["resultado"] == "A":
                self.cae = response["cae"]
                self.cae_expiration = response["fch_venc_cae"]
                self.document.number = response["cbte_nro"]
                self.number = response["cbt_desde"]
                self.letter = response["cbt_desde"]

                self.result = response
            else:
                if len(response["warnings"]) > 0:
                    raise Exception(response["warnings"][0])
    
   
    def get_next_number(self) -> int:
        """Obtiene el próximo número del comprobante de AFIP"""
        return self.afip.get_next_number(self.afip_code,self.branch)
    
    def __get_document_type(self) -> None:

        if self.document.customer.document_type == DOC_CUIT:
            self.customer_document_type = AFIP_DOC_CUIT
        elif self.document.customer.document_type == DOC_DNI:
            self.customer_document_type = AFIP_DOC_DNI

    def __get_afip_code(self):
        """Retorna el código de afip de acuerdo a la sucursal y la letra"""
        if self.document.code + self.document.letter == 'FCA':
            self.afip_code = 1
        elif self.document.code + self.document.letter == 'NDA':
            self.afip_code = 2
        elif self.document.code + self.document.letter == 'NCA':
            self.afip_code = 3
        elif self.document.code + self.document.letter == 'FCB':
            self.afip_code = 6
        elif self.document.code + self.document.letter == 'NDB':
            self.afip_code = 7
        elif self.document.code + self.document.letter == 'NCB':
            self.afip_code = 8
        elif self.document.code + self.document.letter == 'FCC':
            self.afip_code = 11
        elif self.document.code + self.document.letter == 'NDC':
            self.afip_code = 12
        elif self.document.code + self.document.letter == 'NCC':
            self.afip_code = 13

