import pyodbc

from config import DB_SERVER, DB_NAME, DB_PASS, DB_USER, DB_VERSION, SA_PASSWORD, SA_USER


conn = ''


def _build_connection_string(database_name: str, user: str, password: str):
    return (
        'Driver={SQL Server Native Client ' + DB_VERSION + '};'
        + 'Server=' + DB_SERVER + ';'
        + 'Database=' + database_name + ';'
        + 'uid=' + user + ';'
        + 'pwd=' + password + ';'
        + 'MARS_Connection=Yes'
    )


def get_connection(force_new: bool = False):
    global conn

    if not force_new and conn not in ('', None):
        try:
            conn.cursor().execute('SELECT 1')
            return conn
        except Exception:
            conn = ''

    try:
        conn = pyodbc.connect(_build_connection_string(DB_NAME, DB_USER, DB_PASS))
    except Exception as e:
        print('Ocurrió un error al conectar a SQL Server : ', e)
        conn = ''

    return conn


try:
    conn = get_connection(force_new=True)
except Exception:
    conn = ''


def get_master_connection():
    try:
        master_conn = pyodbc.connect(
            _build_connection_string('master', SA_USER, SA_PASSWORD) + ';',
            autocommit=True,
        )
    except Exception as e:
        print('Ocurrió un error al conectar a SQL Server (Master): ', e)
        master_conn = ''

    return master_conn