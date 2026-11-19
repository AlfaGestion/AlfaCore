from datetime import datetime

from configs.connection import get_connection
from functions.DataBase import DataBase
from functions.Log import Log
from functions.general import get_format_response
from functions.responses import set_response


def get_dbases(account: str) -> list:
    """
    Retorna las bases del cliente
    """
    result = []

    sql = f"""
    SELECT id,nombre,dbname FROM bases where idcliente='{account}'
    """
    result, _ = get_format_response(sql, " las bases del cliente", True)
    return result


def set_db(account: str, id: int, token: str):
    """
    Establece una base de datos en la sesion
    """

    connection_ok_log = ""

    try:
        db_info = get_db_info(id)[0]
        connection_ok_log = (
            f"INICIO DE CONEXION: {datetime.now().strftime('%d-%m-%Y %H:%M:%S')}\n"
            f"CLIENTE: {account}\n"
            f"BASE: {db_info.get('dbname', '')}\n"
            f"SERVER: {db_info.get('dbserver', '')}\n"
            f"USUARIO: {db_info.get('dbuser', '')}\n"
            f"ERROR: NO\n"
            f"FIN DE CONEXION: {datetime.now().strftime('%d-%m-%Y %H:%M:%S')}"
        )
    except Exception as e:
        print("Ocurrio un error al obtener los datos de la base")
        db_info = {}
        Log.create(
            (
                f"INICIO DE CONEXION: {datetime.now().strftime('%d-%m-%Y %H:%M:%S')}\n"
                f"CLIENTE: {account}\n"
                f"ERROR: SI\n"
                f"DETALLE ERROR: {e}\n"
                f"FIN DE CONEXION: {datetime.now().strftime('%d-%m-%Y %H:%M:%S')}"
            ),
            account,
            "ERROR",
            token=token,
        )
        return False

    sql = f"""
    UPDATE sessions SET dbname='{db_info['dbname']}',dbuser='{db_info['dbuser']}',
    dbpassword='{db_info['dbpassword']}',dbserver='{db_info['dbserver']}',
    company_name='{db_info['nombre']}',path='{db_info['path']}'
    WHERE idcliente='{account}' AND token='{token.replace(".","")}'
    """

    try:
        sql_conn = get_connection(force_new=True)
        if sql_conn in ("", None):
            raise RuntimeError("No se pudo obtener la conexion principal.")

        sql_conn.cursor().execute(sql)
        sql_conn.commit()

        Log.create_once(
            connection_ok_log,
            account,
            "INFO",
            token=token,
            dedupe_key=(
                f"CONNECTION_OK|{account}|{db_info.get('dbserver', '')}|"
                f"{db_info.get('dbname', '')}|{db_info.get('dbuser', '')}"
            ),
        )

        update_database = False
        last_update = db_info["last_update"]
        if last_update is None or last_update == "":
            update_database = True
        else:
            now = datetime.now()
            dif = now - last_update
            if dif.total_seconds() / 60 > 180:
                update_database = True

        if update_database:
            update_ok, update_error = DataBase.update(
                db_info["dbserver"],
                db_info["dbname"],
                db_info["dbuser"],
                db_info["dbpassword"],
            )
            if not update_ok:
                Log.create(
                    (
                        f"ERROR SET_DB UPDATE DATABASE: {datetime.now().strftime('%d-%m-%Y %H:%M:%S')}\n"
                        f"CLIENTE: {account}\n"
                        f"BASE: {db_info.get('dbname', '')}\n"
                        f"SERVER: {db_info.get('dbserver', '')}\n"
                        f"DETALLE ERROR: {update_error}"
                    ),
                    account,
                    "ERROR",
                    token=token,
                )
            set_last_update_db(id)
        return True
    except Exception as e:
        print("Ocurrio un error al setear la base: ", e)
        Log.create(
            (
                f"ERROR DE CONEXION: {datetime.now().strftime('%d-%m-%Y %H:%M:%S')}\n"
                f"CLIENTE: {account}\n"
                f"ERROR: SI\n"
                f"DETALLE ERROR: {e}\n"
                f"FIN DE CONEXION: {datetime.now().strftime('%d-%m-%Y %H:%M:%S')}"
            ),
            account,
            "ERROR",
            token=token,
        )
        return False


def set_last_update_db(database_id: int):
    now = datetime.now().strftime("%d-%m-%Y %H:%M:%S")
    sql = f"UPDATE bases SET last_update='{now}' WHERE id={database_id}"

    try:
        sql_conn = get_connection(force_new=True)
        if sql_conn in ("", None):
            raise RuntimeError("No se pudo obtener la conexion principal.")
        sql_conn.cursor().execute(sql)
        sql_conn.commit()
    except Exception as e:
        print("Ocurrio un error al actualizar la fecha de ultima actualizacion de la base: ", e)


def get_db_info(id: int):
    """
    Retorna la informacion de una base de datos especifica
    """
    sql = f"""
    SELECT id,nombre,dbserver,dbname,dbuser,dbpassword,path,last_update
    FROM bases where id={id}
    """
    result, _ = get_format_response(sql, " las base del cliente", True)
    return result


def get_info_session(token: str):
    """
    Retorna la informacion de una sesion
    """

    result = []

    sql = f"""
    SELECT id,idcliente,dbname,dbuser,dbpassword,dbserver,nombre, company_name,isnull(path,'') as path
    FROM sessions WHERE token='{token.replace(".","")}'
    """

    result, _ = get_format_response(sql, " la informacion de la sesion", True)

    return result
