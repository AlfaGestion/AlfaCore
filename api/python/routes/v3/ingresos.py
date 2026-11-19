from functions.general_customer import get_customer_response, exec_customer_sql
from functions.responses import set_response
from routes.v2.master import MasterView
from flask_classful import route
from flask import request, jsonify
from functions.Log import Log
from datetime import datetime, date

class IngresosView(MasterView):
    def get(self):
        sql = """
        SELECT *
        FROM MV_INGRESOS
        WHERE format(Ingreso, 'yyyy-MM-dd') = format(getdate(),'yyyy-MM-dd')
        """
        
        # return set_response([], 200, sql)
        
        result, error = get_customer_response(sql, f" al obtener los ingresos del día.", True, self.token_global)

        response = set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])
        
        if error:
            self.log({result}, "ERROR")
        
        return jsonify(response)
    
    def post(self):
        data = request.get_json()
        Log.createIngreso(f"DEBUG POST: Inicio DNI={data.get('dni')} Patente={data.get('patente')}")

        # 1. Extracción y Limpieza de datos
        id_ingreso = data.get('id', 0)
        dni = data.get('dni', '')
        patente = data.get('patente', '')
        parcela = data.get('parcela', 0)
        ingreso = data.get('ingreso', '')
        egresar = data.get('egresar', False) 

        # Conversión de booleanos a 1/0
        amarre = 1 if data.get('amarre') else 0
        trekking = 1 if data.get('trekking') else 0
        kayak = 1 if data.get('kayak') else 0
        embarcado = 1 if data.get('embarcado') else 0
        anulado = 1 if data.get('anulado') else 0  
        adicional = 1 if data.get('adicional') else 0
        adicionalL = 1 if data.get('adicionalL') else 0
        Estacionamiento = 1 if data.get('Estacionamiento') else 0 

        import datetime
        egreso_real_actual = datetime.datetime.now().strftime('%d/%m/%Y %H:%M:%S')

        # 2. SQL Cliente (Update o Insert de datos básicos)
        sql_cliente = f"""
            IF NOT EXISTS(SELECT 1 FROM MV_INGRESOS_CLIENTES WHERE Dni='{dni}')
                INSERT INTO MV_INGRESOS_CLIENTES (ApellidoNombre, Dni, Nacionalidad, Direccion, ModeloVehiculo, Ciudad, Patente, Telefono)
                VALUES ('{data.get('apellido_nombre','')}', '{dni}', '{data.get('nacionalidad','')}', '{data.get('direccion','')}', '{data.get('modelo_vehiculo','')}', '{data.get('ciudad','')}', '{patente}', '{data.get('telefono','')}');
            ELSE
                UPDATE MV_INGRESOS_CLIENTES SET 
                    ApellidoNombre = '{data.get('apellido_nombre','')}', Nacionalidad = '{data.get('nacionalidad','')}', Direccion = '{data.get('direccion','')}', 
                    ModeloVehiculo = '{data.get('modelo_vehiculo','')}', Ciudad = '{data.get('ciudad','')}', Patente = '{patente}', Telefono = '{data.get('telefono','')}' 
                WHERE Dni = '{dni}';
        """

        # 3. SQL Movimiento (Lógica: Si existe actualiza, si no existe crea)
        campo_egreso_real = f", EgresoReal = '{egreso_real_actual}'" if egresar else ""
        mensaje_exito = "Operación realizada con éxito."
        sql_movimiento = f"""
            UPDATE MV_INGRESOS SET 
                ApellidoNombre = '{data.get('apellido_nombre','')}', Egreso = '{data.get('egreso','')}', 
                Observaciones = '{data.get('observaciones','')}', Adultos = {data.get('adultos',0)}, PrecioAdultos = {data.get('precio_adultos',0)}, 
                Menores = {data.get('menores',0)}, PrecioMenores = {data.get('precio_menores',0)}, Jubilados = {data.get('jubilados',0)}, 
                PrecioJubilados = {data.get('precio_jubilados',0)},
                AdultosL = {data.get('adultosL',0)}, PrecioAdultosL = {data.get('precio_adultosL',0)}, 
                MenoresL = {data.get('menoresL',0)}, PrecioMenoresL = {data.get('precio_menoresL',0)}, JubiladosL = {data.get('jubiladosL',0)}, 
                PrecioJubiladosL = {data.get('precio_jubiladosL',0)}, 
                BajadaLancha = {data.get('bajada_lancha',0)}, PrecioBajadaLancha = {data.get('precio_bajada_lancha',0)}, 
                BajadaLanchaL = {data.get('bajada_lanchaL',0)}, PrecioBajadaLanchaL = {data.get('precio_bajada_lanchaL',0)},
                Adicional = {adicional}, PrecioAdicional = {data.get('precio_adicional',0)},
                AdicionalL = {adicionalL}, PrecioAdicionalL = {data.get('precio_adicionalL',0)},    
                MedioDePago = '{data.get('medio_de_pago','')}',
                Amarre = {amarre}, Trekking = {trekking}, Kayak = {kayak}, Embarcado = {embarcado}, 
                SubTotal = {data.get('subtotal',0)}, Total = {data.get('total',0)}, Dto = {data.get('descuento',0)}, 
                ModeloVehiculo = '{data.get('modelo_vehiculo','')}', Patente = '{patente}', Direccion = '{data.get('direccion','')}', 
                Ciudad = '{data.get('ciudad','')}', Nacionalidad = '{data.get('nacionalidad','')}', Anulada = {anulado}
                {campo_egreso_real},
                Estacionamiento = {Estacionamiento}, PrecioEstacionamiento = {data.get('PrecioEstacionamiento',0)}
            WHERE Dni = '{dni}' AND Patente = '{patente}' AND Parcela = {parcela} AND Ingreso = '{ingreso}' AND ISNULL(Finalizada, 0) <> 1;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO MV_INGRESOS (
                    ApellidoNombre, Ingreso, Egreso, Observaciones, Parcela, Dni, 
                    Nacionalidad, Direccion, ModeloVehiculo, Ciudad, Patente, 
                    MedioDePago, Amarre, Trekking, Kayak, Embarcado, SubTotal, Total, Dto,
                    Adultos, PrecioAdultos, Menores, PrecioMenores, Jubilados, PrecioJubilados,
                    AdultosL, PrecioAdultosL, MenoresL, PrecioMenoresL, JubiladosL, PrecioJubiladosL, 
                    BajadaLanchaL, PrecioBajadaLanchaL, BajadaLancha, PrecioBajadaLancha, 
                    Adicional, PrecioAdicional, AdicionalL, PrecioAdicionalL, Estacionamiento,PrecioEstacionamiento
                )
                VALUES (
                    '{data.get('apellido_nombre','')}', '{ingreso}', '{data.get('egreso','')}', '{data.get('observaciones','')}', {parcela}, '{dni}',
                    '{data.get('nacionalidad','')}', '{data.get('direccion','')}', '{data.get('modelo_vehiculo','')}', '{data.get('ciudad','')}', '{patente}', 
                    '{data.get('medio_de_pago','')}', {amarre}, {trekking}, {kayak}, {embarcado}, {data.get('subtotal',0)}, {data.get('total',0)}, {data.get('descuento',0)},
                    {data.get('adultos',0)}, {data.get('precio_adultos',0)}, {data.get('menores',0)}, {data.get('precio_menores',0)}, {data.get('jubilados',0)}, {data.get('precio_jubilados',0)},
                    {data.get('adultosL',0)}, {data.get('precio_adultosL',0)}, {data.get('menoresL',0)}, {data.get('precio_menoresL',0)}, {data.get('jubiladosL',0)}, {data.get('precio_jubiladosL',0)},
                    {data.get('bajada_lanchaL',0)}, {data.get('precio_bajada_lanchaL',0)}, {data.get('bajada_lancha',0)}, {data.get('precio_bajada_lancha',0)}, 
                    {adicional}, {data.get('precio_adicional',0)}, {adicionalL}, {data.get('precio_adicionalL',0)} ,
                    {Estacionamiento} , {data.get('PrecioEstacionamiento',0)}
                );
            END
        """

        # 4. Ejecución Final
        sql_final = sql_cliente + sql_movimiento
        result, error = exec_customer_sql(sql_final, "al procesar movimiento", self.token_global)

        if error:
            Log.createIngreso(f"DEBUG POST: Error SQL: {result} sql = {sql_final}")
            return set_response(None, 404, f"Error en base de datos: {result}")

        Log.createIngreso(f"DEBUG POST: Éxito para DNI {dni} sql = {sql_final}")
        return set_response({"status": "ok"}, 200, mensaje_exito)

    @route('/clientes', methods=['POST'])
    def upload_cliente(self):
        data = request.get_json()
    
        apellido_nombre = data.get('apellido_nombre', '')
        dni = data.get('dni', '')
        nacionalidad = data.get('nacionalidad', '')
        direccion = data.get('direccion', '')
        modelo_vehiculo = data.get('modelo_vehiculo', '')
        ciudad = data.get('ciudad', '')
        patente = data.get('patente', '')
        telefono = data.get('telefono', '')
        
        sql = f"""
        INSERT INTO MV_INGRESOS_CLIENTES (ApellidoNombre, Dni, Nacionalidad, Direccion, ModeloVehiculo, Ciudad, Patente, Telefono)
        VALUES
        ('{apellido_nombre}', '{dni}', '{nacionalidad}', '{direccion}', '{modelo_vehiculo}', '{ciudad}', '{patente}', '{telefono}')
        """

        # sql = "DELETE FROM MV_INGRESOS_CLIENTES"

        print(sql)
        # return set_response([], 200, "Cliente grabado con éxito.")
        
        result, error = exec_customer_sql(sql, "al grabar el cliente", self.token_global)

        if error:
            self.log({result}, "ERROR")
            return set_response(None, 404, f"Ocurrió un error al grabar el cliente: \n{result}.")
          
        return set_response(result, 200, "Cliente grabado con éxito.")


    @route('/ObtenerClientes', methods=['get'])
    def ObtenerClientes(self):
        sql = f"""
        SELECT TOP (1000) [Id]
        ,[ApellidoNombre]
        ,[Dni]
        ,[Nacionalidad]
        ,[Direccion]
        ,[ModeloVehiculo]
        ,[Ciudad]
        ,[Patente]
        FROM MV_Ingresos_Clientes
        """

        result, error = get_customer_response(sql, f" al obtener los precios .", True, self.token_global)
        response = set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])
        
        if error:
            self.log({result}, "ERROR")
        
        return response       
        
    @route('/clientes', methods=['GET'])
    def get_cliente_por_dni(self):
        dni = request.args.get('dni', '').strip()

        if not dni:
            return set_response([], 200, "")

        sql = f"""
        SELECT TOP (1) [Id]
            ,[ApellidoNombre]
            ,[Dni]
            ,[Nacionalidad]
            ,[Direccion]
            ,[ModeloVehiculo]
            ,[Ciudad]
            ,[Patente]
            ,[Telefono]
        FROM MV_Ingresos_Clientes
        WHERE Dni = '{dni}'
        """

        result, error = get_customer_response(sql, "al obtener cliente.", True, self.token_global)
        response = set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])

        if error:
            self.log({result}, "ERROR")

        return response


    @route('/precios', methods=['GET'])
    def get_precios(self):
                    
        sql = f"""
        SELECT CLAVE, VALOR
        FROM TA_CONFIGURACION
        WHERE CLAVE LIKE 'ING_%'
        """
        
        result, error = get_customer_response(sql, f" al obtener los precios .", True, self.token_global)
        response = set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])
        print(response) 
        if error:
            self.log({result}, "ERROR")
        
        return response

    @route('/medios_de_pago', methods=['GET'])
    def get_mp(self):
                    
        sql = f"""
        SELECT CodigoOpcional, DESCRIPCION 
        FROM MA_CUENTAS WHERE NOT MEDIODEPAGO IS NULL 
        AND MEDIODEPAGO <> '' 
        AND CodigoOpcional <> '' 
        ORDER BY DESCRIPCION 
        """
        
        result, error = get_customer_response(sql, f" al obtener los medios de pago .", True, self.token_global)
        response = set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])
        
        if error:
            self.log({result}, "ERROR")
        
        return response
    
    @route('/parcelas', methods=['GET'])
    def pacelas(self):
                    
        sql = f"""
        SELECT CLAVE, VALOR
        FROM TA_CONFIGURACION
        WHERE CLAVE = 'HOTEL_CANTHAB'
        """
        
        result, error = get_customer_response(sql, f" al obtener las parcelas.", True, self.token_global)
        response = set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])
        
        if error:
            self.log({result}, "ERROR")
        
        return response   
 
    @route('/ingresospendientes', methods=['GET'])
    def ingresospendientes(self):
        desde = request.args.get('desde', '').strip()
        hasta = request.args.get('hasta', '').strip()

        filtro_fecha = ""
        if desde and hasta:
            try:
                datetime.strptime(desde, '%Y-%m-%d')
                datetime.strptime(hasta, '%Y-%m-%d')
                filtro_fecha = f"WHERE Ingreso >= '{desde}' AND Ingreso <= '{hasta}'"
            except ValueError:
                from flask import Response, json
                error_response = set_response([], 400, "Formato de fecha invalido. Use YYYY-MM-DD")
                return Response(
                    json.dumps(error_response, ensure_ascii=False),
                    mimetype="application/json"
                )

        sql = f"""
        SELECT Ingreso,Egreso, ApellidoNombre, Parcela,Dni from vt_mv_ingresos_pendientes
        {filtro_fecha}
        """
        
        result, error = get_customer_response(sql, f" al obtener los ingresos pendientes.", True, self.token_global)
        if not error and isinstance(result, list):
            for row in result:
                if isinstance(row, dict):
                    for key, value in row.items():
                        if isinstance(value, (datetime, date)):
                            row[key] = value.isoformat()
        response = set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])

        from flask import Response, json
        #return jsonify(response)

        if error:
            self.log({result}, "ERROR")
        
        return Response(
            json.dumps(response, ensure_ascii=False),
            mimetype="application/json"
        )

    @route('/ingresospendientes/search', methods=['GET'])
    def ingresospendientes_search(self):
        search = request.args.get('search', '').strip()
        desde = request.args.get('desde', '').strip()
        hasta = request.args.get('hasta', '').strip()

        if not search:
            from flask import Response, json
            empty_response = set_response([], 200, "")
            return Response(
                json.dumps(empty_response, ensure_ascii=False),
                mimetype="application/json"
            )

        filtro_fecha = ""
        if desde and hasta:
            try:
                datetime.strptime(desde, '%Y-%m-%d')
                datetime.strptime(hasta, '%Y-%m-%d')
                filtro_fecha = f" AND Ingreso >= '{desde}' AND Ingreso <= '{hasta}'"
            except ValueError:
                from flask import Response, json
                error_response = set_response([], 400, "Formato de fecha invalido. Use YYYY-MM-DD")
                return Response(
                    json.dumps(error_response, ensure_ascii=False),
                    mimetype="application/json"
                )

        sql = f"""
        SELECT Ingreso, Egreso, ApellidoNombre, Parcela, Dni, Patente
        FROM vt_mv_ingresos_pendientes
        WHERE (ApellidoNombre LIKE '%{search}%'
           OR Dni LIKE '%{search}%'
           OR Patente LIKE '%{search}%'
           OR CAST(Parcela AS VARCHAR(50)) LIKE '%{search}%')
        {filtro_fecha}
        """

        result, error = get_customer_response(sql, f" al buscar ingresos pendientes.", True, self.token_global)
        if not error and isinstance(result, list):
            for row in result:
                if isinstance(row, dict):
                    for key, value in row.items():
                        if isinstance(value, (datetime, date)):
                            row[key] = value.isoformat()

        response = set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])

        from flask import Response, json
        if error:
            self.log({result}, "ERROR")

        return Response(
            json.dumps(response, ensure_ascii=False),
            mimetype="application/json"
        )

    @route('/estadisticas', methods=['GET'])
    def estadisticas(self):
        desde = request.args.get('desde', '').strip()
        hasta = request.args.get('hasta', '').strip()

        if not desde or not hasta:
            from flask import Response, json
            empty_response = set_response([], 400, "Faltan parametros desde/hasta")
            return Response(
                json.dumps(empty_response, ensure_ascii=False),
                mimetype="application/json"
            )

        try:
            datetime.strptime(desde, '%Y-%m-%d')
            datetime.strptime(hasta, '%Y-%m-%d')
        except ValueError:
            from flask import Response, json
            error_response = set_response([], 400, "Formato de fecha invalido. Use YYYY-MM-DD")
            return Response(
                json.dumps(error_response, ensure_ascii=False),
                mimetype="application/json"
            )
        sql = f"""
        SELECT
        SUM(ingresos_registros)   AS ingresos_registros,
        SUM(en_predio_registros)  AS en_predio_registros,
        SUM(egresos_registros)    AS egresos_registros,

        SUM(ingresaron) AS ingresaron,
        SUM(en_predio)  AS en_predio,
        SUM(egresaron)  AS egresaron,

        SUM(adultos)     AS adultos,
        SUM(adultos_l)   AS adultos_l,
        SUM(menores)     AS menores,
        SUM(menores_l)   AS menores_l,
        SUM(jubilados)   AS jubilados,
        SUM(jubilados_l) AS jubilados_l,

        SUM(estacionamientos) AS estacionamientos,
        SUM(motorhome)        AS motorhome,
        SUM(motorhome_l)      AS motorhome_l,
        SUM(bajada_lancha)    AS bajada_lancha,
        SUM(bajada_lancha_l)  AS bajada_lancha_l
        FROM dbo.vw_estadisticas_ingresos_diarias
        WHERE Fecha BETWEEN '{desde}' AND '{hasta}';
        """

        
        result, error = get_customer_response(sql, " al obtener estadisticas.", True, self.token_global)
        response = set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])

        from flask import Response, json

        if error:
            self.log({result}, "ERROR")

        return Response(
            json.dumps(response, ensure_ascii=False),
            mimetype="application/json"
        )

    @route('/MediosDePagosEstadisticas', methods=['GET'])
    def medios_de_pagos_estadisticas(self):
        desde = request.args.get('desde', '').strip()
        hasta = request.args.get('hasta', '').strip()

        if not desde or not hasta:
            from flask import Response, json
            empty_response = set_response([], 400, "Faltan parametros desde/hasta")
            return Response(
                json.dumps(empty_response, ensure_ascii=False),
                mimetype="application/json"
            )

        try:
            datetime.strptime(desde, '%Y-%m-%d')
            datetime.strptime(hasta, '%Y-%m-%d')
        except ValueError:
            from flask import Response, json
            error_response = set_response([], 400, "Formato de fecha invalido. Use YYYY-MM-DD")
            return Response(
                json.dumps(error_response, ensure_ascii=False),
                mimetype="application/json"
            )

        sql = f"""
        SET DATEFORMAT YMD;
        SELECT
          mp.MedioDePago,
          CASE
            WHEN mp.MedioDePago IS NULL OR mp.MedioDePago = 'CXP' THEN 'CUENTA CORRIENTE'
            ELSE c.Descripcion
          END AS Descripcion,
          COUNT(*) AS cantidad,
          SUM(mp.total) AS total
        FROM (
          SELECT
            NULLIF(LTRIM(RTRIM(i.MedioDePago)), '') AS MedioDePago,
            i.total,
            i.Anulada,
            i.Ingreso
          FROM MV_Ingresos i
        ) mp
        LEFT JOIN MA_CUENTAS c
          ON c.CodigoOpcional = mp.MedioDePago
          AND c.CodigoOpcional IS NOT NULL
        WHERE mp.Ingreso >= '{desde}'
          AND mp.Ingreso <= '{hasta}'
          AND mp.Anulada = 0
        GROUP BY
          mp.MedioDePago,
          CASE WHEN mp.MedioDePago IS NULL OR mp.MedioDePago = 'CXP' THEN 'CUENTA CORRIENTE' ELSE c.Descripcion END
        ORDER BY total DESC;
        """
        print(sql)
        result, error = get_customer_response(sql, " al obtener estadisticas de medios de pago.", True, self.token_global)
        response = set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])

        from flask import Response, json

        if error:
            self.log({result}, "ERROR")

        if not error and isinstance(result, list):
            for row in result:
                if isinstance(row, dict):
                    for key, value in row.items():
                        if isinstance(value, (datetime, date)):
                            row[key] = value.isoformat()
                        elif type(value).__name__ == "Decimal":
                            try:
                                row[key] = float(value)
                            except Exception:
                                row[key] = str(value)

        return Response(
            json.dumps(response, ensure_ascii=False),
            mimetype="application/json"
        )
