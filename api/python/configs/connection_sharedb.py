import pyodbc
from config import DB_VERSION,DB_NAME_SHARE,DB_SERVER_SHARE,DB_USER_SHARE,DB_PASS_SHARE

def get_conn_sharedb():
    try:
        conn = pyodbc.connect('Driver={SQL Server Native Client '+DB_VERSION+'};Server=' +
                              DB_SERVER_SHARE+';Database='+DB_NAME_SHARE+';uid='+DB_USER_SHARE+';pwd='+DB_PASS_SHARE)
    except Exception as e:
        print("Ocurrió un error al conectar a SQL Server : ", e)
        return ''

    return conn
