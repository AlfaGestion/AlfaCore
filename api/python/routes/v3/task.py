from datetime import datetime
from flask import request
from functions.general_customer import exec_customer_sql, get_customer_response
from routes.v2.master import MasterView
from functions.general_alfa import get_extension_and_b64string_file, get_path_tasks_file_both, insert_attachment, mkdir
from functions.responses import set_response
import base64
import os


class ViewTask(MasterView):
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
    def _delete_task_on_error(self, cpte_id: str):
        if not cpte_id:
            return

        query = f"""
        DELETE FROM V_MV_CPTETAREAS WHERE IDCOMPROBANTE = '{cpte_id}';
        DELETE FROM V_MV_CPTE WHERE IDCOMPROBANTE = '{cpte_id}';
        """
        exec_customer_sql(query, " al eliminar la tarea incompleta", self.token_global, False)
    def _append_marker(self, obs: str, marker: str, max_len: int = 250) -> str:
        if not marker:
            return obs or ""

        obs = obs or ""
        if marker in obs:
            return obs

        sep = " " if obs else ""
        candidate = f"{obs}{sep}{marker}"
        if len(candidate) <= max_len:
            return candidate

        max_obs_len = max_len - len(marker) - 1
        if max_obs_len <= 0:
            return marker[:max_len]

        trimmed_obs = obs[:max_obs_len].rstrip()
        return f"{trimmed_obs} {marker}"

    def _task_exists(self, marker: str) -> bool:
        if not marker:
            return False

        safe_marker = marker.replace("'", "''")
        sql = f"SELECT TOP 1 ID FROM V_MV_CPTE WHERE TC='OT' AND TRANSPORTE_NOMBRE = '{safe_marker}'"
        result, error = get_customer_response(sql, "validar tarea duplicada", True, self.token_global)
        return (not error) and len(result) > 0

    def post(self):
        local_path, path = get_path_tasks_file_both()

        tasks = request.get_json()

        for task in tasks:
            files = []
            file_number_name = 1
            account = task.get('account', '')
            seller = task.get('seller', '')
            date_raw = task.get('date', datetime.now().strftime('%d/%m/%Y'))
            obs = task.get('obs', '')
            sign = task.get('sign', '')
            id_task = task.get('task', '')
            external_id = task.get('externalId', '') or task.get('external_id', '') or task.get('taskId', '') or task.get('id', '')

            customer_name = task.get('customerName', '')
            document = task.get('document', '')
            phone = task.get('phone', '')
            x = task.get('latitude', '0')
            y = task.get('longitude', '0')

            image1a = task.get('image1a', '')
            image1b = task.get('image1b', '')
            image2a = task.get('image2a', '')
            image2b = task.get('image2b', '')
            image3a = task.get('image3a', '')
            image3b = task.get('image3b', '')
            image4a = task.get('image4a', '')
            image4b = task.get('image4b', '')
            image5a = task.get('image5a', '')
            image5b = task.get('image5b', '')

            if image1a and image1b:
                files.append(f"{image1a}{image1b}")

            if image2a and image2b:
                files.append(f"{image2a}{image2b}")

            if image3a and image3b:
                files.append(f"{image3a}{image3b}")

            if image4a and image4b:
                files.append(f"{image4a}{image4b}")

            if image5a and image5b:
                files.append(f"{image5a}{image5b}")

            date = self._normalize_date_only(date_raw)

            marker = ""
            if external_id:
                seller_tag = seller.strip() if seller else ""
                if seller_tag:
                    marker = f"{seller_tag}-{external_id}"
                else:
                    marker = f"{external_id}"

            if marker and self._task_exists(marker):
                self.log(f"Tarea duplicada omitida. Marker: {marker}")
                continue

            if marker:
                obs = self._append_marker(obs, marker)

            sql = f"""
            DECLARE @pRes INT
            DECLARE @pMensaje NVARCHAR(250)
            DECLARE @pIdCpte NVARCHAR(13)
            set nocount on; EXEC sp_web_setTareas '{account}','{seller}','{date}','{obs}','{id_task}','{customer_name}','{document}','{phone}','{x}','{y}',@pRes OUTPUT, @pMensaje OUTPUT, @pIdCpte OUTPUT
            SELECT @pRes as pRes, @pMensaje as pMensaje, @pIdCpte as pCpte
            """

            result, error = exec_customer_sql(sql, "", self.token_global, True)

            if error:
                return set_response(None, 404, "Ocurrio un error al grabar la tarea.")

            # Genero las imagenes
            comprobante_id = result[0][2]
            if marker:
                safe_marker = marker.replace("'", "''")
                sql_marker = f"UPDATE V_MV_CPTE SET TRANSPORTE_NOMBRE='{safe_marker}' WHERE IDCOMPROBANTE='{comprobante_id}' AND TC='OT'"
                exec_customer_sql(sql_marker, " al actualizar transporte_nombre de la tarea", self.token_global, False)

            try:
                dirname = os.path.join(path, self.code_account)
                local_dirname = os.path.join(local_path, self.code_account)

                if not mkdir(dirname):
                    self._delete_task_on_error(comprobante_id)
                    return set_response([], 404, f"No se pudo crear el directorio {dirname}.")

                dirname = os.path.join(dirname, comprobante_id)
                local_dirname = os.path.join(local_dirname, comprobante_id)

                if not mkdir(dirname):
                    self._delete_task_on_error(comprobante_id)
                    return set_response([], 404, f"No se pudo crear el directorio {dirname}.")

                for image in files:
                    image_data, extension = get_extension_and_b64string_file(image)
                    image_info = base64.b64decode(image_data)
                    document_filename = os.path.join(dirname, f"{file_number_name}{extension}")
                    local_document_filename = os.path.join(local_dirname, f"{file_number_name}{extension}")

                    with open(document_filename, "wb") as fh:
                        fh.write(image_info)

                    insert_attachment(comprobante_id, local_document_filename, self.token_global)
                    file_number_name += 1
            except Exception as err:
                self.log(err)
                self._delete_task_on_error(comprobante_id)
                return set_response([], 404, f"Error al guardar las imagenes: {err}.")

        return set_response([], 200, "Tareas grabados correctamente.")
