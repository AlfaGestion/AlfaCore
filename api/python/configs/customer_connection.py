import pyodbc
from functions.session import get_info_session
from rich import print
from config import DB_VERSION
from functions.Log import Log

def get_conn(token: str):
    """
    Retorna la conexion a la base de datos de la sesión
    """

    try:
        session = get_info_session(token)
        session = session[0]

    except Exception as e:
        session = []
        print("Error al obtener conexión del cliente: ", e)
    #Log.create(session)
    try:
        conn = pyodbc.connect('Driver={SQL Server Native Client '+DB_VERSION+'};Server=' +
                              session['dbserver']+';Database='+session['dbname']+';uid='+session['dbuser']+';pwd='+session['dbpassword']+';MARS_Connection=Yes')
    except Exception as e:
        print("Ocurrió un error al conectar al SQL Server del cliente : ", e)
        return ''

    return conn
