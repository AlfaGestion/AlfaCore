from flask import jsonify, abort, Response

from configs.customer_connection import get_conn
from configs.connection_alfa import get_conn_alfa
from configs.connection_sharedb import get_conn_sharedb
from configs.connection_transport import get_conn_transport
from functions.Log import Log
from functions.auth import register_session
from functions.jwt import write_token
from functions.session import get_info_session, set_db
from configs.query import config_query

def exec_update_db(sql: str, token: str):
    result = []

    connection = get_conn(token)

    try:
        if connection == '':
            result.append({
                'error': 'true',
                'message': 'Error en la conexión con el servidor'
            })

            return result, True

        connection.cursor().execute(sql)

        # connection.commit()
        # connection.close()
        return result, False
    except Exception as e:
        # print(f"Ocurrió un error al obtener {name_error}: ", e)
        result.append({
            'error': 'true',
            'message': e
        })
        return result, True


def exec_customer_sql(sql: str, name_error: str, token: str = '', return_result=False, is_transport_query: bool = False, is_share_query: bool = False):
    result = []
    #print(sql)
    if is_share_query:
        connection = get_conn_sharedb()
    elif is_transport_query:
        connection = get_conn_transport()
    else:
        connection = get_conn(token)

    try:

        if connection == '':
            result.append({
                'error': 'true',
                'message': 'Error en la conexión con el servidor'
            })

            return result, True

        cursor = connection.cursor().execute(sql)

        if return_result:
            result = cursor.fetchall()
        connection.commit()
        connection.close()
        return result, False
    except Exception as e:
        # print(f"Ocurrió un error al obtener {name_error}: ", e)
        result.append({
            'error': 'true',
            'message': e
        })
        print(e)
        return result, True


def get_customer_response(sql: str, name_error: str, return_list: bool = False, token: str = '', is_alfa_query: bool = False, code_account: str = '', is_share_query: bool = False):
    """
    Retorna un diccionario con los elementos de la consulta
    """

    result = []
    error = False
    error_message = ''
    try:
        if is_share_query:
            connection = get_conn_sharedb()
        elif is_alfa_query:
            connection = get_conn_transport()
        else:
            connection = get_conn(token)

        if connection == '':
            result.append({
                'error': 'true',
                'message': 'Error en la conexión con el servidor'
            })
            error_message = 'No se pudo obtener la conexión con el servidor'
            error = True

        cursor = connection.cursor().execute(sql)
        columns = [column[0] for column in cursor.description]
        for row in cursor.fetchall():
            result.append(dict(zip(columns, row)))

    except Exception as e:
        error = True
        result.append({
            'error': 'true',
            'message': f"Ocurrió un error al obtener {name_error}: {e}"
        })

        error_message = f'Error: {e}\nSQL: {sql}'

        print(f"Ocurrió un error al obtener {name_error}: ", e)
    finally:

        if error:
            result = []
            result.append({
                "error": True,
                "message": name_error,
                "data": None,
                "status_code": 500
            })

            Log.create(error_message, code_account, token=token)
            abort(jsonify(result[0]), 500)

            # if return_list:
            #     return result, True
            # response = jsonify(
            #     {"error": f"Ocurrió un error al obtener {name_error}"})
            # response.status_code = 500
        else:
            if return_list:
                return result, False
            response = jsonify({"data": result})

        return response


