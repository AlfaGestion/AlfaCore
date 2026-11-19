from routes.v2.master import MasterView
from functions.general_customer import get_user_checked_authorized_menu, get_all_authorized_menu, get_customer_response, exec_customer_sql
from functions.responses import set_response
from flask_classful import route
from flask import request

class ViewMenu(MasterView):
    def index(self):
        try:
            menu = get_all_authorized_menu(token=self.token_global)
            return set_response(menu, 200)
        except Exception as e:
            self.log(e)
            return set_response(None, 404, f"{e}")
        
    def get(self, seller_name:str):
        try:
            menu = get_user_checked_authorized_menu(seller_name=seller_name, token=self.token_global)
            return set_response(menu, 200)
        except Exception as e:
            self.log(e)
            return set_response(None, 404, f"{e}")
    
    @route('/users', methods=['GET'])
    def get_groups(self):
        query = f"""
            SELECT
            NOMBRE as usuario, GRUPO, IDCAJA as id_caja, UNegocio as unidad_negocio, IDDEPOSITO as id_deposito,
            email_usuario, email_password, email_nombre, email_de, email_server, eMail_ServerOtro as email_otro_server
            FROM TA_USUARIOS
            WHERE SISTEMA = 'CN000PR'
            ORDER BY NOMBRE
        """

        try:
            result, error = get_customer_response(query, f" al obtener los usuarios", True, self.token_global)
            return set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])
        except Exception as f:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + query)

        return set_response(None, 404, f"Ocurrió un error al obtener los usuarios.")
    
    @route('/unidades-negocio', methods=['GET'])
    def get_unidades_negocio(self):
        query = f"""
            SELECT
            Descripcion as description, Codigo as code
            FROM V_TA_UnidadNegocio
            ORDER BY Codigo
        """

        try:
            result, error = get_customer_response(query, f" al obtener las unidades de negocio", True, self.token_global)
            return set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])
        except Exception as f:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + query)

        return set_response(None, 404, f"Ocurrió un error al obtener las unidades de negocio.")
        
    @route('/cajas/<string:unidad_negocio>', methods=['GET'])
    def get_cajas(self, unidad_negocio:str):
        
        query = f"""
            SELECT
            Descripcion as description, IdCajas as code, UNIDADNEGOCIO as unidad_negocio
            FROM V_Ta_Cajas
            WHERE UNIDADNEGOCIO = '{unidad_negocio}'
            ORDER BY IdCajas
        """

        try:
            result, error = get_customer_response(query, f" al obtener las cajas", True, self.token_global)
            return set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])
        except Exception as f:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + query)

        return set_response(None, 404, f"Ocurrió un error al obtener las cajas.")
    
    @route('/depositos/<string:unidad_negocio>', methods=['GET'])
    def get_depositos(self, unidad_negocio:str):

        query = f"""
            SELECT Descripcion as description, IdDeposito as code
            FROM V_TA_DEPOSITO
            WHERE IdDeposito = '{unidad_negocio}'
            ORDER BY IdDeposito
        """

        try:
            result, error = get_customer_response(query, f" al obtener los depósitos", True, self.token_global)
            return set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])
        except Exception as f:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + query)

        return set_response(None, 404, f"Ocurrió un error al obtener los depósitos.")
    
    @route('/cobranzas', methods=['GET'])
    def get_cobranzas(self):

        query = f"""
            SELECT Descripcion as description, CODIGO as code, MedioDePago as type
            FROM MA_CUENTAS
            WHERE MedioDePago <> '' AND NOT MedioDePago is NULL and BLOQUEO = 0
            ORDER by CODIGO
        """

        try:
            result, error = get_customer_response(query, f" al obtener las cobranzas", True, self.token_global)
            return set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])
        except Exception as f:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + query)

        return set_response(None, 404, f"Ocurrió un error al obtener las cobranzas.")

    @route('/cobranzas/<string:user>', methods=['GET'])
    def get_user_checked_cobranzas(self, user:str):

        query = f"""
            SELECT u.Cuenta as code
            FROM MA_CUENTAS_USUARIOS u
            INNER JOIN MA_CUENTAS c ON u.Cuenta = c.CODIGO
            WHERE
            u.GrupoUser = '{user}' AND
            c.MedioDePago <> '' AND
            c.MedioDePago IS NOT NULL AND
            c.BLOQUEO = 0
            ORDER BY c.CODIGO
        """

        try:
            result, error = get_customer_response(query, f" al obtener las cobranzas del usuario", True, self.token_global)
            return set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])
        except Exception as f:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + query)

        return set_response(None, 404, f"Ocurrió un error al obtener las cobranzas del usuario.")

    @route('/atrr-op/<string:user>', methods=['GET'])
    def get_user_attr_op(self, user:str):

        query = f"""
			SELECT
                V_Recargos, V_CantidadNegativa, V_Descuentos, V_ModificaPrecio,
                ModificaListaPrecios, ModificaCostos, V_Cotizacion, V_ConsultaHistorica,
                V_AbrirCajon, CondicionVenta, V_ModificaArtLuegoDeCargado, ProvBlockCondCpra,
                ProvSoloCondCpra, AstAnular, AstModificar, ChkSolicitudPnlSinOT,
                MuestraImporteOT, SUCOT, LETOT, IDVENDEDOR, CLASEPUSA
            FROM TA_USUARIOS
            where SISTEMA = 'CN000PR' and NOMBRE = '{user}'
        """

        try:
            result, error = get_customer_response(query, f" al obtener los atributos operativos del usuario", True, self.token_global)
            return set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])
        except Exception as f:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + query)

        return set_response(None, 404, f"Ocurrió un error al obtener los atributos operativos del usuario.")
    
    @route('/others-attr/<string:user>', methods=['GET'])
    def get_user_others_attr(self, user:str):

        # Sé que el user.upper() no es necesario pero es puro TOC 🤠.
        query = f"""
            SELECT
                u.Administrador,
                u.AdminCentral,
                u.VerProforma,
                u.VerChequesEnAgenda,
                u.CierreCiego,
                u.AnulaBonos,
                u.HelpCenter,
                u.PermiteBloqueo,
                u.StockOcultaImportes,
                CASE WHEN c.ESMOZO_ = 'SI' THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS ESMOZO_,
                CASE WHEN c.PERMITECLASIFICAR_ = 'SI' THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS PERMITECLASIFICAR_,
                CASE WHEN c.VERTODASCAJAS_ = 'SI' THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS VERTODASCAJAS_,
                CASE WHEN c.ADMINTAREAS_ = 'SI' THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS ADMINTAREAS_,
                CASE WHEN c.ELIMINA_EQUIPOS_ = 'SI' THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS ELIMINA_EQUIPOS_
            FROM TA_USUARIOS u
            LEFT JOIN (
                SELECT
                    MAX(CASE WHEN CLAVE = 'ESMOZO_{user.upper()}' THEN VALOR END) AS ESMOZO_,
                    MAX(CASE WHEN CLAVE = 'PERMITECLASIFICAR_{user.upper()}' THEN VALOR END) AS PERMITECLASIFICAR_,
                    MAX(CASE WHEN CLAVE = 'VERTODASCAJAS_{user.upper()}' THEN VALOR END) AS VERTODASCAJAS_,
                    MAX(CASE WHEN CLAVE = 'ADMINTAREAS_{user.upper()}' THEN VALOR END) AS ADMINTAREAS_,
                    MAX(CASE WHEN CLAVE = 'ELIMINA_EQUIPOS_{user.upper()}' THEN VALOR END) AS ELIMINA_EQUIPOS_
                FROM TA_CONFIGURACION
                WHERE CLAVE IN (
                    'ESMOZO_{user.upper()}',
                    'PERMITECLASIFICAR_{user.upper()}',
                    'VERTODASCAJAS_{user.upper()}',
                    'ADMINTAREAS_{user.upper()}',
                    'ELIMINA_EQUIPOS_{user.upper()}'
                )
            ) c ON 1=1
            WHERE u.SISTEMA = 'CN000PR' AND u.NOMBRE = '{user}'
        """

        try:
            result, error = get_customer_response(query, f" al obtener los otros atributos del usuario", True, self.token_global)
            return set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])
        except Exception as f:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + query)

        return set_response(None, 404, f"Ocurrió un error al obtener los otros atributos del usuario.")
    
    @route('/user', methods=['POST'])
    def post_user_auth(self):
        data = request.get_json()

        # Usuario
        user = data.get('user', '')

        # Códigos de Cobranzas
        codes = data.get('codes', '')
        
        # Datos de Configuración Cuenta E-Mail
        email_usuario = data.get('email_usuario', '')
        email_password = data.get('email_password', '')
        email_nombre = data.get('email_nombre', '')
        
        email_de = data.get('email_de', '')
        email_server = data.get('email_server', '')
        email_otro_server = data.get('email_otro_server', '')
        
        # Caja, Unidad de Negocio y Depósito
        id_caja = data.get('id_caja', '')
        unidad_negocio = data.get('unidad_negocio', '')
        id_deposito = data.get('id_deposito', '')
        
        # Opciones del Menú
        menu = data.get('menu', '')

        # Atributos Operativos.
        V_Recargos = data.get('V_Recargos', '')
        V_CantidadNegativa = data.get('V_CantidadNegativa', '')
        V_Descuentos = data.get('V_Descuentos', '')
        V_ModificaPrecio = data.get('V_ModificaPrecio', '')
        ModificaListaPrecios = data.get('ModificaListaPrecios', '')
        ModificaCostos = data.get('ModificaCostos', '')
        V_Cotizacion = data.get('V_Cotizacion', '')
        V_ConsultaHistorica = data.get('V_ConsultaHistorica', '')
        V_AbrirCajon = data.get('V_AbrirCajon', '')
        CondicionVenta = data.get('CondicionVenta', '')
        V_ModificaArtLuegoDeCargado = data.get('V_ModificaArtLuegoDeCargado', '')

        CLASEPUSA = data.get('CLASEPUSA', '')

        ProvBlockCondCpra = data.get('ProvBlockCondCpra', '')
        ProvSoloCondCpra = data.get('ProvSoloCondCpra', '')

        AstAnular = data.get('AstAnular', '')
        AstModificar = data.get('AstModificar', '')

        ChkSolicitudPnlSinOT = data.get('ChkSolicitudPnlSinOT', '')
        MuestraImporteOT = data.get('MuestraImporteOT', '')

        IDVENDEDOR = data.get('IDVENDEDOR', '')
        LETOT = data.get('LETOT', '')
        SUCOT = data.get('SUCOT', '')
        
        # Otros Atributos.
        Administrador = data.get('Administrador', '')
        AdminCentral = data.get('AdminCentral', '')
        VerProforma = data.get('VerProforma', '')
        VerChequesEnAgenda = data.get('VerChequesEnAgenda', '')
        CierreCiego = data.get('CierreCiego', '')
        AnulaBonos = data.get('AnulaBonos', '')
        HelpCenter = data.get('HelpCenter', '')
        PermiteBloqueo = data.get('PermiteBloqueo', '')
        StockOcultaImportes = data.get('StockOcultaImportes', '')

        ESMOZO_ = data.get('ESMOZO_', '')
        PERMITECLASIFICAR_ = data.get('PERMITECLASIFICAR_', '')
        VERTODASCAJAS_ = data.get('VERTODASCAJAS_', '')
        ADMINTAREAS_ = data.get('ADMINTAREAS_', '')
        ELIMINA_EQUIPOS_ = data.get('ELIMINA_EQUIPOS_', '')

        # Validación de usuario.
        if not user:
            return set_response(None, 400, "No se informó el usuario.")
        
        query_cuenta_email = f"""
            UPDATE TA_USUARIOS
            SET
            email_usuario = '{email_usuario}',
            email_password = '{email_password}',
            email_nombre = '{email_nombre}',
            email_de = '{email_de}',
            email_server = '{email_server}',
            eMail_ServerOtro = '{email_otro_server}'
            WHERE NOMBRE = '{user}'
        """

        query_caja_unidad_deposito = f"""
            UPDATE TA_USUARIOS
            SET
            IDCAJA = '{id_caja}',
            UNegocio = '{unidad_negocio}',
            IDDEPOSITO = '{id_deposito}'
            WHERE NOMBRE = '{user}'
        """
        
        query_atributos_operativos = f"""
            UPDATE TA_USUARIOS
            SET
            V_Recargos = {V_Recargos},
            V_CantidadNegativa = {V_CantidadNegativa},
            V_Descuentos = {V_Descuentos},
            V_ModificaPrecio = {V_ModificaPrecio},
            ModificaListaPrecios = {ModificaListaPrecios},
            ModificaCostos = {ModificaCostos},
            V_Cotizacion = {V_Cotizacion},
            V_ConsultaHistorica = {V_ConsultaHistorica},
            V_AbrirCajon = {V_AbrirCajon},
            CondicionVenta = {CondicionVenta},
            V_ModificaArtLuegoDeCargado = {V_ModificaArtLuegoDeCargado},

            CLASEPUSA = '{CLASEPUSA}',

            ProvBlockCondCpra = {ProvBlockCondCpra},
            ProvSoloCondCpra = {ProvSoloCondCpra},

            AstAnular = {AstAnular},
            AstModificar = {AstModificar},

            ChkSolicitudPnlSinOT = {ChkSolicitudPnlSinOT},
            MuestraImporteOT = {MuestraImporteOT},

            IDVENDEDOR = '{IDVENDEDOR}',
            LETOT = '{LETOT}',
            SUCOT = '{SUCOT}'

            WHERE NOMBRE = '{user}'
        """
        
        query_otros_atributos_usuarios = f"""
            UPDATE TA_USUARIOS
            SET
            Administrador = {Administrador},
            AdminCentral = {AdminCentral},
            VerProforma = {VerProforma},
            VerChequesEnAgenda = {VerChequesEnAgenda},
            CierreCiego = {CierreCiego},
            AnulaBonos = {AnulaBonos},
            HelpCenter = {HelpCenter},
            PermiteBloqueo = {PermiteBloqueo},
            StockOcultaImportes = {StockOcultaImportes}
            WHERE NOMBRE = '{user}'
        """

        query_otros_atributos_config = f"""
        UPDATE TA_CONFIGURACION
        SET VALOR = CASE CLAVE
            WHEN 'ESMOZO_{user.upper()}' THEN '{ESMOZO_}'
            WHEN 'PERMITECLASIFICAR_{user.upper()}' THEN '{PERMITECLASIFICAR_}'
            WHEN 'VERTODASCAJAS_{user.upper()}' THEN '{VERTODASCAJAS_}'
            WHEN 'ADMINTAREAS_{user.upper()}' THEN '{ADMINTAREAS_}'
            WHEN 'ELIMINA_EQUIPOS_{user.upper()}' THEN '{ELIMINA_EQUIPOS_}'
        END
        WHERE CLAVE IN (
            'ESMOZO_{user.upper()}',
            'PERMITECLASIFICAR_{user.upper()}',
            'VERTODASCAJAS_{user.upper()}',
            'ADMINTAREAS_{user.upper()}',
            'ELIMINA_EQUIPOS_{user.upper()}'
        )
        """
        
        query_delete_cobranzas = f"DELETE FROM MA_CUENTAS_USUARIOS WHERE GrupoUser = '{user}'"
        query_delete_menu = f"DELETE FROM TA_TAREAS WHERE USUARIO = '{user}' AND SISTEMA = 'CN000PR'"

        # print("query_atributos_operativos" + query_atributos_operativos)
        # print("query_otros_atributos" + query_otros_atributos_usuarios)
        # print("query_otros_atributos_2" + query_otros_atributos_config)
        # print("query_cuenta_email" + query_cuenta_email)
        # print("query_caja_unidad_deposito" + query_caja_unidad_deposito)

        # return set_response(None, 200, "")
    
        try:
            # BEGIN TRANSACTION
            exec_customer_sql("BEGIN TRANSACTION", " al iniciar la transacción", self.token_global)

            # Delete Cobranzas
            result_delete_cobranzas, error_delete_cobranzas = exec_customer_sql(query_delete_cobranzas, " al eliminar las cobranzas previas", self.token_global)
            if error_delete_cobranzas:
                raise Exception(f"Error en DELETE Cobranzas: \n{result_delete_cobranzas}")

            codes = list(set(codes))

            # Delete Menu
            result_delete_menu, error_delete_menu = exec_customer_sql(query_delete_menu, " al eliminar el menu previo", self.token_global)
            if error_delete_menu:
                raise Exception(f"Error en DELETE Menú: \n{result_delete_menu}")
            
            menu = list(set(menu))
                
            # Insert Cobranzas.
            for code in codes:
                query_insert_cobranzas = f"""
                INSERT INTO MA_CUENTAS_USUARIOS (Cuenta, GrupoUser) VALUES ('{code}', '{user}')
                """
                result_insert_cobranzas, error_insert_cobranzas = exec_customer_sql(query_insert_cobranzas, " al insertar las cobranzas", self.token_global)
                if error_insert_cobranzas:
                    raise Exception(f"Error en INSERT Cobranzas: \n{result_insert_cobranzas}")
                
            # Insert Opciones del Menú.
            for clave in menu:
                query_insert_menu = f"""
                INSERT INTO TA_TAREAS (USUARIO,SISTEMA,TAREA) VALUES ('{user}','CN000PR','{clave}')
                """
                result_insert_menu, error_insert_menu = exec_customer_sql(query_insert_menu, " al insertar el menú", self.token_global)
                if error_insert_menu:
                    raise Exception(f"Error en INSERT: \n{result_insert_menu}")

            # Update Cuenta E-mail.
            result_cuenta_email, error_cuenta_email = exec_customer_sql(query_cuenta_email, " al grabar la Cuenta E-Mail del usuario", self.token_global)

            if error_cuenta_email:
                raise Exception(f"Error en UPDATE Cuenta E-Mail: \n{result_cuenta_email}")
            
            # Update Caja, Unidad de Negocio y Depósito.
            result_caja_unidad_deposito, error_caja_unidad_deposito = exec_customer_sql(query_caja_unidad_deposito, " al grabar la caja, unidad de negocio y depósito del usuario", self.token_global)

            if error_caja_unidad_deposito:
                raise Exception(f"Error en UPDATE Caja, UNegocio y Depósito: \n{result_caja_unidad_deposito}")
            
            # Update Atributos Operativos.
            result_atributos_operativos, error_atributos_operativos = exec_customer_sql(query_atributos_operativos, " al grabar los atributos operativos del usuario", self.token_global)

            if error_atributos_operativos:
                raise Exception(f"Error en UPDATE Atributos operativos: \n{result_atributos_operativos}")
            
            # Update Otros Atributos en TA_USUARIOS.
            result_otros_atributos_usuarios, error_otros_atributos_usuarios = exec_customer_sql(query_otros_atributos_usuarios, " al grabar los otros atributos del usuario en TA_USUARIOS", self.token_global)

            if error_otros_atributos_usuarios:
                raise Exception(f"Error en UPDATE Otros atributos en TA_USUARIOS: \n{result_otros_atributos_usuarios}")
            
            # Update Otros Atributos en TA_CONFIGURACION.
            result_otros_atributos_config, error_otros_atributos_config = exec_customer_sql(query_otros_atributos_config, " al grabar los otros atributos del usuario en TA_CONFIGURACION", self.token_global)

            if error_otros_atributos_config:
                raise Exception(f"Error en UPDATE Otros atributos en TA_CONFIGURACION: \n{result_otros_atributos_config}")


            # COMMIT TRANSACTION
            exec_customer_sql("COMMIT", " al confirmar la transacción", self.token_global)

            return set_response([], message="Autorización del usuario grabada con éxito.")

        except Exception as e:
            exec_customer_sql("ROLLBACK", " al revertir la transacción", self.token_global)
            self.log(f"post_user_auth: {str(e)}", 'ERROR')
            
            return set_response(f"Ocurrió un error inesperado: {str(e)}", 500)
    