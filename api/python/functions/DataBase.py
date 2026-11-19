from datetime import datetime
from configs.connection import get_master_connection, conn
from config import DB_SERVER, SA_PASSWORD, SA_USER, API_ROOT
import subprocess
import os
import re
from pathlib import Path
from functions.Log import Log
SERVER_DEFAULT = DB_SERVER


def _available_odbc_driver() -> str:
    """
    Devuelve el mejor driver ODBC disponible para SQL Server.
    """
    try:
        import pyodbc  # type: ignore
    except Exception:
        return ""

    preferred = [
        "ODBC Driver 18 for SQL Server",
        "ODBC Driver 17 for SQL Server",
        "ODBC Driver 13 for SQL Server",
        "SQL Server Native Client 11.0",
        "SQL Server",
    ]
    installed = set(pyodbc.drivers())
    for driver in preferred:
        if driver in installed:
            return driver
    return next(iter(installed), "")


def _run_script_via_pyodbc(server: str, dbname: str, dbuser: str, dbpassword: str, script_path: str) -> None:
    """
    Fallback para ejecutar scripts SQL cuando sqlcmd falla resolviendo la instancia.
    Soporta batches separados por GO.
    """
    driver = _available_odbc_driver()
    if not driver:
        raise RuntimeError("No hay drivers ODBC de SQL Server disponibles")

    try:
        import pyodbc  # type: ignore
    except Exception as e:
        raise RuntimeError(f"No se pudo importar pyodbc: {e}") from e

    conn_str = (
        f"DRIVER={{{driver}}};"
        f"SERVER={server};"
        f"DATABASE={dbname};"
        f"UID={dbuser};"
        f"PWD={dbpassword};"
        "TrustServerCertificate=yes;"
        "Encrypt=no;"
    )

    sql_text = Path(script_path).read_text(encoding="utf-8-sig", errors="ignore")
    batches = re.split(r"(?im)^\s*GO\s*$", sql_text)

    with pyodbc.connect(conn_str, timeout=30, autocommit=False) as connection:
        cursor = connection.cursor()
        for batch in batches:
            batch = batch.strip()
            if not batch:
                continue
            cursor.execute(batch)
        connection.commit()


def _run_sql_file(server: str, dbname: str, script_path: str, dbuser: str, dbpassword: str, log_prefix: str) -> None:
    """
    Ejecuta un script SQL usando sqlcmd y, si falla por instancia/driver,
    reintenta por ODBC directo.
    """
    command = [
        "sqlcmd",
        "-S",
        server,
        "-d",
        dbname,
        "-i",
        script_path,
        "-U",
        dbuser,
        "-P",
        dbpassword,
    ]

    try:
        result = subprocess.run(
            command,
            capture_output=True,
            text=True,
            shell=False,
        )
    except FileNotFoundError as e:
        raise RuntimeError(
            (
                f"{log_prefix}\n"
                f"BASE: {dbname}\n"
                f"SERVER: {server}\n"
                f"DETALLE ERROR: no se encontro sqlcmd en el PATH\n"
                f"EXCEPTION: {e}"
            )
        ) from e
    except Exception as e:
        raise RuntimeError(
            (
                f"{log_prefix}\n"
                f"BASE: {dbname}\n"
                f"SERVER: {server}\n"
                f"DETALLE ERROR: fallo inesperado ejecutando sqlcmd\n"
                f"EXCEPTION: {type(e).__name__}: {e}"
            )
        ) from e

    if result.returncode == 0:
        return

    output = (result.stderr or result.stdout or "").strip()
    needs_fallback = any(
        marker in output
        for marker in (
            "Error Locating Server/Instance Specified",
            "Login timeout expired",
            "network-related or instance-specific error",
        )
    )

    if needs_fallback:
        try:
            _run_script_via_pyodbc(server, dbname, dbuser, dbpassword, script_path)
            return
        except Exception as fallback_error:
            output = f"{output}\nFALLBACK_PYODBC: {fallback_error}".strip()

    raise RuntimeError(
        (
            f"{log_prefix}\n"
            f"BASE: {dbname}\n"
            f"SERVER: {server}\n"
            f"COMANDO: {' '.join(command)}\n"
            f"DETALLE ERROR: {output or 'sqlcmd devolvio un error'}"
        )
    )