def is_valid_account_seller(data):
    """
    Valida si una cuenta de vendedor y su contraseña son correctas
    """
    username = data["username"]
    alfaCustomerId = data["alfaCustomerId"]
    database_id = data["databaseId"]

    data["account"] = alfaCustomerId
    # data["superadmin"] = 0
    # data["admin"] = 0
    # data["bloqueado"] = 0

    # print("DATA SELLER : ", data)
    token = write_token(data)
    str_token = token.decode('UTF-8')

    if register_session(str_token, data):
        # data["token"] = str_token
        if set_db(alfaCustomerId, database_id, str_token):

            database_information = get_info_session(str_token)

            # data["dbname"] = database_information[0]["dbname"]
            # data["nombre"] = database_information[0]["nombre"]
            # data["company_name"] = database_information[0]["company_name"]
            # data["id_driver"] = ""
            # data["account_type"] = ""

            connection = get_conn(str_token)

            sql = f"""
                SELECT ltrim(a.nombre) as nombre,ltrim(a.password) as claveweb,ltrim(a.idvendedor) as idvendedor,
                ltrim(isnull(b.idlista,'')) as idlista, isnull(b.e_mail,'') as email,isnull(a.idcaja,'') as idcaja,
                ltrim(isnull(a.sucot,'')) as sucursal_defecto
                FROM TA_USUARIOS a
                LEFT JOIN V_TA_VENDEDORES b on a.idvendedor = b.idvendedor
                WHERE a.nombre='{username}'
                """
            # and a.idvendedor<>''
            try:
                cursor = connection.cursor().execute(sql)

                for row in cursor.fetchall():
                    if decrypt_password(row.claveweb) == data["password"]:

                        data = complete_data_login(data=data, username=username, account=alfaCustomerId, superadmin=0, admin=0, bloqueado=0,
                                                   token=str_token, dbname=database_information[0]["dbname"], nombre=database_information[0]["nombre"],
                                                   company_name=database_information[0]["company_name"], account_type="", id_seller=row.idvendedor, idlista=row.idlista, hide_prices=0, email=row.email,
                                                   idcaja=row.idcaja, zona="",sucursal_defecto=row.sucursal_defecto)
                        # data["id_seller"] = row.idvendedor
                        # data["idlista"] = row.idlista
                        # data["hide_prices"] = 0
                        # data["email"] = row.email
                        # data["idcaja"] = row.idcaja
                        # data["zona"] = ""

                        return False, ''
                return True, 'Clave incorrecta '
            except Exception as e:
                print(
                    f"Ocurrió un error al obtener la clave de el usuario {username}: ", e)
                return True, f"Ocurrió un error al obtener la clave de el usuario {username}: "
    print("Ocurrió un error al intentar registrar la sesión: ", data)
    return True, "Ocurrió un error al intentar registrar la sesión "


def is_valid_account_driver(data):
    """
    Valida si una cuenta de chofer y su contraseña son correctas
    """
    username = data["username"]
    alfaCustomerId = data["alfaCustomerId"]
    database_id = data["databaseId"]

    data["account"] = alfaCustomerId
    # data["superadmin"] = 0
    # data["admin"] = 0
    # data["bloqueado"] = 0
    # data["account_type"] = ""

    token = write_token(data)
    str_token = token.decode('UTF-8')

    if register_session(str_token, data):
        # data["token"] = str_token
        if set_db(alfaCustomerId, database_id, str_token):

            database_information = get_info_session(str_token)

            # data["dbname"] = database_information[0]["dbname"]
            # data["nombre"] = database_information[0]["nombre"]
            # data["company_name"] = database_information[0]["company_name"]

            connection = get_conn(str_token)

            sql = f"EXEC sp_web_getChoferes '{username}'"

            try:
                cursor = connection.cursor().execute(sql)

                for row in cursor.fetchall():
                    if row.clave == data["password"] and row.clave != '':

                        data = complete_data_login(data=data, username=username, account=alfaCustomerId, superadmin=0, admin=0, bloqueado=0,
                                                   token=str_token, dbname=database_information[0]["dbname"], nombre=row.nombre + ' ' + row.apellido,
                                                   company_name=database_information[0]["company_name"], account_type="", id_seller="", idlista="", hide_prices=0, email="",
                                                   idcaja="", zona="", id_driver=row.codigo)

                        # data["id_seller"] = ""
                        # data["idlista"] = ""
                        # data["hide_prices"] = 0
                        # data["nombre"] = row.nombre + ' ' + row.apellido
                        # data["id_driver"] = row.codigo
                        # data["email"] = ""
                        # data["idcaja"] = ""
                        # data["zona"] = ""

                        # print(data)
                        return False
                return True
            except Exception as e:
                print(
                    f"Ocurrió un error al obtener la clave de el chofer {username}: ", e)
                return True
    return True


