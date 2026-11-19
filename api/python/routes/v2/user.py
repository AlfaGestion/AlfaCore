from functions.general import get_format_response
from flask import request

from functions.general_customer import decrypt_password
from .master import MasterView
from configs.connection import conn

# from rich import print

from functions.responses import set_response


class UserView(MasterView):

    def index(self):

        query = f"""
        SELECT ltrim(nombre) as nombre FROM TA_USUARIOS 
        WHERE SISTEMA='CN000pr' AND (IDVENDEDOR='' OR IDVENDEDOR IS NULL) 
        AND (nombre<>'Sistemas' and nombre<>'SUPERVISOR NETWARE MC' and nombre<>'G_ADMINISTRADORES')"""

        users = self.get_response(query, "Ocurrio un error al consultar los usuarios.", True)

        users, error = self.__get_user_info(users_list=users)

        if error:
            return set_response(None, 500, "Ocurrio un error al obtener la información de los contactos.")

        # print(users)
        return set_response(users, 200)

    def get(self, id_contact: str):

        query = f"""SELECT id,Nombre_y_Apellido as nombre FROM MA_CONTACTOS WHERE id={id_contact} AND (CuentaRel = '' or CuentaRel is null)"""
        contacts = self.get_response(query, "", True)

        contacts, error = self.__get_user_info(contact_list=contacts, id_contact=id_contact)

        if error:
            return set_response(None, 500, "Ocurrio un error al obtener la información de los contactos.")

        return set_response(contacts, 200)

    def post(self):

        data = request.get_json()

        name = data.get('name', '')
        user = data.get('user', '')
        is_admin = data.get('isAdmin', False)

        # Verifico existencia de user, no puede haber otro igual, aunque sea de otro usuario
        if self.__user_exists(user, name):
            return set_response(None, 400, f"No se puede utilizar {user}. Intente con otro, preferentemente un email.")

        password = self.__get_password_user(name)

        query = f"""
            IF EXISTS(SELECT * FROM [users] WHERE [name]='{name}' and idcliente='{self.code_account}')
                BEGIN
                    UPDATE [users] SET [user]='{user}', isAdmin={1 if is_admin else 0}, password='{password}' WHERE [name]='{name}' and idcliente='{self.code_account}'
                END
            ELSE
                BEGIN
                    INSERT INTO [users] ([user], [name], isAdmin, idcliente, password) VALUES ('{user}', '{name}', {1 if is_admin else 0}, '{self.code_account}','{password}')
                END
        """
        # print(query)
        try:
            conn.cursor().execute(query)
            conn.commit()
            return set_response(None, 200, "")

        except Exception as e:
            self.log(f"Error al registrar el usuario {user} : {e}\nQuery: {query}")
            return set_response(None, 500, f"Ocurrió un error. Intente nuevamente")

    def __get_password_user(self, name: str) -> str:
        query = f"""
        SELECT password FROM TA_USUARIOS 
        WHERE SISTEMA='CN000pr' AND (IDVENDEDOR='' OR IDVENDEDOR IS NULL) 
        AND nombre='{name}'"""

        user = self.get_response(query, f"Ocurrió un error al obtener la contraseña de {name}.", True)

        if user.__len__() > 0:
            return decrypt_password(user[0]['password'])

        return ""

    def __user_exists(self, username: str, name: str) -> bool:

        query = f"SELECT * FROM [users] WHERE [user]='{username}' "
        users, error = get_format_response(query, " al verificar el usuario", True)

        if error:
            return True

        if users.__len__() > 0:
            # Verifico si ya existe, si es de la misma cuenta
            if users[0]['idcliente'] != self.code_account:
                return True

            # Si es de la misma cuenta, verifico que sea el que esta editando ahora, y no asigne el mismo usuario a dos distintos
            if users[0]['name'] != name:
                return True

            return False

        return False
        # return True if users.__len__() > 0 else False

    def __get_user_info(self, users_list: list, user_name: str = '') -> list:

        where_clause = ''
        if user_name:
            where_clause = f" and name={user_name}"

        users_registered, error = get_format_response(
            f"SELECT * FROM [users] WHERE idcliente='{self.code_account}' and [user]<>'{self.code_account}' {where_clause}", " al obtener los usuarios", True)

        if error:
            return [], True

        for user in users_list:
            username = ''
            is_admin = False
            # user['password'] = decrypt_password(user['password'])
            for user_registered in users_registered:
                if user['nombre'] == user_registered['name']:
                    username = user_registered['user']
                    is_admin = user_registered['isAdmin']
                    break

            user['user'] = username
            user['isAdmin'] = is_admin

        return users_list, False

    # def post(self):
    #     tasks = request.get_json()

    #     for task in tasks:
    #         account = task.get('account', '')
    #         seller = task.get('seller', '')
    #         date = task.get('date', datetime.now().strftime('%Y-%m-%d'))
    #         obs = task.get('obs', '')
    #         # sign = task.get('sign', '')
    #         id_task = task.get('task', '')

    #         date = datetime.strptime(date, '%Y-%m-%d').strftime('%d/%m/%Y')

    #         sql = f"""
    #         DECLARE @pRes INT
    #         DECLARE @pMensaje NVARCHAR(250)
    #         set nocount on; EXEC sp_web_setTareas '{account}','{seller}','{date}','{obs}','{id_task}',@pRes OUTPUT, @pMensaje OUTPUT
    #         SELECT @pRes as pRes, @pMensaje as pMensaje
    #         """

    #         result, error = exec_customer_sql(sql, "", self.token_global)

    #         if error:
    #             return set_response(None, 404, "Ocurrió un error al grabar la tarea.")

    #     return set_response([], 200, "Tareas grabados correctamente.")
