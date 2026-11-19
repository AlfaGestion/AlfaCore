from functions.general import get_format_response
from configs.connection import conn


def user_exists(account: str, username: str) -> bool:
    """
    Verifica si existe un usuario
    """
    result = []

    sql = f"""
    SELECT [user] FROM users where idcliente='{account}' and [user]='{username}'
    """
    result, _ = get_format_response(
        sql, " el usuario (verifica existencia)", True)

    return True if result else False


def create_new_user(account: str, username: str, password: str) -> bool:

    cursor = conn.cursor().execute(
        f"""
            INSERT INTO users (idcliente,[user],password)
            values ('{account}','{username}','{password}')
        """
    )
    conn.commit()

    return user_exists(account, username)


def update_user(id: int, username: str, password: str):
    cursor = conn.cursor().execute(
        f"""
            UPDATE users SET [user] = '{username}', password = '{password}'
            WHERE id={id}
        """
    )
    conn.commit()


def delete_user(id: int):
    cursor = conn.cursor().execute(
        f"""
            DELETE users WHERE id={id}
        """
    )
    conn.commit()


# def update_account(account, name):
#     cursor = conn.cursor().execute(
#         f"""
#             UPDATE bases SET dbname = '{self.dbname}', dbpassword = '{self.dbpassword}',
#             dbuser='{self.dbuser}', dbserver='{self.dbserver}', nombre='{self.name}'
#             WHERE id={id}
#         """
#     )
#     conn.commit()


class Account():

    def __init__(self, account: str, name: str):
        self.account = account
        self.name = name

    def exists(self) -> bool:
        result = []

        sql = f"""
        SELECT idcliente FROM clientes where idcliente='{self.account}'
        """
        result, _ = get_format_response(
            sql, " la cuenta (verifica existencia)", True)

        return True if result else False

    def save(self) -> bool:
        cursor = conn.cursor().execute(
            f"""
            INSERT INTO clientes (idcliente,nombre,password,superadmin)
            values ('{self.account}','{self.name}','',0)
        """
        )
        conn.commit()

        return self.exists()

    def update(self):
        try:
            conn.cursor().execute(
                f"""
                UPDATE clientes SET nombre = '{self.name}'
                WHERE idcliente={self.account}
            """
            )
            conn.commit()
        except Exception as ex:
            print("Error al actualizar cuenta :", ex)


class Database():

    def __init__(self, account: str, name: str, dbname: str, dbuser: str, dbpassword: str, dbserver: str, path: str = ''):
        self.account = account
        self.dbname = dbname
        self.dbuser = dbuser
        self.dbpassword = dbpassword
        self.dbserver = dbserver
        self.name = name
        self.path = path

    def save(self) -> bool:
        cursor = conn.cursor().execute(
            f"""
            INSERT INTO bases (idcliente,nombre,dbserver,dbname,dbuser,dbpassword,path) 
            values ('{self.account}','{self.name}','{self.dbserver}','{self.dbname}','{self.dbuser}','{self.dbpassword}','{self.path}')
        """
        )
        conn.commit()

        return self.exists()

    def exists(self) -> bool:
        result = []

        sql = f"""
        SELECT dbname FROM bases where idcliente='{self.account}' and dbname='{self.dbname}' and dbserver='{self.dbserver}'
        """
        result, _ = get_format_response(
            sql, " la base de datos (verifica existencia)", True)

        return True if result else False

    def update(self, id: int):
        cursor = conn.cursor().execute(
            f"""
            UPDATE bases SET dbname = '{self.dbname}', dbpassword = '{self.dbpassword}',
            dbuser='{self.dbuser}', dbserver='{self.dbserver}', nombre='{self.name}',path='{self.path}'
            WHERE id={id}
        """
        )
        conn.commit()

    @staticmethod
    def delete(id: int):
        cursor = conn.cursor().execute(f"DELETE bases WHERE id={id}")
        conn.commit()
