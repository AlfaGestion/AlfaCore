from flask import Blueprint

from .cliente_id_bp import cliente_id_bp


agw_v1_bp = Blueprint('agw_v1_bp', __name__)
agw_v1_bp.register_blueprint(cliente_id_bp)
