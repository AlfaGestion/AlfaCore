import pyodbc
from db import *

if server != '':
    try:
        conn = pyodbc.connect('Driver={SQL Server Native Client 11.0};Server='+server+';Database='+database+';uid='+user+';pwd='+password)
        # conn = pyodbc.connect('Driver={ODBC Driver 17 for SQL Server};Server='+server+';Database='+database+';uid='+user+';pwd='+password)        
        #return conn
    except Exception as e:
        print("Ocurrió un error al conectar a SQL Server : ", e)
else:
    conn = ''