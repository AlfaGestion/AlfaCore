import os
from dotenv import load_dotenv

load_dotenv()

API_KEY = os.getenv('API_KEY')

# Configuración Email
CTA_MAIL =  os.getenv('EMAIL_CTA')

MAIL_SERVER = os.getenv('EMAIL_SERVER')
MAIL_PORT = os.getenv('EMAIL_PORT')
MAIL_USERNAME = CTA_MAIL
MAIL_PASSWORD = os.getenv('EMAIL_PASS')
MAIL_USE_TLS = False
MAIL_USE_SSL = False

DB_SERVER = os.getenv('DB_SERVER')
DB_NAME = os.getenv('DB_NAME')
DB_USER = os.getenv('DB_USER')
DB_PASS = os.getenv('DB_PASS')

DB_VERSION = os.getenv('DB_VERSION')
SA_USER = os.getenv('SA_USER')
SA_PASSWORD = os.getenv('SA_PASSWORD')

BASE_URL = os.getenv('BASE_URL')

# Es la ruta física de la api
API_ROOT = os.getenv('API_ROOT')

# Base ALFA
DB_SERVER_ALFA = os.getenv('DB_SERVER_ALFA')
DB_NAME_ALFA = os.getenv('DB_NAME_ALFA')
DB_USER_ALFA = os.getenv('DB_USER_ALFA')
DB_PASS_ALFA = os.getenv('DB_PASS_ALFA')

#Base DATOS COMPARTIDOS
DB_SERVER_SHARE = os.getenv('DB_SERVER_SHARE')
DB_NAME_SHARE = os.getenv('DB_NAME_SHARE')
DB_USER_SHARE = os.getenv('DB_USER_SHARE')
DB_PASS_SHARE = os.getenv('DB_PASS_SHARE')

#Base TRANSPORTE
DB_SERVER_TRANSPORT = os.getenv('DB_SERVER_TRANSPORT')
DB_NAME_TRANSPORT = os.getenv('DB_NAME_TRANSPORT')
DB_USER_TRANSPORT = os.getenv('DB_USER_TRANSPORT')
DB_PASS_TRANSPORT = os.getenv('DB_PASS_TRANSPORT')

ML_API_ID = os.getenv('ML_API_ID')
ML_API_SECRET_KEY = os.getenv('ML_API_SECRET_KEY')

class AppConfig:
    MAIL_SERVER = MAIL_SERVER
    MAIL_PORT = MAIL_PORT
    MAIL_USERNAME = MAIL_USERNAME
    MAIL_PASSWORD = MAIL_PASSWORD
    MAIL_USE_TLS = MAIL_USE_TLS
    MAIL_USE_SSL = MAIL_USE_SSL

    UPLOAD_FOLDER = os.getenv('UPLOAD_FOLDER')