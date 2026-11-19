import pyodbc
from config import DB_VERSION,DB_NAME_ALFA,DB_SERVER_ALFA,DB_USER_ALFA,DB_PASS_ALFA



def get_conn_alfa():
    try:
        conn = pyodbc.connect('Driver={SQL Server Native Client '+DB_VERSION+'};Server=' +
                              DB_SERVER_ALFA+';Database='+DB_NAME_ALFA+';uid='+DB_USER_ALFA+';pwd='+DB_PASS_ALFA)
    except Exception as e:
        print("Ocurrió un error al conectar a SQL Server : ", e)
        return ''

    return conn
