from functions.general_customer import exec_customer_sql, get_customer_response
from datetime import datetime
from functions.Log import Log
from functions.Company import Company

from rich import print

class Report:

    TOKEN = None
    
    def __init__(self,  token: str) -> None:
        self.TOKEN = token

    def save(self, fields: dict):
        # print(fields)

        code = fields.get('code',None)
        if not code:
            raise Exception("Debe informar el código.")

        # if not self.__is_valid_code(code):
        #     raise Exception("El código informado no es correcto. Verifique la cantidad de digítos.")
        
        escape_query = fields['query'].replace("'","''")
        index = escape_query.lower().find('from')

        if index != -1:
            after = escape_query[:index]
            before = escape_query[index:]

        if fields['table'].lower() == 'vt_pl_clientes' or fields['table'].lower() == 'vt_pl_proveedores' or fields['table'].lower() == 'ma_cuentas':
            escape_query = after + ",codigo as idReferenciaGlobal, dada_de_baja as estadoDeExistencia " + before
        elif fields['table'].lower() == 'vt_pl_articulos': 
            escape_query = after + ",articulo as idReferenciaGlobal, debaja as estadoDeExistencia " + before

        
        if self.__exists_report(code):
            query = f"""
            UPDATE V_TA_SCRIPT SET SQL='{escape_query}',GRUPO='{fields['name']}',DESCRIPCION='{fields['comments']}',TABLA='{fields['table']}'
            WHERE CLAVE='{code}'
            """
        else:
            query = f"""
            INSERT INTO V_TA_SCRIPT (SQL,MARCA,CLAVE,GRUPO,DESCRIPCION,TABLA)
            VALUES ('{escape_query}','CL','{code}','{fields['name']}','{fields['comments']}','{fields['table']}')
            """

        result, error = exec_customer_sql(query, " al insertar",self.TOKEN,False)
        if error:
            Log.create(result)
            #Intento retornar el mensaje de error del sql
            error_message = result[0].get('message')
            raise Exception(f"Ocurrió un error al grabar el informe. {error_message}.")
            
            # raise Exception("Ocurrió un error al grabar el informe. Intente nuevamente más tarde.")

    def __exists_report(self,code:str)->bool:
        result =[]
        error = False

        query = f"SELECT clave FROM V_TA_SCRIPT WHERE clave='{code}'"
        result, error = get_customer_response(query, " al verificar existencia", True,self.TOKEN)
        if error:
            raise Exception("Ocurrió un error al obtener el reporte. Intente nuevamente más tarde.")

        if result:
            return True

        return False

    def __is_valid_code(self, code:str) -> bool:
        levels = Company.getPNLevels(self.TOKEN)

        is_valid = False
        for level in levels:
            if int(level['valor']) == len(code):
                is_valid = True
                break
        
        return is_valid

    def getAll(self):
        result =[]
        error = False

        query = "SELECT clave as code,grupo as name FROM V_TA_SCRIPT WHERE MARCA='CL' AND NOT CLAVE IS NULL ORDER BY CLAVE"
        result, error = get_customer_response(query, "Los reportes", True,self.TOKEN)
        if error:
            raise Exception("Ocurrió un error al obtener los reporte. Intente nuevamente más tarde.")

        
        return result
    
    def delete(self, code: str):
        result =[]
        error = False

        query = f"DELETE FROM V_TA_SCRIPT WHERE clave='{code}'"
        result, error = exec_customer_sql(query, " al eliminar", self.TOKEN,False)
        if error:
            Log.create(result)
            raise Exception("Ocurrió un error al eliminar el reporte. Intente nuevamente más tarde.")

        
        return result
    
    def get(self, code: str):
        result =[]
        error = False

        query = f"SELECT sql as query,clave as code,grupo as name,descripcion as comments,tabla as [table] FROM V_TA_SCRIPT WHERE clave='{code}'"
        result, error = get_customer_response(query, "El reporte", True,self.TOKEN)
        if error:
            raise Exception("Ocurrió un error al obtener el reporte. Intente nuevamente más tarde.")

        
        return result
    
    def getViews(self, code: str):
        result = []
        error = False

        query = f"SELECT sql as query,clave as code,grupo as name,descripcion as comments,tabla FROM V_TA_SCRIPT WHERE clave like '{code}%'"

        result, error = get_customer_response(query, "El reporte", True,self.TOKEN)
        if error:
            raise Exception("Ocurrió un error al obtener los reportes. Intente nuevamente más tarde.")

        return result
    
    def execute(self, code:str):
        result = self.get(code)

        error = False
        
        if not result:
            return []
        
        original_query = result[0].get('query').lower()
        
        if 'delete' in original_query or 'update' in original_query:
            raise Exception("No está permitido eliminar, ni actualizar. Revise la configuración del informe.")

        query,view = self.__normalize_query(result[0].get('query'))

        if view == "":
            raise Exception("Error al ejecutar la consulta. Revise la configuración del informe.")
            
        result, error = get_customer_response(query, "El reporte", True,self.TOKEN)
        self.__delete_view(view)

        if error:
            Log.create(f"Ocurrio un error al obtener el reporte {code}")
            raise Exception("Ocurrió un error al obtener el reporte. Intente nuevamente más tarde.")
        for row in result:
            for key, value in row.items():
                if isinstance(value, str):
                    value = value.strip()
                    if '.' in value or ',' in value:
                        try:
                            row[key] = float(value)
                        except ValueError:
                            pass
                    else:
                        row[key] = value
        
        return result
    
    def testExecute(self, query: str):        
        if not query.strip():
            raise Exception("La consulta no puede estar vacía.")

        query = self.__sanitize_query(query)
        
        query,view = self.__normalize_query(query=query)

        if view == "":
            raise Exception("Error al ejecutar la consulta. Revise la configuración del informe.")

        # Ejecutar la consulta
        result, error = get_customer_response(query, "La consulta de prueba", True, self.TOKEN)
        
        if error:
            Log.create(f"Ocurrió un error al ejecutar la consulta: {query}")
            raise Exception("Ocurrió un error al ejecutar la consulta. Intente nuevamente más tarde.")
        
        # Procesar los resultados si es necesario
        for row in result:
            for key, value in row.items():
                if isinstance(value, str):
                    value = value.strip()
                    if '.' in value or ',' in value:
                        try:
                            row[key] = float(value)
                        except ValueError:
                            pass
                    else:
                        row[key] = value

        return result
    
    def __sanitize_query(self, query: str) -> str:
        # Verifica si hay una instrucción maliciosa en la consulta
        dangerous_keywords = [
            'delete', 'update', 'drop', 'insert', 'alter', 'truncate', 'exec', 'execute', 'merge', 'call', 
            'create', 'replace', 'rename', 'shutdown'
        ]

        query_lower = query.lower()

        for keyword in dangerous_keywords:
            if keyword in query_lower:
                raise Exception(f"Operación no permitida: la consulta contiene una palabra peligrosa ('{keyword}'). Revise la configuración.")
        
        return query
    
    def __normalize_query(self, query:str) ->str:
        """Normaliza la query para obtener las fechas y numeros de manera correcta"""
        
        view = self.__create_view(query)
        
        if view == '':
            return query, ""

        result, error = get_customer_response(f"SELECT COLUMN_NAME, DATA_TYPE, ORDINAL_POSITION FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='{view}' ORDER BY ORDINAL_POSITION", "al obtener el detalle de las columnas", True,self.TOKEN)

        newField = ""
        excludeField = False

        if result:
            tmpQuery = ""
            for field in result:
                excludeField = False
                if field['DATA_TYPE'] == 'datetime':
                    #Fecha
                    newField = f" convert(NVARCHAR(18),isnull({field['COLUMN_NAME']},''),103) "
                elif field['DATA_TYPE'] == 'money' or field['DATA_TYPE'] == 'int' or field['DATA_TYPE'] == 'float' or field['DATA_TYPE'] == 'real':
                    #numero
                    newField = f" convert(varchar,convert(decimal(15,2),isnull({field['COLUMN_NAME']},0))) "
                elif field['DATA_TYPE'] == 'image':
                    excludeField = True
                else:
                    newField = f" isnull({field['COLUMN_NAME']},'') "
                
                if not excludeField:
                    tmpQuery =  tmpQuery + ("," if tmpQuery != '' else '') + newField + f" as '{str(field['ORDINAL_POSITION'] + 100)}_{field['COLUMN_NAME']}' "

            tmpQuery = "SELECT " + tmpQuery + f" FROM {view}"
        
        return tmpQuery, view

    def __create_view(self, query:str)->str:
        """Crea una vista para la query actual"""

        date = datetime.now()
        name_of_view = "temp_" + date.strftime("%d%m%y%H%M%S")

        query = f"CREATE VIEW {name_of_view} AS {query};"
        result,error = exec_customer_sql(query, "al crear la vista temporal", self.TOKEN)

        if error:
            Log.create(result)
            return ""
        return name_of_view

    def __delete_view(self, view:str):
        query = f"DROP VIEW {view}"
        result,error = exec_customer_sql(query, "al crear la vista temporal", self.TOKEN)

    @staticmethod
    def get_availables_tables(token:str):
        query = f"SELECT TABLE_NAME as name FROM INFORMATION_SCHEMA.COLUMNS GROUP BY TABLE_NAME ORDER BY TABLE_NAME"

        result, error = get_customer_response(query, "Las tablas", True, token)
        if error:
            raise Exception("Ocurrió un error al obtener las tablas disponibles. Intente nuevamente más tarde.")

        return result

    @staticmethod
    def get_fields_from_table(table_name:str, token:str):
        query = f"SELECT COLUMN_NAME as column_name,data_type FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='{table_name}' ORDER BY ORDINAL_POSITION"

        result, error = get_customer_response(query, "Los campos de la tabla", True, token)
        if error:
            raise Exception(f"Ocurrió un error al obtener las columnas de la tabla {table_name}. Intente nuevamente más tarde.")

        return result