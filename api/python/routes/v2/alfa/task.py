import base64
from datetime import datetime
import os
from random import randint
from unittest import result
from flask import request, send_file, abort
from functions.general_alfa import get_extension_and_b64string_file,  get_path_tasks_file_both, insert_attachment, mkdir, send_email
from functions.general_customer import exec_customer_sql, get_customer_response, rgbint_to_hex
from ..master import MasterView
from flask_classful import route
from functions.responses import set_response
from rich import print


class AlfaTaskView(MasterView):

    def post(self):
        data = request.get_json()
        description = data.get('description', '')
        user = data.get('user', '')
        email = data.get('email', '')
        title = data.get('title', '')

        sql = f"""
            DECLARE @pIdCpte NVARCHAR(13)
            set nocount on;
            EXEC sp_web_AltaTarea '{self.code_account}','   1','{description}','{user}','{email}',1,'{title}', @pIdCpte OUTPUT
            SELECT @pIdCpte as comprobante
        """

        try:
            result, error = exec_customer_sql(sql, "", self.token_global, True, True)
        except Exception as r:
            error = True

        if error:
            self.log(str(result) + "\nSENTENCIA : " + sql)
            return set_response(result, 404, "No se pudo grabar la tarea.")

        comprobante_id = result[0][0]

        return self.save_file_task(comprobante_id, email, description, data.get('files', []))

    @route('/list')
    @route('/list/<string:status>')
    @route('/list/<string:status>/<string:budget>')
    def list(self, status: str = '', budget: str = ''):
        """
        Retorna un listado de tareas por estado, por defecto todas
        """

        status = '' if status == '*' else status

        sql = f"""
        sp_web_getTareas '{self.code_account}','{status}','{budget}'
        """

        result, error = get_customer_response(sql, f"Tareas del cliente {self.code_account}", True, self.token_global, True)

        if error:
            self.log(str(result) + "\nSENTENCIA : " + sql)
            return set_response(None, 404, "No se pudo obtener las tareas.")

        for item in result:
            if item["color"]:
                item["color"] = rgbint_to_hex(item["color"])

            item['descripcionadicional'] = self.hide_comments(item['descripcionadicional'])

        return set_response(result, 200, "Tareas grabados correctamente.")

    def get(self, id: str):
        # def get_task(cpte: str, account: str = ''):
        """Retorna la información de una orden de trabajo"""

        # verify = True
        # if account:
        #     verify = verify_account_task(account, cpte)

        # if not verify:
        #     return jsonify({"data": f"El comprobante no existe", "status": "error"})

        sql = f"""
        SELECT tipo,tc,isnull(estado_actual,'') as estado_actual,idcomprobante,idtarea,descripcion,observaciones,convert(NVARCHAR(18),fecha,103) + ' ' + convert(NVARCHAR(18),fecha,108) as fecha,isnull(color,0) as color, (minutos /60) as horas FROM (
        SELECT 'C' as tipo,(SELECT TOP 1 CAST(Observaciones as NVARCHAR(MAX)) as observaciones FROM V_MV_STATUS WHERE TC='OT' AND IdComprobante='{id}' ORDER BY NroMov DESC) as estado_actual,
        tc,idcomprobante,IDTAREA,DESCRIPCION,DescripcionAdicional as observaciones,FechaEstInicio as fecha,
        (SELECT TOP 1 color FROM V_MV_STATUS LEFT JOIN  V_TA_STATUS ON V_MV_STATUS.IdEstado = V_TA_STATUS.CodigoEstado WHERE TC='OT' AND IdComprobante='{id}' ORDER BY NroMov DESC) as color, horas as minutos FROM V_MV_CpteTareas WHERE idcomprobante='{id}'
        UNION
        SELECT 'D' as tipo,isnull(v_ta_tecnicos.nombre,usuario) as estado_actual,TC,IDCOMPROBANTE,IDTarea,Descripcion,OBSERVACIONES,fecha,0 as color, MINUTOS FROM V_MV_Diarios LEFT JOIN V_TA_TECNICOS ON V_MV_DIARIOS.idtecnico = V_TA_TECNICOS.idtecnico WHERE idcomprobante='{id}'
        UNION
        SELECT 'S' as tipo,'' as estado_actual,TC,IdComprobante,IdTarea,'' as descripcion,CAST(Observaciones as NVARCHAR(MAX)) as observaciones,FechaHora as fecha,color,0 as minutos FROM V_MV_STATUS 
        LEFT JOIN V_TA_STATUS ON V_MV_STATUS.IdEstado = V_TA_STATUS.CodigoEstado 
        WHERE idcomprobante='{id}'
        UNION
        SELECT 'Y' as tipo,
        (case when documento like '%\%' then reverse(left(reverse(documento), charindex('\\', reverse(documento)) - 1)) else '' end) as estado_actual,
        tc,IDCOMPROBANTE,CAST(id as nvarchar(100)) as idtarea,(case when documento like '%.%' then reverse(left(reverse(documento), charindex('.', reverse(documento)) - 1)) else '' end) as descripcion
        ,documento as observaciones,GETDATE() as fecha,0 as color, 0 as horas from V_MV_CpteDoc 
        where IDCOMPROBANTE='{id}'
        UNION
        SELECT 'X' as tipo,'' as estado_actual,TC,IdComprobante,'' as idtarea,'' as descripcion,ISNULL(CAST(OBSERVACIONES as NVARCHAR(MAX)) ,'') as observaciones,'01/01/3000' as fecha,0 as color,0 as minutos 
        FROM V_MV_CPTE
        WHERE idcomprobante='{id}' AND TC='OT'
        ) AS D ORDER BY fecha
        """

        result, error = get_customer_response(sql, f"Tarea del cliente {self.code_account}, id: {id}", True, self.token_global, True)

        if error:
            return set_response(None, 404, "No se pudo obtener las tarea.")

        for item in result:
            if item["color"]:
                item["color"] = rgbint_to_hex(item["color"])

            if item['tipo'] == 'D' or item['tipo'] == 'C':
                item['observaciones'] = self.hide_comments(item['observaciones'])

        return set_response(result, 200, "")

    @route('/get_hours_pending')
    def get_hours_pending(self):
        """Retorna los datos del último presupuesto abierto"""

        sql = f"""
        SELECT TOP 1 a.tc,a.idcomprobante,CONVERT(NVARCHAR(10),A.fecha,103) as fecha, (SUM(b.HORAS) / 60) as horas,CONVERT(NVARCHAR(10),a.fechaestfin,103) as vencimiento, DATEDIFF(DAY,GETDATE(),a.fechaestfin) as dias_restantes,
        (SELECT (SUM(f.HORAS) / 60) FROM V_MV_CPTE d
        LEFT JOIN V_MV_CPTE e on d.IDCOMPROBANTE = e.COMPROBANTEORIGEN and d.CUENTA=e.CUENTA 
        LEFT JOIN V_MV_CpteTareas f on f.TC=e.TC and f.IDCOMPROBANTE = e.IDCOMPROBANTE
        WHERE d.TC='PR' and (d.NroOrdenDeTrabajo='' or d.NroOrdenDeTrabajo is null) and e.TC='OT' and d.CUENTA='{self.code_account}' and d.FINALIZADA=0 
        and d.IDCOMPROBANTE= a.IDCOMPROBANTE) as horas_trabajadas
        FROM v_mv_cpte a LEFT JOIN V_MV_CpteTareas b ON a.TC = b.TC and a.IDCOMPROBANTE = b.IDCOMPROBANTE 
        WHERE a.TC='PR' and (a.NROORDENDETRABAJO ='' OR a.NROORDENDETRABAJO is null) AND a.FINALIZADA=0 AND a.CUENTA='{self.code_account}' and a.FechaEstFin>=GETDATE()
        GROUP BY a.tc, a.IDCOMPROBANTE, a.FECHA,a.fechaestfin
        ORDER BY a.FECHA DESC
        """

        result, error = get_customer_response(sql, f"Presupuesto del cliente {self.code_account}, id: {id}", True, self.token_global, True)

        if error:
            return set_response(None, 404, "No se pudo obtener el presupuesto.")

        return set_response(result, 200, "")

    @route('/get_file/<int:id>')
    def get_file(self, id: int):

        sql = f"SELECT documento FROM V_MV_CpteDoc WHERE ID={id}"
        path = ""
        result, error = exec_customer_sql(sql, "", "", True, True)
        if error:
            return set_response(None, 404, "No se pudo obtene las tarea.")

        for items in result:
            if items[0]:
                path = items[0]

        download = path.replace("SERVER-ALFAVB6", "10.30.0.6")

        try:
            return send_file(download, as_attachment=True)
        except FileNotFoundError:
            abort(500)

    @route('/calificate', methods=['POST'])
    def calificate(self):
        data = request.get_json()
        id = data.get('id', '')
        calification = data.get('calification', '')
        comment = data.get('comment', '')

        sql = f"""
            set nocount on; EXEC sp_web_CalificarRespuesta '{id}',{calification},'{comment}'
        """

        try:
            result, error = exec_customer_sql(sql, "", self.token_global, True, True)
        except Exception as r:
            error = True

        if error:
            self.log(str(result) + "\nSENTENCIA : " + sql)
            return set_response(None, 404, "No se pudo calificar la respuesta.")

        return set_response([], 200, "Calificado correctamente")

    @route('/response', methods=['POST'])
    def send_task_reponse(self):
        data = request.get_json()

        id = data.get('idcpte', '')
        message = data.get('message', '')
        account = data.get('account', '')
        user = data.get('user', '')
        date = datetime.today().strftime("%d/%m/%Y %H:%M:%S")

        sql = f"""
        DECLARE @IDRETORNO AS INT;
        set nocount on; EXEC sp_web_setRespuestaTarea 'V',0,'OT','{id}','','','{date}','{date}','VS','Respuesta','{message}','','{account}',0,'{user}','0',1,@IDRETORNO
        SELECT @IDRETORNO
        """

        try:
            result, error = exec_customer_sql(sql, "", "", True, True)
        except Exception as r:
            error = True

        if error:
            self.log(str(result) + "\nSENTENCIA : " + sql)
            return set_response(result, 404, "No se pudo grabar la respuesta.")

        return self.save_file_task(id, '', message, data.get('files', []), True)

    def save_file_task(self, comprobante_id: str, email: str, description: str, files: list, is_response=False):
        if comprobante_id:
            local_path, path = get_path_tasks_file_both()
            if is_response:
                action = 'C'
                file_number_name = randint(100, 200)
            else:
                action = 'R'
                file_number_name = 1

            try:
                dirname = os.path.join(path, self.code_account)
                local_dirname = os.path.join(local_path, self.code_account)

                if not mkdir(dirname):
                    return set_response([], 404, f"No se pudo crear el directorio {dirname}.")

                dirname = os.path.join(dirname, comprobante_id)
                local_dirname = os.path.join(local_dirname, comprobante_id)

                if not mkdir(dirname):
                    return set_response([], 404, f"No se pudo crear el directorio {dirname}.")

                document_filename = os.path.join(dirname, f"{comprobante_id}.rtf")
                local_document_filename = os.path.join(local_dirname, f"{comprobante_id}.rtf")

                with open(document_filename, "w") as f:
                    f.write(description)

                for image in files:
                    image_data, extension = get_extension_and_b64string_file(image)
                    image_info = base64.b64decode(image_data)
                    document_filename = os.path.join(dirname, f"{file_number_name}{extension}")
                    local_document_filename = os.path.join(local_dirname, f"{file_number_name}{extension}")

                    with open(document_filename, "wb") as fh:
                        fh.write(image_info)
                    insert_attachment(comprobante_id, local_document_filename)
                    file_number_name += 1

            except Exception as err:
                return set_response([], 404, f"Error al generar el comprobante: {err}.")

            if email:
                send_email(email, description, action)
            return set_response([], 200, "Tarea creada correctamente.")
        else:
            return set_response([], 404, "No se pudo generar el comprobante.")

    def hide_comments(self, task_body: str):
        # Agrego dos espacios en blanco al principio para evitar problemas
        modified_text = "  " + task_body + "  "

        is_running = True
        while is_running:
            first_coincidence = modified_text.find('//')
            second_coincidence = modified_text.find('//', first_coincidence + 1)

            if first_coincidence > 0 and second_coincidence > 0:
                try:
                    modified_text = modified_text[:first_coincidence-1] + modified_text[second_coincidence+2:]
                except:
                    is_running = False
            else:
                is_running = False
        return modified_text.strip()
