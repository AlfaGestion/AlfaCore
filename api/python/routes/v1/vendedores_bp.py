from flask import Blueprint, jsonify
from model import db_get_visitas_vendedor

from auth import validate_api_key

vendedores_bp = Blueprint('vendedores_bp', __name__)


@vendedores_bp.route('/agetVisitasVendedor/<string:vdor>/<int:dia>')
@validate_api_key
def get_visitas_vendedor(vdor, dia):
    return jsonify(db_get_visitas_vendedor(vdor, dia))


def ssas():
    pass
