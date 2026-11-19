from flask import jsonify, request
from flask_classful import FlaskView, route
from functions.auth import is_valid_account, register_session
from functions.general_customer import (forgot_password_customer,
                                        forgot_password_seller,
                                        is_valid_account_customer,
                                        is_valid_account_driver,
                                        is_valid_account_public,
                                        is_valid_autologin_contact_customer,
                                        is_valid_account_seller,
                                        autologin_customer_for_odoo)
from functions.jwt import validate_token, write_token
from functions.responses import set_response
from functions.User import User
from functions.Alfa.Customer import Customer
from functions.Log import Log


CUSTOMER_ACCESS = 'c'
SELLER_ACCESS = 's'
TRANSPORT_ACCESS = 't'
PUBLIC_ACCESS = 'p'
ADMIN_ACCESS = 'a'
AUTOLOGIN_CUSTOMER_ACCESS = 'y'


class AuthView(FlaskView):

    @route("/login", methods=["POST"])
    def login(self):
        data = request.get_json()
        typeAccount = data.get("type", ADMIN_ACCESS)
        odoo = data.get("odoo", False)

        if odoo is True:
            error = autologin_customer_for_odoo(data)
            if error:
                return set_response(None, 404, "Usuario o contraseña incorrectos.")
            return data

        error = False
        if typeAccount == SELLER_ACCESS:
            error, message = is_valid_account_seller(data)
            if error:
                return set_response(None, 404, message or "Usuario o contraseña de vendedor incorrectos.")
            return data

        elif typeAccount == CUSTOMER_ACCESS:
            error = is_valid_account_customer(data)
            if error:
                return set_response(None, 404, "Usuario o contraseña incorrectos.")
            return data

        elif typeAccount == AUTOLOGIN_CUSTOMER_ACCESS:
            error = is_valid_autologin_contact_customer(data)
            if error:
                return set_response(None, 404, "No tiene permitido iniciar sesión")
            return data

        elif typeAccount == TRANSPORT_ACCESS:
            error = is_valid_account_driver(data)
            if error:
                return set_response(None, 404, "Usuario o contraseña de chofer incorrectos.")
            return data

        elif typeAccount == PUBLIC_ACCESS:
            error = is_valid_account_public(data)
            if error:
                return set_response(None, 404, "Acceso inválido")
            return data

        else:
            error = is_valid_account(data)

        if not error:
            token = write_token(data)
            str_token = token.decode('UTF-8')

            if register_session(str_token, data):
                data["token"] = str_token
                return data
            else:
                return set_response(None, 404, "No se pudo crear la sesión.")
        else:
            return set_response(None, 404, "Usuario o contraseña incorrectos.")


    @route("/verify/token")
    def verify(self):
        token = request.headers['Authorization'].split(" ")[1]
        Log.create(token)
        # return validate_token(token, output=True)
        error, result = validate_token(token, output=True)

        result['password'] = ""
        return result

    @route("/forgot_password", methods=["POST"])
    def forgot_password(self):
        data = request.get_json()
        type = data.get("type", 'a')
        # email = data["email"]
        password_data = []
        if type == 's':
            password_data = forgot_password_seller(data)
        elif type == 'c':
            password_data = forgot_password_customer(data)

        return jsonify(password_data)

    @route("/register", methods=["POST"])
    def register_user(self):
        data = request.get_json()

        name = data.get('name', '')
        phone = data.get('phone', '')
        email = data.get('email', '')
        cuit = data.get('cuit', '')
        iva = data.get('iva', '')
        password = data.get('password', '')

        try:
            if User.exist(email):
                return set_response(None, 404, "El email ya se encuentra registrado.")

            customer = Customer()
            # Creo el cliente
            if not customer.create(name=name, email=email, phone=phone, cuit=cuit, iva=iva):
                Log.create("No se ha podido crear el cliente.")
                return set_response(None, 404, "No se ha podido crear el cliente.")

            # Registro el usuario
            user = User(customer)
            user.password = password
            if not user.register():
                Log.create("No se ha podido crear el cliente.")
                return set_response(None, 404, "No se ha podido crear el cliente.")

            return set_response(None, 200, "Se ha registrado con éxito.")
        except Exception as e:
            Log.create(e)
            return set_response(None, 404, "Ocurrió un error al registrar el usuario.")

    @route("/verify_account/<string:code>")
    def verify_account(self, code: str):

        verify = User.verify(code)

        if verify:
            return set_response(None, 200, "Se ha verificado correctamente.")
        else:
            return set_response(None, 404, "Error al verificar la cuenta. Intente más tarde.")

    @route("/autologinOdoo/<string:code>/<string:iddb>", methods=["POST"])
    def autologin(self):
        data = request.get_json()

        error = autologin_customer_for_odoo(data)
        if error:
            return set_response(None, 404, "Usuario o contraseña incorrectos.")
        return data
