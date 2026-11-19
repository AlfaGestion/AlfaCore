import pyodbc
from config import DB_VERSION,DB_SERVER_TRANSPORT,DB_NAME_TRANSPORT,DB_USER_TRANSPORT,DB_PASS_TRANSPORT


def get_conn_transport():
    try:
        conn = pyodbc.connect('Driver={SQL Server Native Client '+DB_VERSION+'};Server='+DB_SERVER_TRANSPORT+';Database='+DB_NAME_TRANSPORT+';uid='+DB_USER_TRANSPORT+';pwd='+DB_PASS_TRANSPORT)
    except Exception as e:
        print("Ocurrió un error al conectar a SQL Server : ", e)
        return ''

    return conn