def is_valid_autologin_contact_customer(data: dict):
    # Obtengo la información de un contacto a partir de un id.
    """
    Retorna un token para un autologin de contacto
    """
    contact_id = data["username"]
    alfaCustomerId = decode_id_account(data["alfaCustomerId"])
    database_id = data["databaseId"]

    data["username"] = alfaCustomerId
    data["password"] = "1"
    data["account"] = alfaCustomerId

    token = write_token(data)
    str_token = token.decode('UTF-8')

    if register_session(str_token, data):
        if set_db(alfaCustomerId, database_id, str_token):

            database_information = get_info_session(str_token)
            connection = get_conn(str_token)


            sql = f"""
            SELECT a.nombre_y_apellido as nombre,a.idcontacto,
            isnull(c.ProveedorLocal,0) as oculta_precios,LTRIM(a.claveweb) as claveweb,c.codigo,
            ISNULL(LTRIM(c.idlista),'') as idlista,ISNULL(a.email,'') as email, ISNULL(a.admin,0) as admin
            FROM MA_CONTACTOS_CUENTAS b
            INNER JOIN MA_CONTACTOS a on b.IdContacto = a.idContacto
            LEFT JOIN vt_clientes c on b.cuenta = c.codigo
            WHERE b.Cuenta = '{alfaCustomerId}' and a.idContacto={contact_id}
            """
            try:
                cursor = connection.cursor().execute(sql)

                for row in cursor.fetchall():

                    data = complete_data_login(data=data, account=alfaCustomerId, superadmin=0, admin=row.admin, bloqueado=0,
                                               token=str_token, dbname=database_information[0]["dbname"], nombre=row.nombre,
                                               company_name=database_information[0]["company_name"], account_type="", id_seller="", idlista=row.idlista, hide_prices=row.oculta_precios, email=row.email,
                                               idcaja="", zona="", auth_menu="", id_driver="", username=row.codigo)
                    return False

                return True
            except Exception as e:
                print(f"Ocurrió un error al obtener el autologin: ", e)
                return True
    print("Ocurrió un error al intentar registrar la sesión: ", data)
    return True


def is_valid_account_customer(data):
    """
    Valida si una cuenta de cliente y su contraseña son correctas
    """
    username = data["username"]
    alfaCustomerId = data["alfaCustomerId"]
    database_id = data["databaseId"]

    data["account"] = alfaCustomerId
    # data["superadmin"] = 0
    # data["account_type"] = ""

    token = write_token(data)
    str_token = token.decode('UTF-8')

    if register_session(str_token, data):
        # data["token"] = str_token
        if set_db(alfaCustomerId, database_id, str_token):

            database_information = get_info_session(str_token)

            # data["dbname"] = database_information[0]["dbname"]
            # data["company_name"] = database_information[0]["company_name"]
            # data["id_seller"] = ""
            # data["auth_menu"] = ""
            # data["id_driver"] = ""

            connection = get_conn(str_token)

            # Se puede loguear con un codigo de cliente o con un email de contacto
            sql = f"""
            SELECT ltrim(isnull(zona,'')) as zona,razon_social as nombre,isnull(dada_de_baja,0) as bloqueado, isnull(proveedorlocal,0) as oculta_precios,ltrim(clave) as claveweb,codigo,ltrim(idlista) as idlista,isnull(mail,'') as email, 1 as admin FROM VT_CLIENTES WHERE
            codigo='{username}' and clave<>''
            UNION
            SELECT ltrim(isnull(b.zona,'')) as zona,a.Nombre_y_Apellido as nombre,isnull(dada_de_baja,0) as bloqueado, isnull(b.proveedorlocal,0) as oculta_precios,ltrim(a.claveweb) as claveweb,b.codigo,ltrim(b.idlista) as idlista,isnull(a.email,'') as email, isnull(a.admin, 0) as admin
            FROM MA_CONTACTOS a LEFT JOIN VT_CLIENTES b ON a.CuentaRel = b.CODIGO
            WHERE a.email='{username}' and a.claveweb<>''
            """

            try:
                cursor = connection.cursor().execute(sql)

                for row in cursor.fetchall():
                    if row.claveweb == data["password"]:

                        data = complete_data_login(data=data, account=alfaCustomerId, superadmin=0, admin=row.admin, bloqueado=row.bloqueado,
                                                   token=str_token, dbname=database_information[0]["dbname"], nombre=row.nombre,
                                                   company_name=database_information[0]["company_name"], account_type="", id_seller="", idlista=row.idlista, hide_prices=row.oculta_precios, email=row.email,
                                                   idcaja="", zona=row.zona, auth_menu="", id_driver="", username=row.codigo)

                        # data["nombre"] = row.nombre
                        # data["idlista"] = row.idlista
                        # data["hide_prices"] = row.oculta_precios
                        # data["email"] = row.email
                        # data["username"] = row.codigo
                        # data["admin"] = row.admin
                        # data["idcaja"] = ""
                        # data["zona"] = row.zona
                        # data["bloqueado"] = row.bloqueado

                        return False
                return True
            except Exception as e:
                print(f"Ocurrió un error al obtener la clave el usuario {username}: ", e)
                return True
    print("Ocurrió un error al intentar registrar la sesión: ", data)
    return True


