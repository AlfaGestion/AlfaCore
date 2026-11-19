from flask import Blueprint, jsonify
from model import db_getClientes, db_getCtaCte

from auth import validate_api_key

clientes_bp = Blueprint('clientes_bp', __name__)

@clientes_bp.route('/getClientes') #Todos los clientes
@validate_api_key
def get_clientes_all():
    return jsonify(db_getClientes(-1))    

@clientes_bp.route('/getClientesD/<int:page>')
@validate_api_key
def get_clientes_d(page):
    return jsonify(db_getClientes(page))

@clientes_bp.route('/getCtaCte/<string:idcliente>/<string:fhd>/<string:fhh>/<int:soloPendiente>')
@validate_api_key
def get_cta_cte(idcliente,fhd,fhh,soloPendiente):
    return jsonify(db_getCtaCte(idcliente,fhd,fhh,soloPendiente))
