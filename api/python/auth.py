from flask import request
from flask.json import jsonify
from functools import wraps

from config import API_KEY


def error_api_key():
    return jsonify({
        'error': True,
        'message': 'No se defininió una API KEY correcta.\nComuníquese con el encargado de sistemas.'
    })


def validate_api_key(f):
    @wraps(f)
    def wrapper(*args, **kwargs):
        api_key = request.headers.get('API_KEY')

        if api_key == '':
            return error_api_key()
        else:
            if api_key != API_KEY:
                return error_api_key()

        return f(*args, **kwargs)

    return wrapper