def is_valid_account_public(data):
    """
    Retorna un token para una cuenta pública
    """
    username = data["username"]
    alfaCustomerId = data["alfaCustomerId"]
    database_id = data["databaseId"]

    data["account"] = alfaCustomerId
    # data["superadmin"] = 0
    # data["admin"] = 0
    # data["account_type"] = ""

    token = write_token(data)
    str_token = token.decode('UTF-8')

    if register_session(str_token, data):
        # data["token"] = str_token
        if set_db(alfaCustomerId, database_id, str_token):

            database_information = get_info_session(str_token)

            # data["dbname"] = database_information[0]["dbname"]
            # data["company_name"] = database_information[0]["company_name"]
            # data["id_seller"] = ""
            # data["auth_menu"] = ""
            # data["id_driver"] = ""
            # data["email"] = ""
            # data["idcaja"] = ""
            # data["bloqueado"] = 0

            connection = get_conn(str_token)

            sql = f"""
                DECLARE @CUENTA NVARCHAR(13)
                SET @CUENTA = (SELECT TOP 1 VALOR FROM TA_CONFIGURACION WHERE CLAVE='CUENTACONSUNMIDORFINAL' AND VALOR<>'')
                SELECT razon_social as nombre,codigo,isnull(proveedorlocal,0) as oculta_precios,ltrim(isnull(idlista,'')) as idlista FROM VT_CLIENTES WHERE codigo=@CUENTA
            """
            try:
                cursor = connection.cursor().execute(sql)

                for row in cursor.fetchall():

                    data = complete_data_login(data=data, account=alfaCustomerId, superadmin=0, admin=0, bloqueado=0,
                                               token=str_token, dbname=database_information[0]["dbname"], nombre=row.nombre,
                                               company_name=database_information[0]["company_name"], account_type="", id_seller="", idlista=row.idlista, hide_prices=row.oculta_precios, email="",
                                               idcaja="", zona="", auth_menu="", id_driver="", username=row.codigo, cf_account=row.codigo)

                    # data["cf_account"] = row.codigo
                    # data["nombre"] = row.nombre
                    # data["idlista"] = row.idlista
                    # data["hide_prices"] = row.oculta_precios
                    return False
                return True
            except Exception as e:
                print(
                    f"Ocurrió un error al obtener la clave el usuario {username}: ", e)
                return True
    print("Ocurrió un error al intentar registrar la sesión: ", data)
    return True


def get_authorized_menu_seller(seller_name: str, token: str):
    result = []

    sql = f"""
        SELECT usuario,sistema,tarea FROM TA_TAREAS
        WHERE usuario='{seller_name}'
        AND (tarea='D75' OR tarea='D7515' OR tarea='D80' OR tarea='D8079' OR tarea='D60' OR tarea='D6010' OR tarea='D6009' OR tarea='D50' OR tarea='D5004' OR tarea='D6003'
        OR tarea='D0101' OR tarea='D010105'  OR tarea='D010120'
        )
    """
    # AND (tarea='D75' OR tarea='D7515' OR tarea='D80' OR tarea='D8079' OR tarea='D60' OR tarea='D6010' OR tarea='D6009' OR tarea='D50' OR tarea='D5004')

    # print(sql)
    result, _ = get_customer_response(
        sql, f" el menu del vendedor {seller_name}", True, token=token)
    # print(result)
    return result

# GET menú completo para cargar el arbol de módulos
def get_all_authorized_menu(token: str):
    result = []

    sql = f"""
        SELECT TITULO, CLAVE, NOMBRE, HABILITADO
        FROM TA_MENU
        WHERE MENU = 'ALFA' AND HABILITADO = 1
        ORDER BY CLAVE
    """

    result, _ = get_customer_response(
        sql, f" el menu completo", True, token=token
    )
    
    return result

# GET módulos activos (checked) del menú
def get_user_checked_authorized_menu(seller_name: str, token: str):
    result = []

    sql = f"""
        SELECT m.TITULO, m.CLAVE, m.NOMBRE, m.HABILITADO 
        FROM TA_TAREAS t
        JOIN TA_MENU m ON t.TAREA = m.CLAVE
        WHERE t.USUARIO = '{seller_name}' AND t.SISTEMA = 'CN000PR' AND m.MENU = 'ALFA' AND m.HABILITADO = 1
        ORDER BY m.CLAVE ASC
    """

    result, _ = get_customer_response(
        sql, f" el menu del vendedor {seller_name}", True, token=token
    )
    
    return result

