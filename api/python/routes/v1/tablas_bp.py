from flask import Blueprint, jsonify
from model import db_getRegistros, db_getDepositos, db_getRubros, db_getFamilias, db_getVendedores, db_get_visitas_vendedor

from auth import validate_api_key

tablas_bp = Blueprint('tablas_bp', __name__)


@tablas_bp.route('/getRegistros')
@validate_api_key
def get_registros():
    return jsonify(db_getRegistros())


@tablas_bp.route('/getDepositosD')
@validate_api_key
def get_depositos():
    return jsonify(db_getDepositos())


@tablas_bp.route('/getRubrosD')
@validate_api_key
def get_rubros():
    return jsonify(db_getRubros())


@tablas_bp.route('/getFamiliasD')
@validate_api_key
def get_familias():
    return jsonify(db_getFamilias())


@tablas_bp.route('/getVendedoresD')
@validate_api_key
def get_vendedores():
    return jsonify(db_getVendedores())

@tablas_bp.route('/getVisitasVendedor/<string:vdor>/<int:dia>')
@validate_api_key
def get_visitas_vendedor(vdor, dia):
    return jsonify(db_get_visitas_vendedor(vdor, dia))