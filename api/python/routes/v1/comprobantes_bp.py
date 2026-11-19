from flask import Blueprint, jsonify, request
from model import db_setPedidos, db_save_cobranza, db_getCptes, db_getPedidoDetalle, db_getMediosPago, db_getServicios, db_save_tarea

from auth import validate_api_key

comprobantes_bp = Blueprint('comprobantes_bp', __name__)

# PEDIDOS


@comprobantes_bp.route('/setPedidos/', methods=['POST'])
@validate_api_key
def setPedido():
    if(db_setPedidos(request.json)):
        return "1"
    else:
        return "0"


@comprobantes_bp.route('/getPedidos/<string:vdor>/<string:fhd>/<string:fhh>/<string:idcliente>')
@validate_api_key
def get_pedidos(vdor, fhd, fhh, idcliente):
    return jsonify(db_getCptes('NP', vdor, fhd, fhh, idcliente))


@comprobantes_bp.route('/getPedidoDetalle/<string:tc>/<string:idcpte>')
@validate_api_key
def get_pedido_detalle(tc, idcpte):
    return jsonify(db_getPedidoDetalle(tc, idcpte))

# COBRANZAS


@comprobantes_bp.route('/setCobranzas/', methods=['POST'])
@validate_api_key
def save_cobranza():
    if(db_save_cobranza(request.json)):
        return "1"
    else:
        return "0"


@comprobantes_bp.route('/getCobranzas/<string:vdor>/<string:fhd>/<string:fhh>/<string:idcliente>')
@validate_api_key
def get_cobranzas(vdor, fhd, fhh, idcliente):
    return jsonify(db_getCptes('CB', vdor, fhd, fhh, idcliente))


@comprobantes_bp.route('/getMediosPago')
@validate_api_key
def get_medios_pago():
    return jsonify(db_getMediosPago())


# TAREAS

@comprobantes_bp.route('/getTareas/<string:vdor>/<string:fhd>/<string:fhh>/<string:idcliente>')
@validate_api_key
def get_tareas(vdor, fhd, fhh, idcliente):
    return jsonify(db_getCptes('OT', vdor, fhd, fhh, idcliente))

@comprobantes_bp.route('/getServicios')
# @validate_api_key
def get_servicios():
    return jsonify(db_getServicios())


@comprobantes_bp.route('/setTareas/', methods=['POST'])
@validate_api_key
def save_tarea():
    if(db_save_tarea(request.json)):
        return "1"
    else:
        return "0"
