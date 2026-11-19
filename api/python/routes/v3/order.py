import hashlib
import json
from datetime import datetime
from decimal import Decimal, InvalidOperation

from flask import request
from flask_classful import route

from functions.Log import Log
from functions.general_customer import exec_customer_sql, get_customer_response
from functions.responses import set_response
from routes.v2.master import MasterView


class ViewOrder(MasterView):
    MARKER_LEN = 20
    LOCK_TIMEOUT_MS = 15000

    def _escape_sql_text(self, value, max_len: int | None = None) -> str:
        text = "" if value is None else str(value).strip()
        if max_len is not None:
            text = text[:max_len]
        return text.replace("'", "''")

    def _numeric_sql_literal(self, value, default: str = "0") -> str:
        if value in (None, "", "None"):
            return default

        text = str(value).strip().replace(",", ".")
        try:
            return format(Decimal(text), "f")
        except (InvalidOperation, ValueError):
            return default

    def _normalize_date_only(self, value) -> str:
        if not value:
            return datetime.now().strftime("%d/%m/%Y")

        if isinstance(value, datetime):
            return value.strftime("%d/%m/%Y")

        s = str(value).strip()
        for fmt in [
            "%d/%m/%Y %H:%M:%S.%f",
            "%d/%m/%Y %H:%M:%S",
            "%d/%m/%Y %H:%M",
            "%Y-%m-%d %H:%M:%S.%f",
            "%Y-%m-%d %H:%M:%S",
            "%Y-%m-%d %H:%M",
            "%Y-%m-%dT%H:%M:%S.%f",
            "%Y-%m-%dT%H:%M:%S",
            "%Y%m%d%H%M%S",
            "%Y%m%d",
        ]:
            try:
                dt = datetime.strptime(s, fmt)
                return dt.strftime("%d/%m/%Y")
            except ValueError:
                pass

        for fmt in ["%d/%m/%Y", "%Y-%m-%d"]:
            try:
                dt = datetime.strptime(s, fmt)
                return dt.strftime("%d/%m/%Y")
            except ValueError:
                pass

        return datetime.now().strftime("%d/%m/%Y")

    def _normalize_datetime(self, value) -> str:
        if not value:
            return datetime.now().strftime("%Y-%m-%d %H:%M:%S")

        if isinstance(value, datetime):
            return value.strftime("%Y-%m-%d %H:%M:%S")

        s = str(value).strip()
        for fmt in [
            "%d/%m/%Y %H:%M:%S.%f",
            "%d/%m/%Y %H:%M:%S",
            "%d/%m/%Y %H:%M",
            "%Y-%m-%d %H:%M:%S.%f",
            "%Y-%m-%d %H:%M:%S",
            "%Y-%m-%d %H:%M",
            "%Y-%m-%dT%H:%M:%S.%f",
            "%Y-%m-%dT%H:%M:%S",
            "%Y%m%d%H%M%S",
            "%Y%m%d",
        ]:
            try:
                dt = datetime.strptime(s, fmt)
                return dt.strftime("%Y-%m-%d %H:%M:%S")
            except ValueError:
                pass

        for fmt in ["%d/%m/%Y", "%Y-%m-%d"]:
            try:
                dt = datetime.strptime(s, fmt)
                return dt.strftime("%Y-%m-%d 00:00:00")
            except ValueError:
                pass

        return datetime.now().strftime("%Y-%m-%d %H:%M:%S")

    def _extract_order_marker_seed(self, order: dict, seller: str) -> str:
        transporte_nombre = (
            order.get("TRANSPORTE_NOMBRE", "")
            or order.get("transporte_nombre", "")
            or order.get("transporteNombre", "")
        )
        if transporte_nombre:
            return str(transporte_nombre).strip()

        external_id = order.get("externalId", "") or order.get("external_id", "") or order.get("id", "")
        if not external_id:
            return ""

        seller_tag = (seller or "").strip()
        external_id = str(external_id).strip()
        if seller_tag and not external_id.startswith(f"{seller_tag}-"):
            return f"{seller_tag}-{external_id}"
        return external_id

    def _build_order_marker(self, marker_seed: str, tc: str, account: str) -> str:
        marker_seed = (marker_seed or "").strip()
        if not marker_seed:
            return ""

        raw = f"{(tc or 'NP').strip().upper()}|{(account or '').strip()}|{marker_seed}"
        digest = hashlib.sha1(raw.encode("utf-8")).hexdigest().upper()
        return f"OD{digest[: self.MARKER_LEN - 2]}"

    def _normalize_sale_condition(self, sale_condition: str) -> str:
        value = str(sale_condition or "").strip()
        normalized = value.lower()
        if normalized == "contado":
            return "   1"
        if normalized == "ctacte":
            return "  10"
        if not value:
            return ""
        return value.rjust(4)[:4]

    def _normalize_invoice_type(self, tc_invoice: str) -> str:
        value = (tc_invoice or "").strip().lower()
        if value == "fp":
            return "Proforma"
        if value == "fc":
            return "Factura"
        return tc_invoice or ""

    def _build_items_sql(self, items: list[dict]) -> str:
        statements = []
        for item in items or []:
            product = self._escape_sql_text(item.get("product", ""))
            quantity = self._numeric_sql_literal(item.get("quantity", 0))
            amount = self._numeric_sql_literal(item.get("amount", 0))
            dto = self._numeric_sql_literal(item.get("dto", 0))
            bultos = self._numeric_sql_literal(item.get("bultos", 0))

            statements.append(
                f"""
                SET @pResItem = NULL;
                SET @pMensajeItem = NULL;
                SET @pIdCpteItem = NULL;
                EXEC sp_web_CpteInsumosV2
                    @pIdCpte,
                    '{product}',
                    {quantity},
                    {bultos},
                    {amount},
                    {dto},
                    @pResItem OUTPUT,
                    @pMensajeItem OUTPUT,
                    @pIdCpteItem OUTPUT;

                IF ISNULL(@pResItem, 0) <> 11
                BEGIN
                    SET @pMensajeItem = LEFT(ISNULL(@pMensajeItem, N'No se pudo grabar el detalle del pedido.'), 250);
                    RAISERROR (@pMensajeItem, 16, 1);
                END
                """
            )

        return "\n".join(statements)

    def _build_order_sql(
        self,
        account: str,
        seller: str,
        date: str,
        date_time: str,
        obs: str,
        lat: str,
        lng: str,
        tc: str,
        marker: str,
        sale_condition: str,
        tc_invoice: str,
        seller_name: str,
        device_model: str,
        items: list[dict],
    ) -> str:
        safe_account = self._escape_sql_text(account)
        safe_seller = self._escape_sql_text(seller)
        safe_date = self._escape_sql_text(date)
        safe_date_time = self._escape_sql_text(date_time)
        safe_obs = self._escape_sql_text(obs, 250)
        safe_lat = self._escape_sql_text(lat)
        safe_lng = self._escape_sql_text(lng)
        safe_tc = self._escape_sql_text((tc or "NP").strip().upper())
        safe_marker = self._escape_sql_text(marker, self.MARKER_LEN)
        safe_seller_name = self._escape_sql_text(seller_name, 255)
        safe_device_model = self._escape_sql_text(device_model, 255)
        safe_lock_resource = self._escape_sql_text(f"ALFAGO_ORDER|{safe_tc}|{safe_account}|{marker}", 255)

        normalized_sale_condition = self._normalize_sale_condition(sale_condition).replace("'", "''")
        normalized_tc_invoice = self._escape_sql_text(self._normalize_invoice_type(tc_invoice))

        raw_lat = (lat or "").strip()
        raw_lng = (lng or "").strip()
        has_coords = raw_lat not in ("", "0", "0.0") and raw_lng not in ("", "0", "0.0")
        safe_action_lat = self._escape_sql_text(raw_lat if has_coords else "SIN_PERMISO")
        safe_action_lng = self._escape_sql_text(raw_lng if has_coords else "")

        items_sql = self._build_items_sql(items)

        sale_condition_sql = ""
        if normalized_sale_condition or normalized_tc_invoice:
            if normalized_sale_condition:
                sale_condition_sql = f"""
                UPDATE V_MV_CPTE
                SET IDCOND_CPRA_VTA = '{normalized_sale_condition}',
                    comentarios = '{normalized_tc_invoice}'
                WHERE ID = @pIdCpte;
                """
            else:
                sale_condition_sql = f"""
                UPDATE V_MV_CPTE
                SET comentarios = '{normalized_tc_invoice}'
                WHERE ID = @pIdCpte;
                """

        coords_sql = ""
        if has_coords:
            coords_sql = f"""
            UPDATE MA_CUENTASADIC
            SET X = '{safe_lat}',
                Y = '{safe_lng}'
            WHERE CODIGO = '{safe_account}';
            """

        return f"""
        SET NOCOUNT ON;
        SET XACT_ABORT ON;

        DECLARE @pRes INT = 0;
        DECLARE @pMensaje NVARCHAR(250) = N'';
        DECLARE @pIdCpte INT = 0;
        DECLARE @pResItem INT = 0;
        DECLARE @pMensajeItem NVARCHAR(250) = N'';
        DECLARE @pIdCpteItem INT = 0;
        DECLARE @LockResult INT = 0;
        DECLARE @ExistingId INT = NULL;
        DECLARE @IdComprobante NVARCHAR(50) = NULL;
        DECLARE @FechaGrabacion DATETIME = NULL;
        DECLARE @Usuario NVARCHAR(255) = NULL;

        BEGIN TRY
            EXEC @LockResult = sp_getapplock
                @Resource = '{safe_lock_resource}',
                @LockMode = 'Exclusive',
                @LockOwner = 'Session',
                @LockTimeout = {self.LOCK_TIMEOUT_MS};

            IF @LockResult < 0
            BEGIN
                SET @pMensaje = N'No se pudo obtener el bloqueo para validar el pedido. Intente nuevamente.';
                RAISERROR (@pMensaje, 16, 1);
            END

            SELECT TOP 1 @ExistingId = ID
            FROM V_MV_CPTE WITH (UPDLOCK, HOLDLOCK)
            WHERE TC = '{safe_tc}'
              AND CUENTA = '{safe_account}'
              AND TRANSPORTE_NOMBRE = '{safe_marker}';

            IF @ExistingId IS NOT NULL
            BEGIN
                EXEC sp_releaseapplock
                    @Resource = '{safe_lock_resource}',
                    @LockOwner = 'Session';

                SELECT
                    CAST(11 AS INT) AS pRes,
                    CAST(N'Pedido ya procesado.' AS NVARCHAR(250)) AS pMensaje,
                    @ExistingId AS pIdCpte,
                    CAST(1 AS BIT) AS pDuplicado;
                RETURN;
            END

            EXEC sp_web_V_MV_CPTE
                '{safe_account}',
                '{safe_seller}',
                '{safe_date}',
                '{safe_obs}',
                '{safe_lat}',
                '{safe_lng}',
                '{safe_tc}',
                @pRes OUTPUT,
                @pMensaje OUTPUT,
                @pIdCpte OUTPUT;

            IF ISNULL(@pRes, 0) <> 11
            BEGIN
                SET @pMensaje = LEFT(ISNULL(@pMensaje, N'No se pudo grabar el pedido.'), 250);
                RAISERROR (@pMensaje, 16, 1);
            END

            UPDATE V_MV_CPTE
            SET FECHAHORA_GRABACION = CASE
                    WHEN ISDATE('{safe_date_time}') = 1 THEN CONVERT(DATETIME, '{safe_date_time}', 121)
                    ELSE GETDATE()
                END,
                TRANSPORTE_NOMBRE = '{safe_marker}'
            WHERE ID = @pIdCpte;

            {sale_condition_sql}

            SELECT
                @IdComprobante = CAST(IDCOMPROBANTE AS NVARCHAR(50)),
                @FechaGrabacion = FECHAHORA_GRABACION
            FROM V_MV_CPTE
            WHERE ID = @pIdCpte;

            SET @Usuario = LTRIM(RTRIM('{safe_seller}'));
            IF @Usuario <> ''
            BEGIN
                DECLARE @SellerName NVARCHAR(255) = LTRIM(RTRIM('{safe_seller_name}'));
                IF @SellerName = ''
                BEGIN
                    SELECT TOP 1 @SellerName = LTRIM(nombre)
                    FROM V_TA_VENDEDORES
                    WHERE LTRIM(idvendedor) = LTRIM('{safe_seller}');
                END

                IF ISNULL(@SellerName, '') <> ''
                    SET @Usuario = LEFT(@Usuario + ' - ' + @SellerName, 255);
            END

            INSERT INTO V_MV_CPTEACCIONES
            (
                TC,
                IDCOMPROBANTE,
                IDCOMPLEMENTO,
                TIPO_ACCION,
                FECHAHORA,
                USUARIO,
                PC,
                SYSTEMUSER,
                CUENTA,
                PROCESO,
                PROCESOLOTE,
                FECHA
            )
            VALUES
            (
                '{safe_tc}',
                ISNULL(@IdComprobante, CAST(@pIdCpte AS NVARCHAR(50))),
                0,
                'UB',
                ISNULL(@FechaGrabacion, GETDATE()),
                LEFT(ISNULL(@Usuario, ''), 255),
                LEFT('{safe_device_model}', 255),
                'APP AlfaGo - Ubicacion actual',
                '{safe_account}',
                '{safe_action_lat}',
                '{safe_action_lng}',
                CASE
                    WHEN ISDATE('{safe_date}') = 1 THEN CONVERT(DATETIME, '{safe_date}', 103)
                    ELSE GETDATE()
                END
            );

            {coords_sql}

            {items_sql}

            EXEC sp_releaseapplock
                @Resource = '{safe_lock_resource}',
                @LockOwner = 'Session';

            SELECT
                @pRes AS pRes,
                CAST(LEFT(ISNULL(@pMensaje, N'Pedido grabado correctamente.'), 250) AS NVARCHAR(250)) AS pMensaje,
                @pIdCpte AS pIdCpte,
                CAST(0 AS BIT) AS pDuplicado;
        END TRY
        BEGIN CATCH
            IF XACT_STATE() <> 0 OR @@TRANCOUNT > 0
            BEGIN
                ROLLBACK TRAN;
            END

            EXEC sp_releaseapplock
                @Resource = '{safe_lock_resource}',
                @LockOwner = 'Session';

            SELECT
                CAST(CASE WHEN ERROR_NUMBER() IS NULL THEN 50000 ELSE ERROR_NUMBER() END AS INT) AS pRes,
                CAST(LEFT(ISNULL(ERROR_MESSAGE(), N'Error al grabar el pedido.'), 250) AS NVARCHAR(250)) AS pMensaje,
                CAST(ISNULL(@pIdCpte, 0) AS INT) AS pIdCpte,
                CAST(0 AS BIT) AS pDuplicado;
        END CATCH
        """

    def _format_log_value(self, value):
        try:
            return json.dumps(value, ensure_ascii=False, default=str, indent=2)
        except Exception:
            return repr(value)

    def _log_failed_order(
        self,
        index: int,
        order: dict,
        sql: str,
        message: str,
        stage: str = "",
        result=None,
        exception: Exception | None = None,
    ):
        id_cliente = self.code_account
        account = order.get("account", "")
        seller = order.get("seller", "")
        date = order.get("date", "")
        tc = order.get("tc", "NP")
        external_id = order.get("externalId", "") or order.get("external_id", "") or order.get("id", "")
        marker_seed = self._extract_order_marker_seed(order, order.get("seller", ""))

        lines = [
            f"FECHA: {datetime.now().strftime('%d-%m-%Y %H:%M:%S')}",
            f"TIPO_ENVIO: PEDIDOS",
            f"ETAPA: {stage or 'ERROR'}",
            f"PATH: {request.path}",
            f"METODO: {request.method}",
            f"ID_CLIENTE: {id_cliente}",
            f"CUENTA: {account}",
            f"VENDEDOR: {seller}",
            f"FECHA_PEDIDO: {date}",
            f"TC: {tc}",
            f"PEDIDO_INDEX: {index}",
            f"EXTERNAL_ID: {external_id}",
            f"MARKER_SEED: {marker_seed}",
            f"MENSAJE: {message}",
            f"EXCEPCION_API: {repr(exception) if exception else ''}",
            "RESULTADO_SQL:",
            self._format_log_value(result),
            "PEDIDO:",
            self._format_log_value(order),
            "INSERT_COMPLETO:",
            sql or "No se llego a generar el insert.",
        ]

        Log.create_v3_order("\n".join(lines), id_cliente, "ERROR", token=self.token_global)

    def post(self):
        orders = request.get_json(silent=True)
        if not isinstance(orders, list) or not orders:
            return set_response(None, 400, "Debe enviar una lista JSON de pedidos en el body.")

        inserted_count = 0
        duplicate_count = 0

        for index, order in enumerate(orders, start=1):
            if not isinstance(order, dict):
                return set_response(None, 400, f"El pedido #{index} no tiene un formato JSON valido.")

            account = order.get("account", "")
            if not str(account or "").strip() or str(account).strip().lower() == "none":
                self._log_failed_order(
                    index,
                    order,
                    "",
                    "El pedido no tiene cuenta de cliente. No se puede generar el comprobante.",
                    stage="VALIDACION_CUENTA",
                )
                return set_response(None, 400, "El pedido no tiene cuenta de cliente. No se puede generar el comprobante.")

            date_raw = order.get("date", datetime.now().strftime("%d/%m/%Y"))
            date = self._normalize_date_only(date_raw)
            date_time = self._normalize_datetime(date_raw)
            seller = order.get("seller", "")
            lat = str(order.get("lat", "0") or "0")
            lng = str(order.get("lng", "0") or "0")
            tc_invoice = order.get("type", "")
            obs = order.get("obs", "")
            sale_condition = order.get("condition", "")
            tc = (order.get("tc", "NP") or "NP").strip().upper()
            seller_name = order.get("sellerName", "") or ""
            device_model = order.get("deviceModel", "") or ""

            marker_seed = self._extract_order_marker_seed(order, seller)
            if not marker_seed:
                self._log_failed_order(
                    index,
                    order,
                    "",
                    "Cada pedido debe enviar externalId o transporteNombre para poder evitar duplicados.",
                    stage="VALIDACION",
                )
                return set_response(
                    None,
                    400,
                    "Cada pedido debe enviar externalId o transporteNombre para poder evitar duplicados.",
                )

            marker = self._build_order_marker(marker_seed, tc, account)

            sql = self._build_order_sql(
                account=account,
                seller=seller,
                date=date,
                date_time=date_time,
                obs=obs,
                lat=lat,
                lng=lng,
                tc=tc,
                marker=marker,
                sale_condition=sale_condition,
                tc_invoice=tc_invoice,
                seller_name=seller_name,
                device_model=device_model,
                items=order.get("items", []),
            )

            result = []
            try:
                result, error = exec_customer_sql(sql, " al grabar los pedidos", self.token_global, True)
            except Exception as e:
                error = True
                self._log_failed_order(
                    index,
                    order,
                    sql,
                    "Excepcion no controlada al grabar el pedido.",
                    stage="EXCEPCION_API",
                    exception=e,
                )

            if error or not result:
                message = "Ocurrio un error al grabar el pedido."
                if result and isinstance(result[0], dict):
                    message = str(result[0].get("message", message))
                self._log_failed_order(index, order, sql, message, stage="ERROR_SQL", result=result)
                self.log(f"{message}\nSENTENCIA : {sql}")
                return set_response(None, 404, "Ocurrio un error al grabar el pedido.")

            try:
                result_row = result[0]
                result_code = int(result_row[0])
                result_message = str(result_row[1] or "")
                result_id_invoice = int(result_row[2] or 0)
                is_duplicate = bool(result_row[3])
            except Exception as e:
                message = f"No se pudo interpretar la respuesta del insert: {e}"
                self._log_failed_order(
                    index,
                    order,
                    sql,
                    message,
                    stage="RESPUESTA_SQL_INVALIDA",
                    result=result,
                    exception=e,
                )
                self.log(f"{message}\nSENTENCIA : {sql}")
                return set_response(None, 404, "Ocurrio un error al grabar el pedido.")

            if result_code != 11:
                self._log_failed_order(
                    index,
                    order,
                    sql,
                    result_message or "Ocurrio un error al grabar el pedido.",
                    stage="CODIGO_SQL_ERROR",
                    result=result,
                )
                self.log(f"{result_message}\nSENTENCIA : {sql}")
                return set_response(None, 404, result_message or "Ocurrio un error al grabar el pedido.")

            if is_duplicate:
                duplicate_count += 1
                self.log(
                    f"Pedido duplicado omitido. Cuenta: {account} TC: {tc} marker: {marker} id: {result_id_invoice}"
                )
                continue

            inserted_count += 1

        if duplicate_count:
            message = (
                f"Pedidos grabados correctamente. Nuevos: {inserted_count}. "
                f"Duplicados omitidos: {duplicate_count}."
            )
        else:
            message = "Pedidos grabados correctamente."

        return set_response([], 200, message)

    @route("/detail/<string:tc>/<string:invoice>", methods=["GET"])
    def get_detail_order(self, tc: str, invoice: str):
        sql = f"""
        SELECT convert(varchar,convert(decimal(15,2),isnull(a.importe,0))) as total_comprobante,CONVERT(NVARCHAR(10),a.fecha,103) as fecha,a.cuenta,a.nombre,a.tc,a.idcomprobante,ltrim(b.idarticulo) as idarticulo,b.descripcion,
        convert(varchar,convert(decimal(15,2),isnull(b.cantidad,0))) as cantidad,
        convert(varchar,convert(decimal(15,2),isnull(b.importe,0))) as importe,
        convert(varchar,convert(decimal(15,2),isnull(b.total,0))) as total
        FROM V_MV_CPTE a LEFT JOIN V_MV_CPTEINSUMOS b on a.tc = b.tc and a.idcomprobante = b.idcomprobante
        WHERE a.tc='{tc}' and a.idcomprobante='{invoice}'
        """

        result, error = get_customer_response(sql, f" al obtener el detalle del pedido {invoice}", True, self.token_global)
        response = set_response(result, 200 if not error else 404, "" if not error else result[0]["message"])
        return response

    @route("/search", methods=["POST"])
    def search_payments(self):
        data = request.get_json()

        seller = data.get("seller", "")
        fhd = data.get("dateFrom", datetime.now().strftime("%Y%m%d"))
        fhh = data.get("dateUntil", datetime.now().strftime("%Y%m%d"))

        fecha_desde = datetime.strptime(fhd, "%Y%m%d").strftime("%d/%m/%Y")
        fecha_hasta = datetime.strptime(fhh, "%Y%m%d").strftime("%d/%m/%Y")

        sql = f"sp_web_getComprobantes 'NP','{seller}','{fecha_desde}','{fecha_hasta}','',0"

        result, error = get_customer_response(sql, " al obtener los pedidos", True, self.token_global)
        response = set_response(result, 200 if not error else 404, "" if not error else result[0]["message"])
        return response

    def __delete_order_on_error(self, cpte_id: str, tc: str):
        safe_tc = (tc or "NP").strip().upper()
        query = f"""
        DELETE FROM V_MV_CPTEINSUMOS WHERE TC = '{safe_tc}' AND IDCOMPROBANTE = '{cpte_id}';
        DELETE FROM V_MV_CPTE WHERE ID = {cpte_id};
        """

        self.get_response(query, "Ocurrio un error al eliminar el comprobante", False, True)
