from flask import jsonify


def set_response_old(message: str, status_code: int = 200):

    error = False

    if status_code == 200:
        error = False
    else:
        error = True

    response = jsonify(
        {"error": error, "message": message})

    response.status_code = status_code
    return response


def set_response(data: None, status_code: int = 200, message: str = ''):
    response = {
        'error': status_code != 200,
        'status_code': status_code,
        'message': message,
        'data': data
    }

    return response