def get_format_print_invoice(tc: str, cpte: str, account: str, token: str):
    """
    Retorna un diccionario con el detalle de un comprobante
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

    # Obtengo los datos de la empresa, que son comunes a todos los comprobantes
    #Ver si lo reemplazo por la clase
    sql = f"""
    exec sp_web_getDatosEmpresa
    """
    datos_empresa, error = get_customer_response(sql, "datos empresa", True, token)

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
        SELECT a.cuenta,b.DESCRIPCION as nombre ,a.idcajas,c.Descripcion as caja,CASE WHEN a.[DEBE-HABER]='D' THEN convert(varchar,convert(decimal(8,2),a.importe)) ELSE '0' END as imported, CASE WHEN a.[DEBE-HABER]='H' THEN convert(varchar,convert(decimal(8,2),a.importe)) ELSE '0' END as importeh,a.[DEBE-HABER] as dh
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
            
        
        # Datos de la empresa

        for items in result:
            items["empresa"] = {k: datos_empresa for (
                k, v) in items.items() if k == "empresa"}

        return result
        
            
    # Cabecera
    sql = f"""
    SELECT '' as empresa,'' as insumos,'' as electronico,'' as cptes_aplicados,'' as medios_pago,'' as retenciones, cuenta,tc,sucursal,numero,letra,idcomprobante,fecha,fechaestfin as vencimiento,nombre,domicilio,telefono,localidad,codigopostal,
    documentonumero,observaciones,isnull(comentarios,'') as comentarios,
    convert(varchar,convert(decimal(8,2),netonogravado)) as netonogravado,
    convert(varchar,convert(decimal(8,2),netogravado)) as netogravado,
    convert(varchar,convert(decimal(8,2),importe)) as importe,
    convert(varchar,convert(decimal(8,2),importe_s_iva)) as importe_s_iva,
    convert(varchar,convert(decimal(8,2),porcdescuento1)) as porcdescuento1,
    convert(varchar,convert(decimal(8,2),impdescuento1)) as impdescuento1,
    convert(varchar,convert(decimal(8,2),importeiva)) as importeiva,
    convert(varchar,convert(decimal(8,2),importeiva2)) as importeiva2,
    convert(varchar,convert(decimal(8,2),aliciva)) as aliciva,
    convert(varchar,convert(decimal(8,2),aliciva2)) as aliciva2,
    convert(varchar,convert(decimal(8,2),RETIBR_Importe)) as iibb_importe,
    ta_condiva.descripcion as condiva, v_ta_cpra_vta.descripcion as condvta from v_mv_cpte
    LEFT JOIN TA_CONDIVA ON v_mv_Cpte.condicioniva = ta_condiva.codigo LEFT JOIN V_TA_Cpra_Vta ON V_MV_CPTE.idcond_cpra_vta = V_TA_Cpra_Vta.IDCond_Cpra_Vta
    where tc='{tc}' AND IDCOMPROBANTE='{cpte}' {where}
    """

    result, error = get_customer_response(
        sql, "Cabecera comprobante", True, token)

    if error:
        return jsonify({"error": f"ocurrió un error al obtener la cabecera del comprobante {tc} {cpte}"})

    # Datos de la empresa

    for items in result:
        items["empresa"] = {k: datos_empresa for (
            k, v) in items.items() if k == "empresa"}

    # Insumos
    sql = f"""
    SELECT ltrim(idarticulo) as idarticulo,descripcion,idunidad,
    convert(varchar,convert(decimal(8,2),cantidadud)) as cantidadud,
    convert(varchar,convert(decimal(8,2),cantidad)) as cantidad,
    convert(varchar,convert(decimal(8,2),importe)) as importe,
    convert(varchar,convert(decimal(8,2),importe_s_iva)) as importe_s_iva,
    convert(varchar,convert(decimal(8,2),isnull(aliciva,21))) as aliciva,
    convert(varchar,convert(decimal(8,2),total)) as total from v_mv_cpteinsumos
    where tc='{tc}' AND IDCOMPROBANTE='{cpte}'
    UNION
    SELECT '' as idarticulo,CONVERT(VARCHAR(MAX),observacion) as descripcion,'' as idunidad,
    '1' as cantidadud,
    '1' as cantidad,
    convert(varchar,convert(decimal(8,2),importe)) as importe,
    convert(varchar,convert(decimal(8,2),importe_s_iva)) as importe_s_iva,
    '' as aliciva,
    convert(varchar,convert(decimal(8,2),importe)) as total from V_MV_CPTE_OBSERV
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

   
    return result


def decrypt_password(password: str):
    """
    Retorna la clave desencriptada
    """
    decrypt = ""
    word = ""

    index = 0

    for letra in password[::-1]:
        word = letra + word
        index += 1

        if index == 3:
            decrypt += chr(int(word) - int(password.__len__() / 3))
            index = 0
            word = ""

    return decrypt


