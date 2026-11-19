from flask import request, jsonify
from functions.general_customer import exec_customer_sql, get_customer_response
from functions.responses import set_response
from .master import MasterView
from functions.User import User
from functions.Alfa.Customer import Customer
from functions.Log import Log

class RegisterView(MasterView):
    def post(self):
        data = request.get_json()

        name = data.get('name', '')
        phone = data.get('phone', '')
        email = data.get('email', '')
        cuit = data.get('cuit', '')
        iva = data.get('iva', '')
        password = data.get('password', '')

        Log.create(name, 'ERROR')


        customer = Customer(self.token_global)
        result = customer.create(
            name=name, email=email, phone=phone, cuit=cuit, iva=iva)

        # print(result)
        # User.register(name=name, phone=phone, email=email, cuit=cuit, iva=iva, password=password)

        return jsonify({'status': 'ok'})

        # password_data = []
        # if type == 's':
        #     password_data = forgot_password_seller(data)
        # elif type == 'c':
        #     password_data = forgot_password_customer(data)

        # return jsonify(password_data)
