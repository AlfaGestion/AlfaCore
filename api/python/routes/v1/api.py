from flask import Blueprint

# V1
from .v1.articulos_bp import articulos_bp
from .clientes_bp import clientes_bp
from .tablas_bp import tablas_bp
from .comprobantes_bp import comprobantes_bp
from .vendedores_bp import vendedores_bp

# Endpoints V1
# Utilizado para app movil
api_bp = Blueprint('api_bp', __name__,
                   template_folder='templates',
                   static_folder='static', static_url_path='assets')

api_bp.register_blueprint(articulos_bp)
api_bp.register_blueprint(clientes_bp)
api_bp.register_blueprint(tablas_bp)
api_bp.register_blueprint(comprobantes_bp)
api_bp.register_blueprint(vendedores_bp)
