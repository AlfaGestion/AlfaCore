from flask import Blueprint, jsonify, request

from functions.general import get_format_response
from functions.responses import set_response

from functions.admin import user_exists, create_new_user, update_user, delete_user, Database, Account

code_account = ''  # '112010001'
token_global = ''  # 'eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJ1c2VybmFtZSI6IjExMjAxMDAwMSIsInBhc3N3b3JkIjoiMSIsImV4cCI6MTYzODYzNTM0M30.IFqRDIZghAxeJn6yfH9jco58weqFtZeTOEWBL9VRCYo'

"""
Blueprint utilizado para todo lo relacionado la cuenta
"""
admin = Blueprint('admin', __name__)


# @admin.before_request
# def verify_token_middleware():

#   valid = False

#    if 'Authorization' in request.headers:
#         token = request.headers['Authorization'].split(" ")[1]
#         valid, response = validate_token(token, output=True)
#     else:
#         print("no tiene token")
#         response = jsonify({"message": "Invalid Token"})
#         response.status_code = 401

#     if not valid:
#         return response
#     else:
#         global code_account
#         global token_global

#         code_account = response['account']
#         token_global = token


"""
Method: GET
End point api/v2/admin/customers
Retorna los clientes
"""


@admin.route('/admin/customers')
def getCustomers():
    """
    Retorna los clientes
    """
    result = []

    sql = f"""
    SELECT idcliente, nombre, password
    FROM clientes
    """

    result, _ = get_format_response(
        sql, " el detalle de ventas por comprobante", True)

    return jsonify(result)


@admin.route('/admin/customer/<string:account>', methods=['GET'])
def getCustomer(account: str):
    """
    Method: GET \n
    End point api/v2/admin/customer/<string:account>\n
    Retorna la información de un cliente especifico\n
    """
    response = []
    info = []
    databases = []
    users = []

    sql = f"""
    SELECT idcliente, nombre, password, superadmin
    FROM clientes where idcliente='{account}'
    """

    info, _ = get_format_response(
        sql, " la información del cliente", True)

    sql = f"""
        SELECT id,idcliente, nombre, dbserver, dbname, dbuser, dbpassword,path
        FROM bases where idcliente='{account}'
        """
    databases, _ = get_format_response(
        sql, " el detalle de las bases", True)

    sql = f"""
        SELECT id,idcliente, [user], password
        FROM users where idcliente='{account}'
        """
    users, _ = get_format_response(
        sql, " el detalle de los usuarios", True)

    response.append({
        'info': info,
        'databases': databases,
        'users': users
    })

    return jsonify(response)


@admin.route('/admin/customer/create', methods=['POST'])
def create_account():
    """
    Method: POST \n
    End point api/v2/admin/customer/create\n
    Crea una cuenta\n
    """
    data = request.json
    name: str = data.get('name', '')
    code: str = data.get('account', '')

    if len(code) != 9:
        return set_response(None, 404, "Longitud de código inválida. Debe ser de 9 digítos.")

    ac = Account(code, name)
    if not ac.exists():
        try:
            if ac.save():
                return set_response(None, 200, "Cuenta creada correctamente")
            else:
                return set_response(None, 404, "Ocurrió un error al crear la cuenta")
        except Exception as ex:
            print(ex)
            return set_response(None, 404, "Ocurrió un error al crear la cuenta")

    return set_response(None, 404, f"Ya existe una cuenta con el código {code}")


@admin.route('/admin/customer/<string:account>', methods=['POST'])
def set_account_info(account: str):
    """
    Method: POST \n
    End point api/v2/admin/customer/<string:account>\n
    Grabo datos cuenta\n
    """
    data = request.json
    name: str = data.get('name', '')

    try:
        ac = Account(account, name)
        ac.update()
        return set_response(None, 200, "Cuenta actualizada correctamente")

    except Exception as ex:
        print(ex)
        return set_response(None, 404, "Ocurrió un error al actualizar la cuenta")


@admin.route('/admin/customer/user', methods=['POST'])
def delete_user_account():
    """
    Method: POST \n
    End point api/v2/admin/customer/user\n
    Elimina un usuario\n
    """

    data = request.json
    id: int = data.get('id', 0)
    print(data)
    if id > 0:
        try:
            delete_user(id)
            return set_response(None, 200, "Usuario eliminado correctamente")

        except Exception as ex:
            print(ex)
            return set_response(None, 404, "Ocurrió un error al eliminar el usuario")

    return set_response(None, 404, "No selecciono ningun usuario")


@admin.route('/admin/customer/<string:account>/user', methods=['POST'])
def set_user(account: str):
    """
    Method: POST \n
    End point api/v2/admin/customer/<string:account>/user\n
    Grabar un usuario\n
    """
    data = request.json
    username: str = data.get('username', '')
    password: str = data.get('password', '')
    id: int = data.get('id', 0)

    if not username or not password:
        return set_response(None, 404, "Debe informar el usuario y la contraseña")

    if id > 0:
        try:
            update_user(id, username, password)
            return set_response(None, 200, "Usuario actualizado correctamente")

        except Exception as ex:
            print(ex)
            return set_response(None, 404, "Ocurrió un error al actualizar el usuario")

    if user_exists(account, username):
        return set_response(None, 404, "El usuario ya existe")

    if not create_new_user(account, username, password):
        return set_response(None, 404, "Ocurrió un error al crear el usuario")

    return set_response(None, 200, "Usuario creado correctamente")


@admin.route('/admin/customer/db', methods=['POST'])
def delete_database_account():
    """
    Method: DELETE \n
    End point api/v2/admin/customer/db\n
    Elimina una base de datos\n
    """

    data = request.json
    id: int = data.get('id', 0)

    if id > 0:
        try:
            Database.delete(id)
            return set_response(None, 200, "Base de datos eliminada correctamente")

        except Exception as ex:
            print(ex)
            return set_response(None, 404, "Ocurrió un error al eliminar la base de datos")

    return set_response(None, 404, "No selecciono ninguna base de datos")


@admin.route('/admin/customer/<string:account>/db', methods=['POST'])
def set_db(account: str):
    """
    Method: POST \n
    End point api/v2/admin/customer/<string:account>/db\n
    Gestion dbs\n
    """
    data = request.json
    dbname: str = data.get('dbname', '')
    dbserver: str = data.get('dbserver', '')
    dbpassword: str = data.get('dbpassword', '')
    dbuser: str = data.get('dbuser', '')
    id: int = data.get('id', 0)
    path: str = data.get('path', '')
    name: str = data.get('name', '')

    db = Database(account, name, dbname, dbuser, dbpassword, dbserver, path)
    if id > 0:
        try:
            db.update(id)
            return set_response(None, 200, "Base actualizada correctamente")

        except Exception as ex:
            print(ex)
            return set_response(None, 404, "Ocurrió un error al actualizar la base")

    if not dbname or not dbserver or not dbuser or not dbpassword:
        return set_response(None, 404, "Debe informar todos los datos de la base de datos")

    if db.exists():
        return set_response(None, 404, "La base de datos ya existe")

    if not db.save():
        return set_response(None, 404, "Ocurrió un error al crear la base de datos")

    return set_response(None, 200, "Base de datos creada correctamente")
