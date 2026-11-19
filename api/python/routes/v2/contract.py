from datetime import datetime

from flask import request
from flask_classful import FlaskView, route
from functions.general_customer import (decode_id_account,
                                        get_customer_response,
                                        get_last_contract_account, update_contract)
from functions.responses import set_response


class ContractView(FlaskView):

    @route('/get/<string:account>/<int:db>/<int:id_contact>')
    def get_contract(self, account: str, db: int, id_contact: int):

        account = decode_id_account(account)

        data = {
            'id_contact': id_contact,
            'username': account,
            'account': account,
            'alfaCustomerId': account,
            'databaseId': db,
            'password': '1',
        }

        error, result = get_last_contract_account(data)
        if error:
            return set_response(None, 404, result)

        return set_response(result, 200)

    def post(self):
        data = request.get_json()

        name = data.get('name', '')
        phone = data.get('phone', '')
        email = data.get('email', '')
        selection = data.get('selection', 'Ninguno')
        id_file = data.get('id')
        comments = data.get('comments', '')
        account = data.get('account')
        id_database = data.get('databaseId')
        id_contact = data.get('idContact')

        account = decode_id_account(account)

        data = {
            'id_contact': id_contact,
            'username': account,
            'account': account,
            'alfaCustomerId': account,
            'databaseId': id_database,
            'password': '1',
        }

        payload = {
            'name': name,
            'phone': phone,
            'email': email,
            'selection': selection,
            'id_file': id_file,
            'comments': comments
        }

        error = update_contract(data, payload)
        if error:
            return set_response(None, 400, [])

        return set_response([], 200)
