# from datetime import datetime
from flask import request
from functions.general_customer import exec_customer_sql, get_customer_response, rgbint_to_hex
from functions.responses import set_response
from flask_classful import route
from ..master import MasterView
from rich import print
from functions.Log import Log


class RemittancesTransportView(MasterView):
    def post(self):
        data = request.get_json()

        account = data.get('account', '')
        travel = data.get('travel', '')
        status = data.get('status', '')
        observations = data.get('obs', '')

        # obtengo la base de TA_CONFIGURACION
        sql_db = "SELECT VALOR FROM TA_CONFIGURACION WHERE CLAVE = 'SAT_BASEDEDATOS'"

        db_result, error = get_customer_response(sql_db, " al obtener la base de datos", True, self.token_global)

        if error or not db_result or 'VALOR' not in db_result[0]:
            return set_response(None, 404, "Error al obtener la base de datos")

        base_db = db_result[0]['VALOR']

        if not base_db:
            return set_response(None, 500, "Error: Nombre de base de datos no válido.")

        sql = f"""
        USE {base_db};

        DECLARE @TMPNUMERO NVARCHAR(8);
        DECLARE @NUMERO NVARCHAR(8);
        DECLARE @ID INT;
        DECLARE @TIPOESTADO NVARCHAR(1);
        DECLARE @FECHAENT DATETIME = NULL;

        SELECT @TMPNUMERO = ISNULL(MAX(CAST(NUMERO_DOCUMENTO AS INT)), 0) + 1 
        FROM MV_RUTEOCAB
        WHERE TIPO_DOCUMENTO = 'RM' AND DOCUMENTO_SUCURSAL = '0001' AND DOCUMENTO_LETRA = 'R';

        SET @NUMERO = RIGHT(REPLICATE('0', 8) + CAST(@TMPNUMERO AS NVARCHAR(8)), 8);

        INSERT INTO MV_RUTEOCAB 
        (CLIENTE, ORDEN_DE_CARGA, NRO_VIAJE, FECHA_PROCESO, TIPO_DOCUMENTO, DOCUMENTO_SUCURSAL, NUMERO_DOCUMENTO, DOCUMENTO_LETRA, NRO_SALIDA, CODIGO_CLIENTE, SUCURSAL_CLIENTE, NOMBRE_CLIENTE, DOMICILIO, CODIGO_ESTADO)
        VALUES ('{account}', 1, '{travel}', CONVERT(NVARCHAR(10), GETDATE(), 103), 'RM', '0001', @NUMERO, 'R', 1, NULL, NULL, '', NULL, '{status}');

        SELECT TOP 1 @ID = ID 
        FROM MV_RUTEOCAB
        WHERE TIPO_DOCUMENTO = 'RM' AND DOCUMENTO_SUCURSAL = '0001' AND NUMERO_DOCUMENTO = @NUMERO AND DOCUMENTO_LETRA = 'R'
        -- ORDER BY ID DESC;

        SET @TIPOESTADO = (SELECT LTRIM(TIPO) FROM TA_ESTADOS WHERE LTRIM(CODIGO)=LTRIM('{status}'))
        IF @TIPOESTADO = 'O' SET @TIPOESTADO = ''''

        IF @TIPOESTADO = 'P' OR @TIPOESTADO = 'E'
		    SET @FECHAENT = GETDATE() 
        ELSE
            SET @FECHAENT = NULL

        INSERT INTO MV_SEGUIMIENTO (CLIENTE,NRO_VIAJE,TIPO_DOCUMENTO,NRO_COMPROBANTE,NRO_SALIDA,FECHA_HORA,COD_NOVEDAD,OBSERVACION)
	    VALUES ('{account}','{travel}','RM','0001' + @NUMERO + 'R',1,CAST(GETDATE() AS date), '{status}', '{observations}')

        UPDATE MV_RUTEOCAB
        SET Devolucion = @TIPOESTADO,
            PE = '1',
            PEDEV = '0',
            DEV_PESO_BRUTO = 0, 
            DEV_BULTOS = 0, 
            DEV_M3 = 0, 
            DEV_VD = 0, 
            DEV_CR = 0,
            FH_ENT_DESTINO = @FECHAENT
        WHERE ID = @ID;
        """

        Log.create(f"RUTINA: {sql}")

        results, error = exec_customer_sql(sql, " al dar de alta el remito", self.token_global, False, True, False)
        Log.create(f"RESULTADOS: {results}")

        if error:
            message = results[0]['message'] if results and 'message' in results[0] else "Error desconocido al ejecutar la consulta SQL."
            Log.create(f"ERROR SQL: {message}")
            response = set_response({"error": "true", "message": str(message)}, 500, "Error en la ejecución SQL")
        else:
            response = set_response(results, 200, "")

        return response




    @route('/pending/<string:driver>')
    def get_pending(self, driver: str):

        sql = f"""
        EXEC sp_web_getRemitosTransportePendientesChofer '{driver}'
        """

        results, error = get_customer_response(
            sql, " al obtener los remitos pendientes", True, self.token_global)

        for result in results:
            color = int(result.get("color_estado", '0'))
            result.update({"color_estado": rgbint_to_hex(
                color) if color > 0 else ''})

        response = set_response(results, 200 if not error else 404, "")
        return response

    @route("/driver/<string:driver>")
    def get_driver(self, driver: str):
        formatted_driver = driver.rjust(15)

        sql_db = """
            SELECT VALOR FROM TA_CONFIGURACION WHERE CLAVE = 'SAT_BASEDEDATOS'
        """

        db_result, error = get_customer_response(sql_db, " al obtener la base de datos", True, self.token_global)

        if error or not db_result or 'VALOR' not in db_result[0]:
            # Log.create(f"Error al obtener la base de datos: {db_result}")
            return set_response(None, 404, "Error al obtener la base de datos")

        base_db = db_result[0]['VALOR']

        if not base_db:
            # Log.create("El valor de base_db es inválido o vacío.")
            return set_response(None, 500, "Error: Nombre de base de datos no válido.")

        # 🔴 PRUEBA: Verificar si la base de datos es accesible
        # Log.create(f"Base de datos obtenida: {base_db}")

        sql = f"""
            SELECT * FROM {base_db}.dbo.MA_CHOFERES WHERE NRO_DOCUMENTO = '{formatted_driver}';
        """

        # Log.create(f"Ejecutando consulta: {sql}")

        results, error = get_customer_response(sql, " al obtener los datos del chofer", True, self.token_global)

        # 🔴 PRUEBA: Verificar si results tiene datos antes de retornarlo
        if error or results is None:
            Log.create(f"Error en consulta a MA_CHOFERES: {results}")
            return set_response(None, 404, "Error al obtener los datos del chofer.")

        # Log.create(f"Resultados obtenidos: {results}")

        response = set_response(results, 200 if results else 404, "")

        return response




    
    @route("/status")
    def index(self):
        """
        Retorna los posibles estados de los remitos de transporte
        """

        sql = "EXEC sp_web_getEstadosRemitosTransporte "

        results, error = get_customer_response(
            sql, " al obtener los estados de remitos de transporte", True, self.token_global)

        for result in results:
            result.update({"color": rgbint_to_hex(result.get("color", ''))})

        response = set_response(results, 200 if not error else 404, "")
        return response

    @route("/update_status", methods=['POST'])
    def update_status(self):
        """
        Actualiza el estado de los remitos de transporte
        """

        data = request.get_json()

        # print(data)

        id = data.get('id', '')
        observations = data.get('observations', '')
        status = data.get('status', '')

        sql = f"EXEC sp_web_ActualizaEstadoRemitoTransporte {id},'{status}','{observations}'"

        results, error = exec_customer_sql(
            sql, " al actualizar el estado de los remitos de transporte", self.token_global)

        response = set_response(results, 200 if not error else 404, "")
        return response
# class RemittancesStatusView(MasterView):
#     @route("/status")
#     def index(self):
#         """
#         Retorna los posibles estados de los remitos de transporte
#         """

#         sql = "EXEC sp_web_getEstadosRemitosTransporte "

#         results, error = get_customer_response(
#             sql, " al obtener los estados de remitos de transporte", True, self.token_global)

#         for result in results:
#             result.update({"color": rgbint_to_hex(result.get("color", ''))})

#         response = set_response(results, 200 if not error else 404, "")
#         return response
