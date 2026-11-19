import os
import pyodbc
from flask import Blueprint, jsonify, request

cliente_id_bp = Blueprint('agw_v1_cliente_id_bp', __name__)


def _sql_conn_str() -> str:
    user = (os.getenv("SQL_USER") or "").strip()
    pwd = (os.getenv("SQL_PASSWORD") or "").strip()
    server = (os.getenv("SQL_SERVER") or "").strip()
    db = (os.getenv("SQL_DATABASE") or "").strip()
    driver = (os.getenv("SQL_DRIVER") or "").strip()
    if not all([user, pwd, server, db, driver]):
        return ""
    return (
        f"DRIVER={{{driver}}};"
        f"SERVER={server};"
        f"DATABASE={db};"
        f"UID={user};"
        f"PWD={pwd};"
        "TrustServerCertificate=yes;"
    )


def _get_conn_alfa_with_fallback():
    errors = []

    # 1) Prioriza SQL_* porque es la configuración activa en este entorno.
    conn_str = _sql_conn_str()
    if conn_str:
        try:
            return pyodbc.connect(conn_str, timeout=2), ""
        except Exception as e:
            errors.append(f"sql_fallback: {e}")
    else:
        errors.append("sql_fallback: missing SQL_*")

    # 2) ALFA_CENTRAL explícita (si está configurada)
    dbv = (os.getenv("DB_VERSION") or "").strip()
    srv = (os.getenv("DB_SERVER_ALFA") or "").strip()
    dbn = (os.getenv("DB_NAME_ALFA") or "").strip()
    usr = (os.getenv("DB_USER_ALFA") or "").strip()
    pwd = (os.getenv("DB_PASS_ALFA") or "").strip()
    if all([dbv, srv, dbn, usr, pwd]):
        try:
            conn = pyodbc.connect(
                f"Driver={{SQL Server Native Client {dbv}}};"
                f"Server={srv};"
                f"Database={dbn};"
                f"uid={usr};"
                f"pwd={pwd}",
                timeout=2
            )
            return conn, ""
        except Exception as e:
            errors.append(f"alfa: {e}")
    else:
        errors.append("alfa: missing DB_*_ALFA")

    return '', " | ".join(errors)


@cliente_id_bp.route('/obtener_idcliente/<string:licencia_param>', methods=['GET'])
@cliente_id_bp.route('/obtener_idcliente', methods=['GET'])
def obtener_idcliente(licencia_param=''):
    licencia = str(
        licencia_param
        or request.args.get('licenciaprincipal')
        or request.args.get('licencia_principal')
        or request.args.get('licencia')
        or ''
    ).strip()
    if not licencia:
        return jsonify({'ok': False, 'error': 'licenciaprincipal_required'}), 400

    conn, conn_error = _get_conn_alfa_with_fallback()
    if conn == '':
        return jsonify({'ok': False, 'error': f'alfa_connection_error: {conn_error}'}), 500

    try:
        sql = """
        SELECT TOP 1 idcliente
        FROM clientes
        WHERE LTRIM(RTRIM(licenciaprincipal)) = LTRIM(RTRIM(?))
        """
        cursor = conn.cursor()
        cursor.execute(sql, (licencia,))
        row = cursor.fetchone()
    except Exception as e:
        return jsonify({'ok': False, 'error': f'query_error: {e}'}), 500
    finally:
        try:
            conn.close()
        except Exception:
            pass

    if not row:
        return jsonify({'ok': False, 'error': 'not_found', 'licenciaprincipal': licencia}), 404

    return jsonify({'ok': True, 'idcliente': str(row[0]).strip(), 'licenciaprincipal': licencia}), 200
