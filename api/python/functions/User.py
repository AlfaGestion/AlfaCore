from config import BASE_URL
from configs.connection import conn
from functions.DataBase import DataBase
from functions.Email import Email, Log
from datetime import datetime
from functions.Alfa.Customer import Customer

import uuid

TYPE_MICROEMPRENDEDOR = 'M'
TYPE_NORMALUSER = 'A'


class User:
    password = ""
    name = ""
    account = ""
    email = ""
    code_verify = ""

    def __init__(self, customer):
        self.customer = customer
        self.account = self.customer.code
        self.name = self.customer.name
        self.email = self.customer.email

    def register(self, is_microemprendedor=True) -> bool:
        """
        Se encarga de crear el cliente en la base central, crear la base, 
        luego registrar el usuario y la base de datos
        """
        self.is_microemprendedor = is_microemprendedor

        # Crear usuario en base, con el idcliente
        self.create_custommer()

        # Registro el usuario
        self.register_user_in_db()

        # Creo la base de datos para el usuario
        # db = DataBase(self.customer)
        # if not db.create():
        #     self.rollback()
        #     return False

        self.notify()
        return True

    def rollback(self):
        # Elimino usuario y cliente
        cursor = conn.cursor().execute(
            f"""
            DELETE FROM clientes WHERE idcliente='{self.account}'
            DELETE FROM [users] WHERE idcliente='{self.account}'
            """)
        conn.commit()

    def notify(self):
        # Envia email para verificar
        body_html = f"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8" />
            <meta http-equiv="X-UA-Compatible" content="IE=edge" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>Document</title>
        </head>
        <body>
            <div style="border: 1px solid #e1e1e1; padding: 10px; text-align: center">
                <h2>Confirmación de cuenta Alfa Net</h2>

                <p>Gracias por su registro en Alfa Net. Solo queda confirmar su cuenta.</p>
                <p>Presione el siguiente link para confirmar su cuenta.</p>

                <a style="padding: 10px; background-color: #0075b8; border-radius: 10px; color: #ffffff; text-decoration: none" href="{BASE_URL}verify/{self.code_verify}">Confirmar cuenta</a>

                <p>Si no funciona, puede copiar el siguiente link y pegarlo en su navegador.</p>
                <p>{BASE_URL}verify/{self.code_verify}</p>
            </div>
        </body>
        </html>
        """
        Email.send_email(
            self.email, "Verificación cuenta Alfa Net", body_html)

    def create_custommer(self):
        try:
            now = datetime.now()
            now = now.strftime("%d/%m/%Y")

            code_verify = uuid.uuid4().hex
            cursor = conn.cursor().execute(
                f"""
                INSERT INTO clientes (idcliente,nombre,password,superadmin,verified,type,verified_code,created)
                VALUES ('{self.account}','{self.name}','{self.password}',0,0,'{TYPE_MICROEMPRENDEDOR if self.is_microemprendedor else TYPE_NORMALUSER}','{code_verify}','{now}')
                """)
            conn.commit()

            self.code_verify = code_verify
        except Exception as e:
            Log.create(e, '', 'ERROR')

    def register_user_in_db(self):

        try:
            cursor = conn.cursor().execute(
                f"""
                INSERT INTO [users] (idcliente,[user],password,isAdmin,name)
                VALUES ('{self.account}','{self.email}','{self.password}',1,'{self.name}')
                """)
            conn.commit()
        except Exception as e:
            Log.create(e, '', 'ERROR')

    @staticmethod
    def verify(verify_code: str) -> bool:
        code_account = ""
        # Reviso si hay alguna cuenta con el codigo de verificacion
        exists = False
        cursor = conn.cursor().execute(
            f"SELECT * FROM clientes WHERE verified_code = '{verify_code}' and verified=0")
        for row in cursor.fetchall():
            code_account = row.idcliente
            exists = True

        if not exists:
            return False

        try:
            cursor = conn.cursor().execute(
                f"UPDATE clientes set verified=1 WHERE verified_code = '{verify_code}'")
            conn.commit()

            customer = Customer()
            customer.load(code_account)
            # Creo la base de datos para el usuario
            db = DataBase(customer)
            if not db.create():
                user = User(customer)
                user.rollback()
                return False

        except Exception as e:
            Log.create(e, '', 'ERROR')
            return False

        return True

    @staticmethod
    def exist(username: str) -> bool:
        # Verifica si el email ya está registrado
        cursor = conn.cursor().execute(
            f"SELECT idcliente,password FROM [users] WHERE ltrim(rtrim([user])) = ltrim(rtrim('{username}'))")

        result = cursor.fetchall()
        return len(result) > 0
