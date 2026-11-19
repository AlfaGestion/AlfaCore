from datetime import datetime
from functions.general_customer import get_customer_response
from functions.responses import set_response
from flask_classful import route
from flask import request
from ..master import MasterView


class CustomerTransportView(MasterView):
    @route('/<string:ride>')
    def get_customer_by_ride(self, ride: str):
        query = f"EXEC sp_web_getClientesTransporte '{ride}'"
        response = self.get_response(query, f"Ocurrió un error al obtener los clientes de transporte", True, False)
        return set_response(response, 200)

    def index(self):
        query = "EXEC sp_web_getClientesTransporte"
        response = self.get_response(query, f"Ocurrió un error al obtener los clientes de transporte", True, False)
        return set_response(response, 200)

    @route('/<string:customer>/receiver')
    def get_receivers(self, customer: str):
        query = f"EXEC sp_web_getDestinatariosTransporte '{customer}'"
        response = self.get_response(query, f"Ocurrió un error al obtener los clientes de transporte", True, False)
        return set_response(response, 200)
