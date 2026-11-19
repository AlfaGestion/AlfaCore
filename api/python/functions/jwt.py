from jwt import encode, decode
from jwt import exceptions
# from os import getenv
from datetime import datetime, timedelta
# from flask import jsonify
from functions.responses import set_response

SECRET_KEY = 'asdw2#@$a334asd4'


def expire_date(days: int):
    now = datetime.now()
    new_date = now + timedelta(days)
    return new_date


def write_token(data: dict):
    token = encode(payload={**data, "exp": expire_date(2)},
                   key=SECRET_KEY, algorithm="HS256")
    return token.encode("UTF-8")


def validate_token(token, output=False):
    try:
        if output:
            return True, decode(token, key=SECRET_KEY, algorithms=["HS256"])
        decode(token, key=SECRET_KEY, algorithms=["HS256"])
    except exceptions.DecodeError:

        # response = jsonify({"message": "Invalid Token"})
        # response.status_code = 401
        return False, set_response("Invalid Token", 401)
    except exceptions.ExpiredSignatureError:
        # response = jsonify({"message": "Token Expired"})
        # response.status_code = 401
        return False, set_response("Token Expired", 401)
    return True, ''
