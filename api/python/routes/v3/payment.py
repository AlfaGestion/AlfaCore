import json
from datetime import datetime
from flask import request
from functions.general_customer import exec_customer_sql, get_customer_response
from functions.responses import set_response
from flask_classful import route
from routes.v2.master import MasterView
from functions.Payment import Payment
from rich import print
from functions.Log import Log


class ViewPayment(MasterView):
    def _disable_valida_fpef_trigger_sql(self) -> str:
        return """
            IF EXISTS (
                SELECT 1
                FROM sys.triggers tr
                INNER JOIN sys.objects obj ON tr.parent_id = obj.object_id
                WHERE tr.name = N'TRG_ValidaFPEF'
                  AND obj.name = N'MV_ASIENTOS'
            )
            BEGIN
                ALTER TABLE MV_ASIENTOS DISABLE TRIGGER TRG_ValidaFPEF
            END
        """

    def _enable_valida_fpef_trigger_sql(self) -> str:
        return """
            IF EXISTS (
                SELECT 1
                FROM sys.triggers tr
                INNER JOIN sys.objects obj ON tr.parent_id = obj.object_id
                WHERE tr.name = N'TRG_ValidaFPEF'
                  AND obj.name = N'MV_ASIENTOS'
            )
            BEGIN
                ALTER TABLE MV_ASIENTOS ENABLE TRIGGER TRG_ValidaFPEF
            END
        """

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

    def _normalize_marker(self, marker: str, max_len: int = 20) -> str:
        if not marker:
            return ""
        return marker[:max_len]

    def _build_marker(self, payment: dict) -> str:
        payment_id = payment.get('paymentId', 'NO_ID')
        external_id = payment.get('externalId', '') or payment.get('external_id', '') or payment_id
        if not external_id or external_id == "NO_ID":
            return ""

        seller = payment.get('seller', '')
        seller_tag = seller.strip() if seller else ""
        if seller_tag:
            return self._normalize_marker(f"{seller_tag}-{external_id}")
        return self._normalize_marker(f"{external_id}")

    def _json_for_log(self, data) -> str:
        try:
            return json.dumps(data, ensure_ascii=False, default=str, indent=2)
        except Exception:
            return str(data)

    def _log_failed_payment(self, payment, message: str, stage: str = "", result=None, exception=None, sql: str = ""):
        try:
            payment = payment if isinstance(payment, dict) else {"raw": payment}
            payment_id = payment.get('paymentId', 'NO_ID')
            account = payment.get('account', '')
            seller = payment.get('seller', '')
            log_data = [
                "",
                "TIPO_ENVIO: COBRANZAS",
                f"ETAPA: {stage}",
                f"ID_CLIENTE: {self.code_account}",
                f"CUENTA_CLIENTE: {account}",
                f"VENDEDOR: {seller}",
                f"PAYMENT_ID: {payment_id}",
                f"ERROR: {message}",
            ]
            if exception:
                log_data.append(f"EXCEPCION: {exception}")
            if result is not None:
                log_data.append("RESULTADO:")
                log_data.append(self._json_for_log(result))
            log_data.append("PAYLOAD_COBRANZA:")
            log_data.append(self._json_for_log(payment))
            if sql:
                log_data.append("SQL:")
                log_data.append(sql)

            Log.create_v3_order("\n".join(log_data), self.code_account, "ERROR", token=self.token_global)
        except Exception:
            pass

    def _existing_payment_markers(self, markers):
        clean_markers = sorted({self._normalize_marker(str(marker)) for marker in markers if marker})
        if not clean_markers:
            return set()

        values = ",".join([f"'{marker.replace(chr(39), chr(39) + chr(39))}'" for marker in clean_markers])
        sql = f"""
        SELECT LTRIM(RTRIM(TRANSPORTE_NOMBRE)) AS marker
        FROM V_MV_CPTE
        WHERE TRANSPORTE_NOMBRE IN ({values})
        """

        try:
            result, error = exec_customer_sql(sql, " al validar cobranzas duplicadas", self.token_global, True)
        except Exception:
            return set()

        if error:
            return set()

        return {str(row[0]).strip() for row in result if row and row[0]}

    def _payment_exists(self, marker: str) -> bool:
        if not marker:
            return False

        safe_marker = marker.replace("'", "''")
        sql = f"SELECT TOP 1 ID FROM V_MV_CPTE WHERE TRANSPORTE_NOMBRE = '{safe_marker}'"
        result, error = get_customer_response(sql, "validar cobranza duplicada", True, self.token_global)
        return (not error) and len(result) > 0

    @route('/save', methods=['POST'])
    def save(self):
        """Utilizada para las cobranzas del movil.
        Asegura la continuidad del procesamiento aunque falle el guardado de un pago."""

        payments = request.get_json(silent=True)
        if not isinstance(payments, list):
            self._log_failed_payment(payments, "El payload de cobranzas no es una lista", "VALIDACION_PAYLOAD")
            return set_response([], 400, "El formato de cobranzas es inválido.")

        failed_payments = []
        markers_by_payment_id = {}
        for payment in payments:
            if isinstance(payment, dict):
                payment_id = payment.get('paymentId', 'NO_ID')
                markers_by_payment_id[payment_id] = self._build_marker(payment)
        existing_markers = self._existing_payment_markers(markers_by_payment_id.values())

        for payment in payments:
            paymentId = payment.get('paymentId', 'NO_ID') if isinstance(payment, dict) else 'NO_ID'

            try:
                if not isinstance(payment, dict):
                    self._log_failed_payment(payment, "La cobranza no tiene formato de objeto", "VALIDACION_COBRANZA")
                    failed_payments.append(paymentId)
                    continue

                tc = payment.get('tc', '')
                account = payment.get('account', '')
                date_raw = payment.get('date', datetime.now().strftime('%d/%m/%Y'))
                date = self._normalize_date_only(date_raw)
                seller = payment.get('seller', '')
                amount = payment.get('amount', 0)
                marker = markers_by_payment_id.get(paymentId, "")

                if marker and marker in existing_markers:
                    continue

                invoices = payment.get('invoices') or []
                methods = payment.get('methods') or []

                pay = Payment(tc, account, date, seller, amount)
                pay.set_db_token(self.token_global)
                pay.set_internal_id(paymentId)

                for method in methods:
                    pay.add_method(method['account'], method['amount'], method['checkNumber'], 'Null', 'Null')

                for invoice in invoices:
                    pay.add_application_invoice(invoice['tc'], invoice['idcomprobante'], invoice['amount'])

                save_result = pay.save()
                if isinstance(save_result, dict) and save_result.get('error', False):
                    message = pay.last_error or save_result.get('msg') or save_result.get('message') or "Falló el guardado de la cobranza"
                    self._log_failed_payment(payment, message, pay.last_stage, save_result, sql=pay.last_sql)
                    Log.create(f"ERROR: Falló el guardado del pago ID {paymentId}.")
                    failed_payments.append(paymentId)
                    continue

                if marker:
                    safe_marker = marker.replace("'", "''")
                    safe_obs_marker = f"Cobranza Web Nro: {paymentId}".replace("'", "''")
                    sql_marker = f"""
                    UPDATE TOP (1) V_MV_CPTE
                    SET TRANSPORTE_NOMBRE='{safe_marker}'
                    WHERE OBSERVACIONES LIKE '%{safe_obs_marker}%'
                    """
                    result_marker, error_marker = exec_customer_sql(sql_marker, " al actualizar matricula de la cobranza", self.token_global, False)
                    if error_marker:
                        self._log_failed_payment(
                            payment,
                            "La cobranza se grabó, pero falló la actualización de la marca de duplicado.",
                            "MARCAR_DUPLICADO",
                            result_marker,
                            sql=sql_marker,
                        )

            except Exception as e:
                self._log_failed_payment(payment, str(e), "EXCEPCION_GENERAL", exception=e)
                Log.create(f"ERROR: Falló el guardado del pago ID {paymentId}. Error: {e}")
                failed_payments.append(paymentId)

        if failed_payments:
            message = f"Cobranzas grabadas. Hubo fallos en {len(failed_payments)} pagos. IDs fallidos: {', '.join(failed_payments)}"
            response = set_response(failed_payments, 404, message)
        else:
            message = "Cobranzas grabadas correctamente."
            response = set_response(failed_payments, 200, message)

        return response

    def post(self):
        payments = request.get_json()

        for payment in payments:

            tc = payment.get('tc', '')
            account = payment.get('account', '')
            date_raw = payment.get('date', datetime.now().strftime('%d/%m/%Y'))
            date = self._normalize_date_only(date_raw)
            seller = payment.get('seller', '')
            amount = payment.get('amount', 0)
            mp = payment.get('mp', '')
            obs = payment.get('observation', '')
            paymentId = payment.get('paymentId', '')

            check = payment.get('check', '')

            if(check != ''):
                check_number = check.get('nro', '')
                check_expiration = check.get('expiration', '')
                check_idbank = check.get('idBank', '')
            else:
                check_number = ''
                check_expiration = ''
                check_idbank = ''

            sql = f"""
            DECLARE @pRes INT
            DECLARE @pMensaje NVARCHAR(250)

            {self._disable_valida_fpef_trigger_sql()}
            set nocount on; EXEC sp_web_setCobranza '{tc}','{account}','{seller}','{date}',{amount},'{obs}','{mp}','{paymentId}','{check_number}','{check_expiration}','{check_idbank}',@pRes OUTPUT, @pMensaje OUTPUT
            {self._enable_valida_fpef_trigger_sql()}
            SELECT @pRes as pRes, @pMensaje as pMensaje
            """

            try:
                result, error = exec_customer_sql(sql, f" al grabar la cobranza",  self.token_global)
            except Exception as r:
                error = True

            if error:
                self._log_failed_payment(payment, str(result), "SP_WEB_SETCOBRANZA", result, sql=sql)
                self.log(str(result) + "\nSENTENCIA : " + sql)
                return set_response(None, 404, "Ocurrió un error al grabar la cobranza. Intente nuevamente.")

        self.__auto_application(tc, account, paymentId)
        response = set_response([], 200, "Cobranzas grabadas correctamente.")

        return response

    def __auto_application(self, tc: str, account: str, paymentWebId: str = ''):
        sql = f"""
        DECLARE @pRes INT
        DECLARE @pMensaje NVARCHAR(250)

        {self._disable_valida_fpef_trigger_sql()}
        set nocount on; EXEC sp_web_AplicacionCobranzaAutomatica '{tc}','{account}','{paymentWebId}',@pRes OUTPUT, @pMensaje OUTPUT
        {self._enable_valida_fpef_trigger_sql()}
        SELECT @pRes as pRes, @pMensaje as pMensaje
        """

        try:
            result, error = exec_customer_sql(sql, f" al generar la aplicación automática",  self.token_global)
        except Exception as r:
            error = True

        if error:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + sql)
            return set_response(None, 404, "Ocurrió un error al generar la aplicación automática. Intente nuevamente.")

    @route('/search', methods=['POST'])
    def search_payments(self):
        data = request.get_json()

        seller = data.get('seller', '')
        fhd = data.get('dateFrom', datetime.now().strftime('%Y%m%d'))
        fhh = data.get(
            'dateUntil', datetime.now().strftime('%Y%m%d'))

        fecha_desde = datetime.strptime(fhd, '%Y%m%d').strftime('%d/%m/%Y')
        fecha_hasta = datetime.strptime(fhh, '%Y%m%d').strftime('%d/%m/%Y')

        sql = f"""
        sp_web_getComprobantes 'CB','{seller}','{fecha_desde}','{fecha_hasta}','',0
        """

        result, error = get_customer_response(
            sql, f" al obtener las cobranzas", True, self.token_global)

        response = set_response(
            result, 200 if not error else 404, "" if not error else result[0]['message'])
        return response