def forgot_password_seller(data):
    """
    Retorna la contraseña de un vendedor
    """
    email = data["email"]
    alfaCustomerId = data["alfaCustomerId"]
    database_id = data["databaseId"]

    data["account"] = alfaCustomerId
    # data["superadmin"] = 0
    token = write_token(data)
    str_token = token.decode('UTF-8')

    if register_session(str_token, data):
        data["token"] = str_token
        if set_db(alfaCustomerId, database_id, str_token):

            connection = get_conn(str_token)

            sql = f"""
                SELECT ltrim(a.nombre) as nombre,ltrim(a.password) as claveweb,ltrim(a.idvendedor) as idvendedor,ltrim(isnull(b.idlista,'')) as idlista
                FROM TA_USUARIOS a
                LEFT JOIN V_TA_VENDEDORES b on a.idvendedor = b.idvendedor
                WHERE a.email_usuario='{email}' and a.idvendedor<>''
                """
            try:
                cursor = connection.cursor().execute(sql)

                for row in cursor.fetchall():
                    return decrypt_password(row.claveweb)
            except Exception as e:
                return ""
    return ""


def forgot_password_customer(data):
    """
    Obtiene la contraseña de un cliente
    """
    email = data["email"]
    alfaCustomerId = data["alfaCustomerId"]
    database_id = data["databaseId"]

    data["account"] = alfaCustomerId
    # data["superadmin"] = 0
    token = write_token(data)
    str_token = token.decode('UTF-8')

    if register_session(str_token, data):
        data["token"] = str_token
        if set_db(alfaCustomerId, database_id, str_token):
            connection = get_conn(str_token)

            sql = f"""
                SELECT ltrim(clave) as claveweb FROM VT_CLIENTES WHERE
                email='{email}' and clave<>''
            """
            try:
                cursor = connection.cursor().execute(sql)

                for row in cursor.fetchall():
                    return row.claveweb
            except Exception as e:
                return ""
    return ""