class DataBase:

    def __init__(self, customer):
        self.customer = customer

    def create(self, server: str = '', dbname: str = '', name: str = '') -> bool:

        now = datetime.now()
        now = now.strftime("%m%d%Y")

        if dbname == '':
            dbname = f"{self.customer.code}_{now}"

        if server == '':
            server = SERVER_DEFAULT

        if name == '':
            name = self.customer.name

        self.name = name
        self.dbname = dbname
        self.server = server

        try:
            connection = get_master_connection()
            cursor = connection.cursor()

            cursor.execute(f"CREATE DATABASE [{dbname}]")

            # Creo las tablas, vistas, stores y funciones
            _run_sql_file(
                server,
                dbname,
                f"{API_ROOT}stores\\db_create.sql",
                SA_USER,
                SA_PASSWORD,
                f"ERROR CREATE DATABASE: {datetime.now().strftime('%d-%m-%Y %H:%M:%S')}",
            )

            # Creo las stores web
            _run_sql_file(
                server,
                dbname,
                f"{API_ROOT}stores\\Stores_alfaweb.sql",
                SA_USER,
                SA_PASSWORD,
                f"ERROR CREATE STORES: {datetime.now().strftime('%d-%m-%Y %H:%M:%S')}",
            )

            # Inserto información básica
            cursor.execute(f"""
            USE [{dbname}]

            UPDATE TA_CONFIGURACION SET VALOR='{name}' WHERE CLAVE='NOMBRE'
            UPDATE TA_CONFIGURACION SET VALOR='{self.customer.phone}' WHERE CLAVE='TELEFONO'
            UPDATE TA_CONFIGURACION SET VALOR='{self.customer.email}' WHERE CLAVE='EMAIL_DE'
            UPDATE TA_CONFIGURACION SET VALOR='{self.customer.cuit}' WHERE CLAVE='CUIT'
            UPDATE TA_CONFIGURACION SET VALOR='{self.customer.iva}' WHERE CLAVE='CONDIVA'
            """)

            self.__register_in_db()
        except Exception as e:
            Log.create(e, '', 'ERROR')
            return False

        return True

    def __register_in_db(self):
        cursor = conn.cursor().execute(
            f"""
            INSERT INTO [bases] (idcliente,nombre,dbserver,dbname,dbuser,dbpassword, path)
            VALUES ('{self.customer.code}','{self.name}','{self.server}','{self.dbname}','{SA_USER}','{SA_PASSWORD}','.')
            """)
        conn.commit()

    @staticmethod
    def update(server, dbname, dbuser, dbpassword):
        started_at = datetime.now().strftime('%d-%m-%Y %H:%M:%S')
        stores_sql = os.path.join(API_ROOT, "stores", "Stores_alfaweb.sql")
        try:
            Log.create(
                (
                    f"INICIO UPDATE DATABASE: {started_at}\n"
                    f"BASE: {dbname}\n"
                    f"SERVER: {server}\n"
                    f"ARCHIVO: {stores_sql}\n"
                    f"USUARIO: {dbuser}"
                ),
                "",
                "INFO",
                prefix="DBUPDATE",
            )
            try:
                _run_sql_file(server, dbname, stores_sql, dbuser, dbpassword, f"ERROR UPDATE DATABASE: {datetime.now().strftime('%d-%m-%Y %H:%M:%S')}")
                Log.create(
                    (
                        f"FIN UPDATE DATABASE: {datetime.now().strftime('%d-%m-%Y %H:%M:%S')}\n"
                        f"BASE: {dbname}\n"
                        f"SERVER: {server}\n"
                        f"RESULTADO: OK"
                    ),
                    "",
                    "INFO",
                    prefix="DBUPDATE",
                )
                return True, ""
            except Exception as e:
                detail = str(e)
                Log.create(
                    (
                        f"ERROR UPDATE DATABASE: {datetime.now().strftime('%d-%m-%Y %H:%M:%S')}\n"
                        f"BASE: {dbname}\n"
                        f"SERVER: {server}\n"
                        f"DETALLE ERROR: {detail}"
                    ),
                    "",
                    "ERROR",
                    prefix="DBUPDATE",
                )
                return False, detail
        except Exception as e:
            detail = str(e)
            Log.create(
                f"ERROR UPDATE DATABASE FATAL: {detail}",
                "",
                "ERROR",
                prefix="DBUPDATE",
            )
            return False, detail
