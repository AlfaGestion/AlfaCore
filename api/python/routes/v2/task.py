from datetime import datetime
from flask import request
from functions.general_customer import exec_customer_sql
from .master import MasterView
from functions.general_alfa import get_extension_and_b64string_file,get_path_tasks_file_both,insert_attachment, mkdir
from functions.responses import set_response
import base64
import os


class TaskView(MasterView):

    def post(self):
        local_path, path = get_path_tasks_file_both()

        tasks = request.get_json()

        for task in tasks:
            files = []
            file_number_name = 1
            account = task.get('account', '')
            seller = task.get('seller', '')
            date = task.get('date', datetime.now().strftime('%Y-%m-%d'))
            obs = task.get('obs', '')
            sign = task.get('sign', '')
            id_task = task.get('task', '') 

            customer_name = task.get('customerName','')
            document = task.get('document','')
            phone = task.get('phone','')

            image1a = task.get('image1a','')
            image1b = task.get('image1b','')
            image2a = task.get('image2a','')
            image2b = task.get('image2b','')
            image3a = task.get('image3a','')
            image3b = task.get('image3b','')
            image4a = task.get('image4a','')
            image4b = task.get('image4b','')
            image5a = task.get('image5a','')
            image5b = task.get('image5b','')

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

            date = datetime.strptime(date, '%Y-%m-%d').strftime('%d/%m/%Y')

            sql = f"""
            DECLARE @pRes INT
            DECLARE @pMensaje NVARCHAR(250)
            DECLARE @pIdCpte NVARCHAR(13)
            set nocount on; EXEC sp_web_setTareas '{account}','{seller}','{date}','{obs}','{id_task}','{customer_name}','{document}','{phone}',@pRes OUTPUT, @pMensaje OUTPUT, @pIdCpte OUTPUT
            SELECT @pRes as pRes, @pMensaje as pMensaje, @pIdCpte as pCpte
            """

            result, error = exec_customer_sql(sql, "", self.token_global, True)

            if error:
                return set_response(None, 404, "Ocurrió un error al grabar la tarea.")
            
            #Genero las imagenes            
            comprobante_id = result[0][2]

            try:
                dirname = os.path.join(path, self.code_account)
                local_dirname = os.path.join(local_path, self.code_account)

                if not mkdir(dirname):
                    return set_response([], 404, f"No se pudo crear el directorio {dirname}.")

                dirname = os.path.join(dirname, comprobante_id)
                local_dirname = os.path.join(local_dirname, comprobante_id)

                if not mkdir(dirname):
                    return set_response([], 404, f"No se pudo crear el directorio {dirname}.")

                for image in files:
                    image_data, extension = get_extension_and_b64string_file(image)
                    image_info = base64.b64decode(image_data)
                    document_filename = os.path.join(dirname, f"{file_number_name}{extension}")
                    local_document_filename = os.path.join(local_dirname, f"{file_number_name}{extension}")

                    with open(document_filename, "wb") as fh:
                        fh.write(image_info)
                        
                    insert_attachment(comprobante_id, local_document_filename,self.token_global)
                    file_number_name += 1
            except Exception as err:
                self.log(err)
                return set_response([], 404, f"Error al guardar las imagenes: {err}.")

        return set_response([], 200, "Tareas grabados correctamente.")
