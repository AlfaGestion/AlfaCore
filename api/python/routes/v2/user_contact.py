from inspect import _void
from functions.general import get_format_response
from .master import MasterView
from flask_classful import route
from flask import request

from rich import print

from functions.responses import set_response


class UserContactView(MasterView):

    @route('/<string:customer_id>/all')
    def get_contacts_by_customer(self, customer_id: str):

        self.__verify_table_and_create_fields()

        query = f"""SELECT id,isnull(Nombre_y_Apellido,'') as nombre,isnull(email,'') as email,isnull(claveweb,'') as password,isnull(admin,0) as isAdmin FROM MA_CONTACTOS WHERE CuentaRel = '{customer_id}'"""
        contacts = self.get_response(query, f"Ocurrió un error al consultar los contactos de la cuenta {customer_id}.", True)

        return set_response(contacts, 200)

    def get(self, id_contact: str):

        query = f"""SELECT id,Nombre_y_Apellido as nombre FROM MA_CONTACTOS WHERE id={id_contact} AND (CuentaRel = '' or CuentaRel is null)"""
        contacts = self.get_response(query, "", True)

        contacts, error = self.__get_user_info(contact_list=contacts, id_contact=id_contact)

        if error:
            return set_response(None, 500, "Ocurrio un error al obtener la información de los contactos.")

        return set_response(contacts, 200)

    def put(self):

        data = request.get_json()

        name = data.get('name', '')
        account = data.get('account', '')
        email = data.get('email', '')
        is_admin = data.get('isAdmin', 0)
        password = data.get('password', '1')

        is_admin = 1 if is_admin == True else 0

        if self.__contact_email_exists(email, account) or self.__contact_email_exists(email, account, True, name):
            return set_response(None, 400, f"No se puede utilizar {email}. Ya está en uso. Intente con otro.")

        query = f"""
            IF EXISTS(SELECT * FROM MA_CONTACTOS WHERE Nombre_y_Apellido = '{name}' and CuentaRel='{account}')
                BEGIN
                    UPDATE MA_CONTACTOS SET Nombre_y_Apellido = '{name}', email = '{email}', claveweb = '{password}', admin = {is_admin}
                    WHERE Nombre_y_Apellido = '{name}' and CuentaRel='{account}'
                END
            ELSE
                BEGIN
                    INSERT INTO MA_CONTACTOS (Nombre_y_Apellido, CuentaRel, email, claveweb, admin) 
                    VALUES ('{name}', '{account}', '{email}', '{password}', {is_admin})
                END
        """

        contact = self.get_response(query, "Error al actualizar acceso", False, True)

        return set_response(contact, 200)

    def post(self):

        data = request.get_json()

        name = data.get('name', '')
        account = data.get('account', '')
        email = data.get('email', '')
        is_admin = data.get('isAdmin', 0)
        password = data.get('password', '1')

        is_admin = 1 if is_admin == True else 0

        # Verifico que no exista el usuario en la cuenta
        if self.__contact_name_exists(name, account):
            return set_response(None, 400, f"No se puede utilizar {name}. Ya está en uso. Intente con otro.")

        # Verifico que el email no esté en uso
        if self.__contact_email_exists(email, account) or self.__contact_email_exists(email, account, True):
            return set_response(None, 400, f"No se puede utilizar {email}. Ya está en uso. Intente con otro.")

        query = f"""
            INSERT INTO MA_CONTACTOS (Nombre_y_Apellido, CuentaRel, email, claveweb, admin) 
            VALUES ('{name}', '{account}', '{email}', '{password}', {is_admin})
        """

        contact = self.get_response(query, "Error al crear el acceso", False, True)

        return set_response(contact, 200)

    def delete(self):

        data = request.get_json()
        name = data.get('name', '')
        account = data.get('account', '')

        if name and account:
            query = f"""
            DELETE FROM MA_CONTACTOS WHERE Nombre_y_Apellido = '{name}' and CuentaRel='{account}'
            """

            contact = self.get_response(query, "Error al actualizar acceso", False, True)

            return set_response(contact, 200)

        return set_response([], 500, "Debe informar el nombre y la cuenta para eliminar.")

    def __contact_email_exists(self, email: str, account: str, same_account: bool = False, name: str = '') -> bool:

        if same_account:
            query = f"SELECT * FROM MA_CONTACTOS WHERE email='{email}' and CuentaRel='{account}' and Nombre_y_Apellido != '{name}'"
        else:
            query = f"SELECT * FROM MA_CONTACTOS WHERE email='{email}' and CuentaRel<>'{account}'"

        contact = self.get_response(query, "Error al consultar contacto", True)

        return True if contact.__len__() > 0 else False

    def __contact_name_exists(self, name: str, account: str) -> bool:

        query = f"SELECT * FROM MA_CONTACTOS WHERE ltrim(Nombre_y_Apellido) = '{name.strip()}' and CuentaRel = '{account}'"
        contact = self.get_response(query, "Error al consultar contacto", True)

        return True if contact.__len__() > 0 else False

    def __verify_table_and_create_fields(self) -> _void:
        # Intento crear los campos en la base
        query = f"""
        IF NOT EXISTS
        (
            SELECT * FROM INFORMATION_SCHEMA.COLUMNS
            WHERE COLUMN_NAME = 'claveweb' AND TABLE_NAME='MA_CONTACTOS'
        )
        BEGIN
            ALTER TABLE MA_CONTACTOS
            ADD claveweb VARCHAR(50) NULL,admin BIT null;
        END
        """

        result = self.get_response(query, "Error al crear los campos", False, True)
