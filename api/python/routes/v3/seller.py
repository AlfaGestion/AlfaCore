import json
from datetime import datetime

from flask import request
from flask_classful import route

from functions.Log import Log
from functions.general_customer import exec_customer_sql, get_customer_response
from functions.responses import set_response
from routes.v2.master import MasterView


class ViewSeller(MasterView):
    def _escape_sql_text(self, value):
        return str('' if value is None else value).replace("'", "''").strip()

    def _normalize_visit_date(self, raw_value: str):
        text = str(raw_value or '').strip()
        if not text:
            return None

        for fmt in (
            '%d/%m/%Y',
            '%d/%m/%Y %H:%M:%S',
            '%Y-%m-%d',
            '%Y-%m-%d %H:%M:%S',
            '%Y-%m-%dT%H:%M:%S',
        ):
            try:
                return datetime.strptime(text, fmt).strftime('%d/%m/%Y')
            except ValueError:
                continue

        return None

    def _format_log_value(self, value):
        try:
            return json.dumps(value, ensure_ascii=False, default=str, indent=2)
        except Exception:
            return repr(value)

    def _log_visit_send(
        self,
        index: int,
        visit,
        sql: str,
        message: str,
        type: str = "ERROR",
        stage: str = "",
        result=None,
        exception: Exception | None = None,
    ):
        id_cliente = self.code_account
        account = ""
        seller = ""
        raw_date = ""
        visited = ""
        if isinstance(visit, dict):
            account = visit.get("account", "")
            seller = visit.get("seller", "")
            raw_date = visit.get("date", "")
            visited = visit.get("visited", "")

        lines = [
            f"FECHA: {datetime.now().strftime('%d-%m-%Y %H:%M:%S')}",
            f"TIPO_ENVIO: VISITAS",
            f"ETAPA: {stage or type}",
            f"PATH: {request.path}",
            f"METODO: {request.method}",
            f"ID_CLIENTE: {id_cliente}",
            f"CUENTA: {account}",
            f"VENDEDOR: {seller}",
            f"FECHA_VISITA: {raw_date}",
            f"VISITADO: {visited}",
            f"VISITA_INDEX: {index}",
            f"MENSAJE: {message}",
            f"EXCEPCION_API: {repr(exception) if exception else ''}",
            "RESULTADO_SQL:",
            self._format_log_value(result),
            "VISITA:",
            self._format_log_value(visit),
            "INSERT_COMPLETO:",
            sql or "No se llego a generar el insert.",
        ]

        Log.create_v3_order("\n".join(lines), id_cliente, type, token=self.token_global)

    def _build_visits_sql(self, visits: list[dict]) -> str:
        statements = [
            """
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            DECLARE @NextNroMov INT;
            DECLARE @Updated INT = 0;
            DECLARE @Inserted INT = 0;
            DECLARE @Fecha date;
            DECLARE @IdComprobante NVARCHAR(100);

            BEGIN TRY
                BEGIN TRAN;

                SELECT @NextNroMov = ISNULL(MAX(NroMov), 0)
                FROM V_MV_STATUS WITH (TABLOCKX, HOLDLOCK);
            """
        ]

        for visit in visits:
            statements.append(
                f"""
                SET @Fecha = CONVERT(date, '{visit['date']}', 103);
                SET @IdComprobante = 'Vdor:{visit['seller']}|Vis:{visit['visited']}';

                UPDATE V_MV_STATUS
                SET
                    FechaHora = @Fecha,
                    Usuario = 'Vendedor App',
                    Observaciones = '{visit['obs']}'
                WHERE TC = 'VV'
                  AND IdComprobante = @IdComprobante
                  AND Cuenta = '{visit['account']}'
                  AND FechaHora >= @Fecha
                  AND FechaHora < DATEADD(day, 1, @Fecha);

                IF @@ROWCOUNT = 0
                BEGIN
                    SET @NextNroMov = @NextNroMov + 1;

                    INSERT INTO V_MV_STATUS
                    (
                        NroMov,
                        UNegocio,
                        TC,
                        IdComprobante,
                        IdTarea,
                        IdEstado,
                        FechaHora,
                        Usuario,
                        Cuenta,
                        Observaciones,
                        Secuencia,
                        IdTecnico
                    )
                    VALUES
                    (
                        @NextNroMov,
                        '   1',
                        'VV',
                        @IdComprobante,
                        '',
                        '',
                        @Fecha,
                        'Vendedor App',
                        '{visit['account']}',
                        '{visit['obs']}',
                        1,
                        ''
                    );

                    SET @Inserted = @Inserted + 1;
                END
                ELSE
                BEGIN
                    SET @Updated = @Updated + 1;
                END
                """
            )

        statements.append(
            """
                COMMIT TRAN;

                SELECT
                    CAST(11 AS INT) AS pRes,
                    CAST(@Inserted AS INT) AS inserted,
                    CAST(@Updated AS INT) AS updated,
                    CAST(N'Visitas grabadas correctamente.' AS NVARCHAR(250)) AS pMensaje;
            END TRY
            BEGIN CATCH
                IF XACT_STATE() <> 0
                    ROLLBACK TRAN;

                SELECT
                    CAST(CASE WHEN ERROR_NUMBER() IS NULL THEN 50000 ELSE ERROR_NUMBER() END AS INT) AS pRes,
                    CAST(0 AS INT) AS inserted,
                    CAST(0 AS INT) AS updated,
                    CAST(LEFT(ISNULL(ERROR_MESSAGE(), N'Error al grabar visitas.'), 250) AS NVARCHAR(250)) AS pMensaje;
            END CATCH
            """
        )

        return "\n".join(statements)

    def index(self):
        sql = f"""
        SELECT ltrim(idvendedor) as idvendedor, ltrim(nombre) as nombre,isnull(ltrim(clave),'1') as clave
        FROM V_TA_VENDEDORES
        """

        result, error = get_customer_response(sql, f" al obtener los vendedores", True, self.token_global)

        response = set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response

    @route('/config/<string:id_seller>')
    def get_config_seller(self, id_seller: str):
        query = f"""
        DECLARE @MODIFICA_CLASE_PRECIO NVARCHAR(2)
        DECLARE @VISUALIZA_CLIENTES NVARCHAR(2)
        DECLARE @MUESTRA_IMPORTES NVARCHAR(2)
        DECLARE @DESCUENTO_POR_ARTICULOS NVARCHAR(2)
        DECLARE @CONSULTA_STOCK_PEDIDOS NVARCHAR(2)
        DECLARE @CARGA_COBRANZAS NVARCHAR(2)
        DECLARE @PERMITE_VER_CTACTE NVARCHAR(2)
        DECLARE @PIDE_BULTOS_APP NVARCHAR(2)
        DECLARE @PIDE_PRECIO_APP NVARCHAR(2)

        DECLARE @DIRECCION NVARCHAR(100)
        DECLARE @NOMBRE NVARCHAR(100)
        DECLARE @TELEFONO NVARCHAR(50)
        DECLARE @EMAIL NVARCHAR(50)
        
        DECLARE @BLOQUEA_NP_STK_REAL_NEGATIVO NVARCHAR(2)
        DECLARE @BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO NVARCHAR(2)

        DECLARE @Ocultar_art NVARCHAR(2)
        DECLARE @Ocultar_tareas NVARCHAR(2)

        SET @PIDE_BULTOS_APP = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'PIDEBULTOSAPP')
        IF @PIDE_BULTOS_APP IS NULL OR @PIDE_BULTOS_APP = 'NO' SET @PIDE_BULTOS_APP = '0'
        IF @PIDE_BULTOS_APP = 'SI' SET @PIDE_BULTOS_APP = '1'

        SET @PIDE_PRECIO_APP = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'PIDEPRECIOAPP')
        IF @PIDE_PRECIO_APP IS NULL OR @PIDE_PRECIO_APP = 'NO' SET @PIDE_PRECIO_APP = '0'
        IF @PIDE_PRECIO_APP = 'SI' SET @PIDE_PRECIO_APP = '1'


        SET @BLOQUEA_NP_STK_REAL_NEGATIVO = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'BLOQUEA_NP_STK_REAL_NEGATIVO')
        IF @BLOQUEA_NP_STK_REAL_NEGATIVO IS NULL OR @BLOQUEA_NP_STK_REAL_NEGATIVO = 'NO' SET @BLOQUEA_NP_STK_REAL_NEGATIVO = '0'
        IF @BLOQUEA_NP_STK_REAL_NEGATIVO = 'SI' SET @BLOQUEA_NP_STK_REAL_NEGATIVO = '1'

        SET @BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO')
        IF @BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO IS NULL OR @BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO = 'NO' SET @BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO = '0'
        IF @BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO = 'SI' SET @BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO = '1'


        SET @MODIFICA_CLASE_PRECIO = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'VDOR_WEB_MODIFICA_CLASE_PRECIO_{id_seller}')
        IF @MODIFICA_CLASE_PRECIO IS NULL OR @MODIFICA_CLASE_PRECIO = 'NO' SET @MODIFICA_CLASE_PRECIO = '0'
        IF @MODIFICA_CLASE_PRECIO = 'SI' SET @MODIFICA_CLASE_PRECIO = '1'

        SET @VISUALIZA_CLIENTES = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'VDOR_WEB_VISUALIZA_CLIENTES_PROPIOS_{id_seller}')
        IF @VISUALIZA_CLIENTES IS NULL OR @VISUALIZA_CLIENTES = 'NO' SET @VISUALIZA_CLIENTES = '0'
        IF @VISUALIZA_CLIENTES = 'SI' SET @VISUALIZA_CLIENTES = '1'

        SET @MUESTRA_IMPORTES = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'VDOR_WEB_MOSTRAR_TOTALES_{id_seller}')
        IF @MUESTRA_IMPORTES IS NULL OR @MUESTRA_IMPORTES = 'NO' SET @MUESTRA_IMPORTES = '0'
        IF @MUESTRA_IMPORTES = 'SI' SET @MUESTRA_IMPORTES = '1'

        SET @DESCUENTO_POR_ARTICULOS = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'VDOR_WEB_DESCUENTO_POR_ARTICULO_{id_seller}')
        IF @DESCUENTO_POR_ARTICULOS IS NULL OR @DESCUENTO_POR_ARTICULOS = 'NO' SET @DESCUENTO_POR_ARTICULOS = '0'
        IF @DESCUENTO_POR_ARTICULOS = 'SI' SET @DESCUENTO_POR_ARTICULOS = '1'

        SET @CONSULTA_STOCK_PEDIDOS = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'VDOR_WEB_CONSULTA_STOCK_PEDIDOS_{id_seller}')
        IF @CONSULTA_STOCK_PEDIDOS IS NULL OR @CONSULTA_STOCK_PEDIDOS = 'NO' SET @CONSULTA_STOCK_PEDIDOS = '0'
        IF @CONSULTA_STOCK_PEDIDOS = 'SI' SET @CONSULTA_STOCK_PEDIDOS = '1'

        SET @CARGA_COBRANZAS = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'VDOR_WEB_PERMITE_COBRANZAS_{id_seller}')
        IF @CARGA_COBRANZAS IS NULL OR @CARGA_COBRANZAS = 'NO' SET @CARGA_COBRANZAS = '0'
        IF @CARGA_COBRANZAS = 'SI' SET @CARGA_COBRANZAS = '1'

        SET @PERMITE_VER_CTACTE = (SELECT ISNULL(VALOR,'0') FROM TA_CONFIGURACION WHERE CLAVE = 'VDOR_WEB_PERMITE_VERCTACTE_{id_seller}')
        IF @PERMITE_VER_CTACTE IS NULL OR @PERMITE_VER_CTACTE = 'NO' SET @PERMITE_VER_CTACTE = '0'
        IF @PERMITE_VER_CTACTE = 'SI' SET @PERMITE_VER_CTACTE = '1'

        SET @DIRECCION = (SELECT VALOR FROM TA_CONFIGURACION WHERE CLAVE ='CALLE')
        SET @DIRECCION = @DIRECCION + ' ' + (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE ='NUMERO')
        SET @DIRECCION = @DIRECCION + ', ' + (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE ='LOCALIDAD')
        SET @DIRECCION = @DIRECCION + ' (' + (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE ='CPOSTAL') + ')'
        SET @DIRECCION = @DIRECCION + ', ' + (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE ='PROVINCIA')

        SET @NOMBRE = (SELECT ISNULL(VALOR,'SU EMPRESA') FROM TA_CONFIGURACION WHERE CLAVE = 'NOMBRE')
        SET @TELEFONO = (SELECT ISNULL(VALOR,'.') FROM TA_CONFIGURACION WHERE CLAVE = 'TELEFONO')
        SET @EMAIL = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE = 'EMAIL_DE')

        SET @Ocultar_art = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE = 'VDOR_WEB_OCULTARART_{id_seller}')

        SET @Ocultar_tareas = (SELECT ISNULL(VALOR,'') FROM TA_CONFIGURACION WHERE CLAVE = 'VDOR_WEB_OCULTARTareas_{id_seller}')

        SELECT 'MODIFICA_CLASE_PRECIO' AS [key],@MODIFICA_CLASE_PRECIO as value
        UNION
        SELECT 'SOLO_CLIENTES_VENDEDOR' AS [key],@VISUALIZA_CLIENTES as value
        UNION
        SELECT 'MOSTRAR_TOTALES_PEDIDOS' AS [key],@MUESTRA_IMPORTES as value
        UNION
        SELECT 'DESCUENTO_POR_ARTICULO' AS [key],@DESCUENTO_POR_ARTICULOS as value
        UNION
        SELECT 'CONSULTA_STOCK_PEDIDOS' AS [key],@CONSULTA_STOCK_PEDIDOS as value
        UNION
        SELECT 'PERMITE_COBRANZAS' AS [key],@CARGA_COBRANZAS as value
        UNION
        SELECT 'PERMITE_VER_CTACTE' AS [key],@PERMITE_VER_CTACTE as value
        UNION
        SELECT 'EMP_DOMICILIO' as [key], @DIRECCION as value
        UNION
        SELECT 'EMP_NOMBRE' as [key], @NOMBRE as value
        UNION
        SELECT 'EMP_TELEFONO' as [key], @TELEFONO as value
        UNION
        SELECT 'EMP_EMAIL' as [key], @EMAIL as value

        UNION
        SELECT 'BLOQUEA_STK_REAL_NEGATIVO' as [key], @BLOQUEA_NP_STK_REAL_NEGATIVO as value
        UNION
        SELECT 'BLOQUEA_STK_COMPROMETIDO_NEGATIVO' as [key], @BLOQUEA_NP_STK_COMPROMETIDO_NEGATIVO as value
        UNION

        SELECT 'PIDE_BULTOS' as [key], @PIDE_BULTOS_APP as value
        UNION
        SELECT 'PIDE_PRECIO' as [key], @PIDE_PRECIO_APP as value
        UNION
        SELECT 'VDOR_WEB_OCULTARART' as [key], @Ocultar_art as value     
        UNION   
        SELECT 'VDOR_WEB_OCULTARTareas' as [key], @Ocultar_tareas as value        

        """

        if id_seller:
            response = self.get_response(query, f"OcurriÃ³ un error al obtener la configuraciÃ³n del vendedor", True, False)
        else:
            response = []

        return set_response(response, 200)

    @route('/visitas/<string:id>/<int:dia>')
    def get_visitas_vendedor(self, id: str, dia: int):
        dia = 1 if dia == 0 else dia

        sql = f"""
        SELECT a.cliente, a.observaciones, b.razon_social as nombre,isnull(b.calle,'') as calle,b.numero,b.localidad, SUBSTRING(frecuencia,1,1) as lunes,SUBSTRING(frecuencia,2,1) as martes,SUBSTRING(frecuencia,3,1) as miercoles,
        SUBSTRING(frecuencia,4,1) as jueves, SUBSTRING(frecuencia,5,1) as viernes,SUBSTRING(frecuencia,6,1) as sabado,SUBSTRING(frecuencia,7,1) as domingo,isnull(a.orden,1) as orden
        FROM V_TA_FRECUENCIA_VDOR a LEFT JOIN Vt_Clientes b on a.Cliente = b.CODIGO
        WHERE SUBSTRING(frecuencia,{dia},1)=1 and ltrim(a.idVendedor)='{id}'
        """
        if id:
            result, error = get_customer_response(
                sql, f" al obtener las visitas del vendedor {id}", True, self.token_global)
        else:
            error = False
            result = []

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response

    @route('/visits', methods=['POST'])
    def set_visits(self):
        data = request.get_json(silent=True)
        if not isinstance(data, list) or len(data) == 0:
            return set_response(
                [],
                400,
                "Debe enviar una lista JSON de visitas en el body."
            )

        response = []
        normalized_visits = []

        for index, visit in enumerate(data, start=1):
            if not isinstance(visit, dict):
                self._log_visit_send(index, visit, "", "La visita no tiene un formato JSON valido.", stage="VALIDACION")
                return set_response(
                    [],
                    400,
                    f"La visita #{index} no tiene un formato JSON valido."
                )

            raw_date = visit.get("date")
            id_seller = self._escape_sql_text(visit.get("seller"))
            obs = self._escape_sql_text(visit.get("obs", ""))
            account = self._escape_sql_text(visit.get("account"))
            visited_raw = visit.get('visited')
            visited = self._escape_sql_text(visited_raw)

            missing_fields = []
            if not raw_date:
                missing_fields.append('date')
            if not id_seller:
                missing_fields.append('seller')
            if not account:
                missing_fields.append('account')
            if visited_raw is None or visited == '':
                missing_fields.append('visited')

            if missing_fields:
                self._log_visit_send(
                    index,
                    visit,
                    "",
                    f"Faltan campos obligatorios: {', '.join(missing_fields)}.",
                    stage="VALIDACION",
                )
                return set_response(
                    [],
                    400,
                    f"La visita #{index} no se pudo procesar porque faltan campos obligatorios: {', '.join(missing_fields)}."
                )

            normalized_date = self._normalize_visit_date(raw_date)
            if not normalized_date:
                self._log_visit_send(index, visit, "", f"Fecha invalida: {raw_date}.", stage="VALIDACION")
                return set_response(
                    [],
                    400,
                    f"La visita #{index} tiene una fecha invalida: {raw_date}."
                )

            normalized_visits.append({
                "index": index,
                "raw": visit,
                "seller": id_seller,
                "account": account,
                "visited": visited,
                "date": normalized_date,
                "obs": obs,
            })

        query = self._build_visits_sql(normalized_visits)

        result = []
        try:
            result, error = exec_customer_sql(
                query,
                " al grabar las visitas",
                self.token_global,
                True
            )
        except Exception as e:
            result = []
            error = True
            self._log_visit_send(
                0,
                {"count": len(normalized_visits), "visits": data},
                query,
                "Excepcion no controlada al grabar las visitas.",
                stage="EXCEPCION_API",
                exception=e,
            )

        if error or not result:
            message = "Error al grabar las visitas."
            if result and isinstance(result[0], dict):
                message = str(result[0].get("message", message))
            self._log_visit_send(
                0,
                {"count": len(normalized_visits), "visits": data},
                query,
                message,
                stage="ERROR_SQL",
                result=result,
            )
            self.log(f"Error al grabar visitas. Resultado: {result}")
            return set_response(
                [],
                500,
                "La API recibio las visitas, pero ocurrio un error al grabarlas en la base de datos del cliente."
            )

        result_row = result[0]
        result_code = int(result_row[0] or 0)
        inserted = int(result_row[1] or 0)
        updated = int(result_row[2] or 0)
        result_message = str(result_row[3] or "")

        if result_code != 11:
            self._log_visit_send(
                0,
                {"count": len(normalized_visits), "visits": data},
                query,
                result_message or "Error al grabar las visitas.",
                stage="CODIGO_SQL_ERROR",
                result=result,
            )
            return set_response(
                [],
                500,
                result_message or "La API recibio las visitas, pero ocurrio un error al grabarlas en la base de datos del cliente."
            )

        for visit in normalized_visits:
            response.append({
                "seller": visit["seller"],
                "account": visit["account"],
                "visited": visit["visited"],
                "date": visit["date"],
                "status": "ok",
            })

        return set_response(
            response,
            200,
            f"Visitas grabadas correctamente. Insertadas: {inserted}. Actualizadas: {updated}."
        )

    @route('/location/<string:id>')
    def get_location(self, id: str):

        sql = f"""
        SELECT top 1 id,lat,long,idvendedor,convert(NVARCHAR(18),fechahora,103) + ' ' + convert(NVARCHAR(18),fechahora,108) as fechahora 
        FROM S_TA_UBICACIONES_VENDEDOR 
        WHERE ltrim(idVendedor)='{id}' AND (lat <>'0.0' AND lat<>'0' AND lat<>'' AND not lat is null)
		ORDER BY fechahora DESC
        """

        result, error = get_customer_response(
            sql, f" al obtener la ubicaciones del vendedor {id}", True, self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response
