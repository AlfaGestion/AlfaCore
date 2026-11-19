from flask import json
from configs.connection import conn, get_connection
from datetime import datetime
from functions.Log import Log
from configs.query import config_query
from config import ML_API_ID
# from rich import print


def is_valid_account(data):
    """
    Valida si una cuenta y su contraseña son correctas
    """
    username = data["username"]

    try:
        sql_conn = get_connection(force_new=True)
        if sql_conn == '':
            return True

        cursor = sql_conn.cursor().execute(
            f"""SELECT a.idcliente, a.[user], a.password, isnull(b.superadmin, 0) as superadmin,isnull(a.isAdmin,0) as is_admin,isnull(b.type,'A') as type
            FROM users a LEFT JOIN clientes b
            ON a.idcliente = b.idcliente
            WHERE a.[user]='{username}' and (b.verified=1 OR b.verified is null) """)

        for row in cursor.fetchall():
            if row.password == data["password"]:

                data = complete_data_login(data=data, username=username, account=row.idcliente, superadmin=row.superadmin, admin=row.is_admin,
                                           bloqueado=0, token="", dbname="", nombre="",
                                           company_name="", account_type=row.type, id_seller="", idlista="", hide_prices=0, email="",
                                           idcaja="", zona="", sucursal_defecto="")
                return False
        return True
    except Exception as e:
        print(f"Ocurrió un error al obtener la clave el usuario {username}: ", e)
        return True


def register_session(token: str, data: json, default_session: bool = True):
    """
    Registra la sesión del usuario
    Toma el token en string y reemplaza
    . -> blanco
    """
    account: str = data["account"]
    username: str = data["username"]
    name: str = ""
    created_at: str = ""

    try:
        sql_conn = get_connection(force_new=True)
        if sql_conn == '':
            return False

        cursor = sql_conn.cursor().execute(
            f"SELECT idcliente, nombre FROM clientes WHERE idcliente='{account}'")

        for row in cursor.fetchall():
            name = row.nombre

        created_at = datetime.today().strftime('%d/%m/%Y %H:%M:%S')

        query = f"""
            INSERT INTO sessions (token,idcliente,nombre,username,created_at) 
            values ('{token.replace('.','')}','{account}','{name}','{username}','{created_at}')
            """

        sql_conn.cursor().execute(query)
        sql_conn.commit()
        return True
    except Exception as e:
        print(f"Ocurrió un error al obtener la clave el usuario {username}: ", e)
        return True


def get_config():

    result = []

    try:
        sql_conn = get_connection(force_new=True)
        if sql_conn == '':
            return []

        cursor = sql_conn.cursor().execute(config_query)

        for row in cursor.fetchall():
            result.append({
                'key': row.clave,
                'value': row.valor,
            })
        return result
    except Exception as e:
        Log.create(f"Error al obtener la configuración : {e}")
        return []


def complete_data_login(data: dict, username: str, account: str, superadmin: int, admin: int, account_type: str, token: str, dbname: str, company_name: str,  nombre: str, idlista: str, cf_account: str = '', hide_prices: int = 0,
                        id_seller: str = '', auth_menu: str = '', id_driver: str = '', email: str = '', idcaja: str = '', bloqueado: int = 0, zona: str = "", sucursal_defecto: str = ''):

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
    data["id_driver"] = ""
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
    data["ML_API_ID"] = ML_API_ID

    return data