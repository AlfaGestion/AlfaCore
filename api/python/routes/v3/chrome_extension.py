
from flask_classful import FlaskView, route
from flask import request
from datetime import datetime
from functions.responses import set_response
from functions.general_alfa import exec_sql, exec_alfa_sql
from functions.Log import Log
from functions.general_customer import rgbint_to_hex
import decimal


class ViewChromeExtension(FlaskView):

    @route("/get_technicians", methods=["GET"])
    def get_technicians(self):
        result, error = exec_sql("SELECT ltrim(idtecnico) as codigo, ltrim(nombre) as descripcion FROM v_ta_tecnicos")

        if error:
            return set_response(result, 404, "error")

        return set_response(result)

    @route("/get_customers", methods=["GET"])
    def get_costumers(self):
        result, error = exec_sql(
            "SELECT codigo, ltrim(razon_social) as descripcion FROM vt_clientes where Dada_De_Baja = 0 order by razon_social asc")
        if error:
            return set_response(result, 404, "error")

        for element in result:
            query = f"""
            select celular from (select replace(replace(replace(replace(replace(celular,'-',''),' ',''),'+',''),'(',''),')','') as celular, cuenta
            from VT_MA_CUENTAS_CONTACTOS where celular is not null and celular<>''
            ) as A where cuenta='{element["codigo"]}'
            union
            select telefono as celular from (select replace(replace(replace(replace(replace(telefono,'-',''),' ',''),'+',''),'(',''),')','') as telefono, cuenta
            from VT_MA_CUENTAS_CONTACTOS where telefono is not null and telefono<>''
            ) as A where cuenta='{element["codigo"]}'
            """
            data, error = exec_sql(query)
            element["phones"] = data
        return set_response(result)

    @route("/tasks/<account>", methods=["GET"])
    def get_tasks(self, account):
        
        query = f"""
        SELECT isnull(descripcion_tarea,'') as descripcion_tarea,fecha as fechaorden,CONVERT(NVARCHAR(10),fecha,103) as fecha,id,descripcion,isnull(NombreTecnico,'') as nombre_tecnico,idcomprobante,cuenta,nombre,isnull(idestado,'') as idestado,isnull(estado,'') as estado
        ,isnull(color,'') as color,isnull(tipo,'') as tipo,isnull(comprobanteorigen,'') as presupuesto,descripcionadicional,horas as minutos,isnull(solicitante,'') as solicitante FROM (
        Select  isnull(ltrim(idtarea),'') as idtarea,descripcion_tarea,FechaEstInicio as fecha, id, z.descripcion, IdTecnico , NombreTecnico , IDCOMPROBANTE , CUENTA, nombre,idestado, x.Descripcion as Estado, x.Color, x.Tipo, comprobanteorigen,ISNULL(CAST(descripcionadicional as NVARCHAR(MAX)) ,'') as descripcionadicional,horas,solicitante  from (
        Select top 20 a.idtarea,d.DESCRIPCION as descripcion_tarea,a.FechaEstInicio, a.id, a.descripcion, a.idtecnico, c.Nombre as NombreTecnico,a.idcomprobante , b.cuenta,b.nombre, b.comprobanteorigen,ISNULL(CAST(a.descripcionadicional as NVARCHAR(MAX)) ,'') as descripcionadicional,a.horas,b.solicitante,
        (select top 1 idestado from v_mv_status where a.IDCOMPROBANTE = V_MV_STATUS.IdComprobante and v_mv_status.tc = 'OT'
        ORDER BY v_mv_status.FechaHora Desc) as IdEstado 
        from v_mv_cptetareas a inner join v_mv_cpte b on a.tc = b.tc and a.idcomprobante = b.idcomprobante and b.cuenta='{account}'
        left outer  join v_ta_tecnicos c on a.IdTecnico = c.IdTecnico 
        LEFT JOIN V_TA_Tareas d on a.IDTAREA = d.IdTarea
        where a.FechaRealFin is null AND A.TC = 'OT'
        order by a.fechaestinicio desc, a.idcomprobante desc
        ) z left outer join v_ta_status x on z.idestado = x.codigoestado where (tipo <> 'A' or tipo is null)
        UNION
        Select  isnull(ltrim(idtarea),'''') as idtarea,descripcion_tarea,FechaEstInicio, id, z.descripcion, IdTecnico , NombreTecnico , IDCOMPROBANTE , CUENTA,nombre, idestado, x.Descripcion as Estado, x.Color, x.Tipo,COMPROBANTEORIGEN,ISNULL(CAST(descripcionadicional as NVARCHAR(MAX)) ,'') as descripcionadicional,horas,solicitante from (
        Select top 20 a.idtarea,d.DESCRIPCION as descripcion_tarea,a.FechaEstInicio, a.id, a.descripcion, a.idtecnico, c.Nombre as NombreTecnico,a.idcomprobante , b.cuenta,b.nombre, b.comprobanteorigen,ISNULL(CAST(a.descripcionadicional as NVARCHAR(MAX)) ,'') as descripcionadicional,a.horas,b.solicitante,
        (select top 1 idestado from v_mv_status where a.IDCOMPROBANTE = V_MV_STATUS.IdComprobante and v_mv_status.tc = 'OT'
        ORDER BY v_mv_status.FechaHora Desc) as IdEstado
        from v_mv_cptetareas a inner join v_mv_cpte b on a.tc = b.tc and a.idcomprobante = b.idcomprobante  and b.cuenta='{account}'
        left outer  join v_ta_tecnicos c on a.IdTecnico = c.IdTecnico 
        LEFT JOIN V_TA_Tareas d on a.IDTAREA = d.IdTarea
        where a.FechaRealFin <> '' AND A.TC = 'OT'
        order by a.fechaestinicio desc, a.idcomprobante desc 
        ) z left outer join v_ta_status x on z.idestado = x.codigoestado where (tipo <> 'A' or tipo is null)
        ) as j WHERE tipo='P'  ORDER by fechaorden desc,id desc
        """

        result, error = exec_sql(query)
        

        if error:
            Log.create(result)
            return set_response(result, 404, "error")
        


        return set_response(result)

    @route("/get_customer_by_phone/<phone>", methods=["GET"])
    def get_customer_by_phone(self, phone):
        phone2 = phone[3:]

        query = f"""
        select top 1 cuenta,descripcion from (
        select isnull(descripcion,'') as descripcion,replace(replace(replace(replace(replace(isnull(celular,''),'-',''),' ',''),'+',''),'(',''),')','') as celular, 
        replace(replace(replace(replace(replace(isnull(telefono,''),'-',''),' ',''),'+',''),'(',''),')','') as telefono,isnull(cuenta,'') as cuenta
        from VT_MA_CUENTAS_CONTACTOS
        ) as A where (telefono='{phone}' or celular='{phone}' or telefono='{phone2}' or celular='{phone2}') and cuenta<>''
        """

        # print(query)
        result, error = exec_sql(query)

        if error:
            return set_response(result, 404, "error")
        
        if result:
            account = result[0]['cuenta']

            if not account:
                return set_response(result)
            
            #Obtengo las horas presupuestadas pendientes
            query = f"""
            SELECT SUM(horas) - SUM(horas_trabajadas) as resto FROM (
            SELECT(SUM(b.HORAS) / 60) as horas,
            (SELECT (SUM(f.HORAS) / 60) FROM V_MV_CPTE d
            LEFT JOIN V_MV_CPTE e on d.IDCOMPROBANTE = e.COMPROBANTEORIGEN and d.CUENTA=e.CUENTA 
            LEFT JOIN V_MV_CpteTareas f on f.TC=e.TC and f.IDCOMPROBANTE = e.IDCOMPROBANTE
            WHERE d.TC='PR' and (d.NroOrdenDeTrabajo='' or d.NroOrdenDeTrabajo is null) and e.TC='OT' and d.CUENTA='{account}' and d.FINALIZADA=0 
            and d.IDCOMPROBANTE= a.IDCOMPROBANTE) as horas_trabajadas
            FROM v_mv_cpte a LEFT JOIN V_MV_CpteTareas b ON a.TC = b.TC and a.IDCOMPROBANTE = b.IDCOMPROBANTE 
            WHERE a.TC='PR' and (a.NROORDENDETRABAJO ='' OR a.NROORDENDETRABAJO is null) AND a.FINALIZADA=0 AND a.CUENTA='{account}'
            and DATEDIFF(DAY,GETDATE(),a.fechaestfin)>-2
            GROUP BY a.tc, a.IDCOMPROBANTE, a.FECHA,a.fechaestfin,a.comentarios,b.descripcion
            ) as g
            """

            presupuestos, error = exec_sql(query)
            if error:
                return set_response(result, 404, "error")

            query = f"""
            SELECT isnull(b.descripcion,'SIN CLASIFICAR') as descripcion,isnull(b.color,'000000') as color,
            (SELECT COUNT(c.saldo) FROM VE_CPTES_IMPAGOS c WHERE c.cuenta='{account}' AND DATEDIFF(day,c.vencimiento,GETDATE())>30 ) as pendientes 
            FROM MA_CUENTASADIC a
            LEFT JOIN TA_CLASIFICACIONES b ON a.clasificacion = b.codigo
            WHERE a.codigo='{account}' GROUP BY b.descripcion,b.color
            """

            data, error = exec_sql(query)
            if error:
                return set_response(result, 404, "error")
            
            try:
                for item in data:
                    if(item['color'] != ''):
                        item['color'] = rgbint_to_hex(item['color'])
                
                result[0]['status'] = data[0]
                result[0]['hours'] = presupuestos[0]['resto']
            except Exception as e:
                Log.create(e)
                return set_response(result, 404, "error")

        return set_response(result)

    @route("/task", methods=["POST"])
    def post_task(self):
        data = request.get_json()

        date = datetime.now().strftime('%d-%m-%Y')
        content = ""
        chatImage = ""
        customerCode = data.get('customerCode', '')
        messages = data.get('messages', '')
        statusCode = data.get('statusCode', '')
        currentPhone = data.get('currentPhone', '')
        createContact = data.get('createContact', '')
        comments = data.get('comments', '')
        customerName = data.get('customerName', '')
        technician = data.get('technician', '')
        category = data.get('category', '')
        taskTime = data.get('time', 10)

        import base64, os
        from functions.general_alfa import get_extension_and_b64string_file,  get_path_tasks_file_both, insert_attachment, mkdir, send_email

        styles = """
        <style>
            .out {
                background-color: rgb(217, 253, 211);
                width:100%
                float: right;
                
            }
            .message {
                font-size: 80%;
                margin-bottom: 1px;
                padding: 10px;
            }
            .in {
                background-color: rgb(229, 231, 234);
                width:100%
            }

            .image-preview {
                width:300px !important;
                height: 300px !important;
            }

            .tecnico-mensaje {
                width: 100%;
                background-color: #58A9E8;
                margin-top:10px;
                padding:10px;
            }
            
            .small {
                font-size:80%;
            }
        </style>
        """

        content += styles

        for message in messages:
            # if message['in']:
                # content += f"Cliente: {message['message']} \n"
            # else:
                # content += f"Tecnico: {message['message']} \n"
            if message['in']:
                content += f'<div class="message in">{message["html"]}</div>'
            else:
                content += f'<div class="message out">{message["html"]}</div>'

    
        content += f'<div class="tecnico-mensaje"><p>Comentarios del tecnico: {comments}</p></div>'
        content += f'\n\n\n\n<p class="small"><b>Tarea generada desde WhatsApp.<b></p>'
        content += f'\n<p>Contacto: {customerName} - {currentPhone}</p>'

        sql = ""

        # print(content)

        try:
            if createContact:
                sql = f""" 
                insert into MA_CONTACTOS (Nombre_y_Apellido, CuentaRel, Celular) values ('{customerName}', '{customerCode}', '{currentPhone}')
                DECLARE @id INT
                SELECT @id = SCOPE_IDENTITY()
                insert into MA_CONTACTOS_CUENTAS (idContacto,Cuenta) VALUES (@id, '{customerCode}')
                """
                result, error = exec_alfa_sql(sql, "", True)
        except Exception as e:
            Log.create(e)

        sql = ""

        sql = sql + f"""
            DECLARE @pRes INT
            DECLARE @pIdCpte NVARCHAR(13)
            DECLARE @pMensaje NVARCHAR(250)
            set nocount on; EXEC sp_web_setExtensionTareas '{technician}','{customerCode}','{date}','{content}','{statusCode}', {taskTime},'{customerName}','{category}',@pIdCpte OUTPUT, @pRes OUTPUT, @pMensaje OUTPUT
            SELECT @pIdCpte, @pRes, @pMensaje
            """
        result, error = exec_alfa_sql(sql, "", True)

        if error:
            Log.create(result)
            return set_response([], 404, "error")
        
        ticket_id = result[0][0]
        file_number_name = 1
        # for message in messages:
            
        #     if message['base64']:
        #         try:
        #             local_path, path = get_path_tasks_file_both()

        #             dirname = os.path.join(path, customerCode)
        #             local_dirname = os.path.join(local_path, customerCode)

        #             if not mkdir(dirname):
        #                 return set_response([], 404, f"No se pudo crear el directorio {dirname}.")

        #             dirname = os.path.join(dirname, ticket_id)
        #             local_dirname = os.path.join(local_dirname, ticket_id)

        #             if not mkdir(dirname):
        #                 return set_response([], 404, f"No se pudo crear el directorio {dirname}.")

        #             # document_filename = os.path.join(dirname, f"{ticket_id}.rtf")
        #             # local_document_filename = os.path.join(local_dirname, f"{ticket_id}.rtf")
                    
        #             image_data, extension = message['base64'],".png"
        #             image_info = base64.b64decode(image_data)
                    
        #             document_filename = os.path.join(dirname, f"{file_number_name}{extension}")
        #             local_document_filename = os.path.join(local_dirname, f"{file_number_name}{extension}")

        #             with open(document_filename, "wb") as fh:
        #                 fh.write(image_info)

        #             insert_attachment(ticket_id, local_document_filename)
        #             file_number_name += 1
        #         except Exception as e:
        #             print(e)


        return set_response({'ticket': ticket_id })
    
    @route("/get_categories", methods=["GET"])
    def get_categories(self):
        result, error = exec_sql("SELECT ltrim(idtarea) as codigo, ltrim(descripcion) as descripcion FROM v_ta_tareas")

        if error:
            return set_response(result, 404, "error")

        return set_response(result)

    @route("/get_update_task", methods=["PUT"])
    def get_update_task(self):
        data = request.get_json()
        id_ticket = data.get('idcpte', '')

        result, error = exec_sql(f"""SELECT * FROM V_MV_CPTETAREAS WHERE IDCOMPROBANTE='{id_ticket}'""")

        def convert_decimal_to_float(d):
            if isinstance(d, decimal.Decimal):
                return float(d)
            return d

        if result and isinstance(result, list) and result:
            item = result[0]
            task = {key: convert_decimal_to_float(value) for key, value in item.items()}
        

        if error:
            return set_response(task, 404, "error")
        
        
        print(set_response(task))


        return set_response(task)

    @route("/update_task", methods=["PUT"])
    def update_task(self):
        data = request.get_json()
        idcomprobante = data.get('idcomprobante', '')
        customerCode = data.get('cuenta', '')
        statusCode = data.get('idestado', '')
        category = data.get('idtarea', '')
        technician = data.get('idtecnico', '')
        date = datetime.now().strftime('%d-%m-%Y')
        content = ''
        taskTime = 0
        customerName = ''

        sql = f"""
            DECLARE @pRes INT;
            DECLARE @pIdCpte NVARCHAR(13);
            DECLARE @pMensaje NVARCHAR(250);

            SET @pIdCpte = '{idcomprobante}';
            SET NOCOUNT ON;

            EXEC sp_web_setExtensionTareas 
                '{technician}', 
                '{customerCode}', 
                '{date}', 
                '{content}', 
                '{statusCode}', 
                {taskTime}, 
                '{customerName}', 
                '{category}', 
                @pIdCpte OUTPUT, 
                @pRes OUTPUT, 
                @pMensaje OUTPUT;

            SELECT @pIdCpte, @pRes, @pMensaje;
            """

        
        result, error = exec_alfa_sql(sql, "", True)

        if error:
            Log.create(result)
            return set_response([], 404, "error")
        
        ticket_id = result[0][0]        

        return set_response({'ticket': ticket_id })