def rgbint_to_hex(rgbint):
    """Convierte un color int (VB6) a Hex"""
    if rgbint:
        b = int(rgbint // 256 // 256 % 256)
        g = int(rgbint // 256 % 256)
        r = int(rgbint % 256)
    else:
        r = 0
        g = 0
        b = 0

    return "#{:02x}{:02x}{:02x}".format(r, g, b)


def decode_id_account(id):
    """
    Retorna el codigo de cuenta decodificado
    """
    pos = 0
    decryptedCode = ""
    tmp = ""

    for code in id:
        tmp = tmp + code

        if pos == 8:
            decryptedCode = decryptedCode + tmp[0] + tmp[4] + tmp[8]
            tmp = ""
            pos = -1
        pos = pos+1

    return decryptedCode


def get_last_contract_account(data: dict):
    """
    Retorna el último contrato de la cuenta
    """
    result = []
    contact_id = data["id_contact"]
    account = data["username"]

    token = write_token(data)
    str_token = token.decode('UTF-8')

    if register_session(str_token, data):
        data["token"] = str_token
        if set_db(data["alfaCustomerId"], data["databaseId"], str_token):

            connection = get_conn(str_token)

            sql = f"""
            SELECT TOP 1 a.id as idfile,a.cuenta,a.ruta_archivo,a.periododesde,a.periodohasta,b.idcontacto,
            isnull(a.nombrefirmante,b.nombre_y_apellido) as nombre,isnull(a.email,isnull(b.email,'')) as email,isnull(a.telefono,isnull(b.Telefono,'')) as telefono,a.nombrefirmante,
            isnull(convert(NVARCHAR(18),a.fechahoraconfirmacion,103),'') + ' ' + isnull(convert(NVARCHAR(18),a.fechahoraconfirmacion,108),'') as fechaconfirmacion,
            a.fechahoraconfirmacion,isnull(a.seleccion,'') as seleccion
            FROM MA_CUENTAS_ARCHIVOS_RELACIONADOS a
            LEFT JOIN VT_MA_CUENTAS_CONTACTOS b ON a.CUENTA = b.cuenta
            WHERE a.CUENTA='{account}' and b.idcontacto = {contact_id} and a.EsContrato=1 and a.RUTA_ARCHIVO<>''
            ORDER By a.id desc
            """

            # AND (a.FechaHoraConfirmacion is null or a.FechaHoraConfirmacion = '') ORDER By a.id desc
            try:
                cursor = connection.cursor().execute(sql)

                for row in cursor.fetchall():
                    result.append({
                        'account': row.cuenta,
                        'file': row.ruta_archivo,
                        'periododesde': row.periododesde,
                        'periodohasta': row.periodohasta,
                        'name': row.nombre,
                        'phone': row.telefono,
                        'email': row.email,
                        'idfile': row.idfile,
                        'nameSigned': row.nombrefirmante,
                        'dateSigned': row.fechaconfirmacion,
                        'selection': row.seleccion
                    })
                    return False, result
                return True, []
            except Exception as e:
                Log.create(f"Error al obtener el contrato : {e}", account)
                # abort(jsonify(result[0]), 500)
                # print(f"Ocurrió un error al obtener el autologin: ", e)
                return True, []
    print("Ocurrió un error al intentar registrar la sesión: ", data)
    return True, []


def update_contract(data: dict, payload: dict):
    """
    Actualizo un contrato
    """
    token = write_token(data)
    str_token = token.decode('UTF-8')

    if register_session(str_token, data):
        data["token"] = str_token
        if set_db(data["alfaCustomerId"], data["databaseId"], str_token):

            connection = get_conn(str_token)

            sql = f"""
            UPDATE MA_CUENTAS_ARCHIVOS_RELACIONADOS SET
            FechaHoraConfirmacion = GETDATE(),
            NombreFirmante = '{payload['name']}',
            email = '{payload['email']}',
            telefono = '{payload['phone']}',
            seleccion = '{payload['selection']}',
            comentarios = '{payload['comments']}'
            WHERE ID = {payload['id_file']}
            """
            try:
                cursor = connection.cursor().execute(sql)
                connection.commit()
                connection.close()
                return False
            except Exception as e:
                print(f"Ocurrió un error al firmar el contrato: ", e)
                return True, []
    print("Ocurrió un error al intentar registrar la sesión: ", data)
    return True, []


def get_last_budget_account(data: dict):
    """
    Retorna el último presupuesto de la cuenta
    """
    result = []
    contact_id = data["id_contact"]
    account = data["username"]

    token = write_token(data)
    str_token = token.decode('UTF-8')

    if register_session(str_token, data):
        data["token"] = str_token
        if set_db(data["alfaCustomerId"], data["databaseId"], str_token):

            connection = get_conn(str_token)

            sql = f"""
            SELECT TOP 1 a.id as idfile,a.cuenta,a.ruta_archivo,a.periododesde,a.periodohasta,b.idcontacto,
            isnull(a.nombrefirmante,b.nombre_y_apellido) as nombre,isnull(a.email,isnull(b.email,'')) as email,isnull(a.telefono,isnull(b.Telefono,'')) as telefono,a.nombrefirmante,
            isnull(convert(NVARCHAR(18),a.fechahoraconfirmacion,103),'') + ' ' + isnull(convert(NVARCHAR(18),a.fechahoraconfirmacion,108),'') as fechaconfirmacion,
            a.fechahoraconfirmacion,isnull(a.seleccion,'') as seleccion,a.nropresupuesto
            FROM MA_CUENTAS_ARCHIVOS_RELACIONADOS a
            LEFT JOIN VT_MA_CUENTAS_CONTACTOS b ON a.CUENTA = b.cuenta
            WHERE a.CUENTA='{account}' and b.idcontacto = {contact_id} and a.EsPresupuesto=1 and a.RUTA_ARCHIVO<>'' and a.nropresupuesto <>''
            ORDER By a.id desc
            """
            # AND (a.FechaHoraConfirmacion is null or a.FechaHoraConfirmacion = '') ORDER By a.id desc
            try:
                cursor = connection.cursor().execute(sql)

                for row in cursor.fetchall():
                    result.append({
                        'account': row.cuenta,
                        'file': row.ruta_archivo,
                        'periododesde': row.periododesde,
                        'periodohasta': row.periodohasta,
                        'name': row.nombre,
                        'phone': row.telefono,
                        'email': row.email,
                        'idfile': row.idfile,
                        'nameSigned': row.nombrefirmante,
                        'dateSigned': row.fechaconfirmacion.strip(),
                        'selection': row.seleccion,
                        'nropresupuesto': row.nropresupuesto
                    })
                    return False, result
                return True, []
            except Exception as e:
                Log.create(f"Error al obtener el contrato : {e}", account)
                # abort(jsonify(result[0]), 500)
                # print(f"Ocurrió un error al obtener el autologin: ", e)
                return True, []
    print("Ocurrió un error al intentar registrar la sesión: ", data)
    return True, []


def update_budget(data: dict, payload: dict):
    """
    Actualizo un presupuesto
    """

    token = write_token(data)
    str_token = token.decode('UTF-8')

    if register_session(str_token, data):
        data["token"] = str_token
        if set_db(data["alfaCustomerId"], data["databaseId"], str_token):

            connection = get_conn(str_token)

            sql = f"""
            UPDATE MA_CUENTAS_ARCHIVOS_RELACIONADOS SET
            FechaHoraConfirmacion = GETDATE(),
            NombreFirmante = '{payload['name']}',
            email = '{payload['email']}',
            telefono = '{payload['phone']}',
            seleccion = '{"Aprobado" if payload['selection'] == "1" else "Desaprobado"}',
            comentarios = '{payload['comments']}'
            WHERE ID = {payload['id_file']}

            UPDATE V_MV_CPTE SET Finalizada=1, Aprobado={1 if payload['selection'] == "1" else 0}
            WHERE TC='PR' AND IDCOMPROBANTE='{payload['id_budget']}'
            """

            try:
                cursor = connection.cursor().execute(sql)
                connection.commit()
                connection.close()
                return False
            except Exception as e:
                print(f"Ocurrió un error al firmar el contrato: ", e)
                return True, []
    print("Ocurrió un error al intentar registrar la sesión: ", data)
    return True, []


def complete_data_login(data: dict, username: str, account: str, superadmin: int, admin: int, account_type: str, token: str, dbname: str, company_name: str,  nombre: str, idlista: str, cf_account: str = '', hide_prices: int = 0,
                        id_seller: str = '', auth_menu: str = '', id_driver: str = '', email: str = '', idcaja: str = '', bloqueado: int = 0, zona: str = "", sucursal_defecto:str = ""):


    data["username"] = username
    data["account"] = account
    data["superadmin"] = superadmin
    data["admin"] = admin
    data["account_type"] = account_type
    data["token"] = token
    data["dbname"] = dbname
    data["company_name"] = company_name
    data["id_seller"] = id_seller
    data["auth_menu"] = auth_menu
    data["id_driver"] = id_driver
    data["email"] = email
    data["idcaja"] = idcaja
    data["bloqueado"] = 0
    data["cf_account"] = cf_account
    data["nombre"] = nombre
    data["idlista"] = idlista
    data["hide_prices"] = hide_prices
    data["zona"] = zona
    data["branch_default"] = sucursal_defecto
    data["final_prices"] = True



    # data["config"] = get_config(token)

    return data


def get_config(token):
    connection = get_conn(token)
    result = []

    try:
        cursor = connection.cursor().execute(config_query)

        for row in cursor.fetchall():
            result.append({
                'key': row.clave,
                'value': row.valor,
            })
        return result
    except Exception as e:
        Log.create(f"Error al obtener la configuración : {e}")
        return []

def autologin_customer_for_odoo(data: dict):
    # Obtengo la información de un contacto a partir de un id.
    """
    Retorna un token para un autologin de contacto
    """
    # alfaCustomerId = decode_id_account(data["alfaCustomerId"])    
    alfaCustomerId = data["alfaCustomerId"]
    database_id = data["databaseId"]

    data["username"] = alfaCustomerId
    data["password"] = "1"
    data["account"] = alfaCustomerId

    token = write_token(data)
    
    str_token = token.decode('UTF-8')

    if register_session(str_token, data):
        if set_db(alfaCustomerId, database_id, str_token):

            database_information = get_info_session(str_token)
            connection = get_conn(str_token)


            sql = f"""
            SELECT DESCRIPCION as nombre,a.CODIGO as codigo,
            LTRIM(a.Clave) as claveweb,
            ISNULL(LTRIM(c.idlista),'') as idlista,ISNULL(a.MAIL,'') as email
            FROM MA_CUENTAS b
            INNER JOIN MA_CUENTASADIC a on b.CODIGO = a.CODIGO
            LEFT JOIN vt_clientes c on b.CODIGO = c.CODIGO
            WHERE b.CODIGO = '{alfaCustomerId}'
            """
            try:
                cursor = connection.cursor().execute(sql)

                for row in cursor.fetchall():
                    print(row)

                    data = complete_data_login(data=data, account=alfaCustomerId, superadmin=0, admin=0, bloqueado=0,
                                               token=str_token, dbname=database_information[0]["dbname"], nombre=row.nombre,
                                               company_name=database_information[0]["company_name"], account_type="", id_seller="", 
                                               idlista=row.idlista, hide_prices=0, email=row.email,
                                               idcaja="", zona="", auth_menu="", id_driver="", username=row.codigo)
                    return False

                return True
            except Exception as e:
                print(f"Ocurrió un error al obtener el autologin: ", e)
                return True
    print("Ocurrió un error al intentar registrar la sesión: ", data)
    return True
