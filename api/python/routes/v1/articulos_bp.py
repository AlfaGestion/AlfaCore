from flask import Blueprint, jsonify
from model import db_getArticulos, db_getStock, db_printPriceList

from auth import validate_api_key

articulos_bp = Blueprint('articulos_bp', __name__)

@articulos_bp.route('/getArticulos') #Todos los articulos
@validate_api_key
def get_articulos_all():
    return jsonify(db_getArticulos(-1))


@articulos_bp.route('/getArticulosD/<int:page>') #Todos los articulos de una pagina
#@validate_api_key
def get_articulos_d(page):
    return jsonify(db_getArticulos(page))



@articulos_bp.route('/getStockD/<string:id>')
@validate_api_key
def get_stock(id):
    return jsonify(db_getStock(id))


@articulos_bp.route('/printPriceList') 
@validate_api_key
def print_price_list():
    # return 'ok'
    return jsonify(db_printPriceList())