from datetime import datetime
import json
from pathlib import Path

import pyodbc
from flask import request
from flask_classful import route

from config import DB_NAME, DB_PASS, DB_SERVER, DB_USER, DB_VERSION
from functions.responses import set_response
from routes.v2.master import MasterView

CONTROL_DB_SERVER = '10.8.0.31'
CONTROL_DB_USER = 'ESTADISTICAS'
CONTROL_DB_PASS = 'ESTADISTICAS'
CONTROL_DB_VERSION = '11.0'
CONTROL_DB_NAME = 'ESTADISTICAS'


class AGWControlView(MasterView):

    def before_request(self, name, *args, **kwargs):
        self.code_account = ''
        self.token_global = ''
        return None

    @route('/estadisticas', methods=['POST'])
    @route('/statistics', methods=['POST'])
    @route('/guardar', methods=['POST'])
    @route('/GrabarControlSincro', methods=['POST'])
    def guardar_estadisticas(self):
        raw_body = request.get_data(cache=True, as_text=True) or ''
        payload = request.get_json(silent=True)
        if payload is None and raw_body:
            try:
                payload = json.loads(raw_body)
            except Exception:
                payload = {}
        payload = payload or {}
        record = payload.get('data') if isinstance(payload.get('data'), dict) else payload
        raw_idcliente = None
        if isinstance(record, dict):
            raw_idcliente = (
                record.get('IdCliente')
                or record.get('idCliente')
                or record.get('idcliente')
                or record.get('cliente')
            )

        self._write_sync_log(
            raw_idcliente or 'sin_idcliente',
            'INICIO RAW GrabarControlSincro',
            (
                f'method={request.method}',
                f'content_type={request.content_type}',
                f'query={request.query_string.decode("utf-8", errors="ignore")}',
                f'payload={repr(payload)}',
                f'raw_body={raw_body[:1000]}',
            ),
            'Ingreso al endpoint GrabarControlSincro.'
        )

        if not isinstance(record, dict) or not record:
            self._write_sync_log(
                raw_idcliente or 'sin_idcliente',
                'ERROR RAW GrabarControlSincro',
                (
                    f'method={request.method}',
                    f'content_type={request.content_type}',
                    f'payload={repr(payload)}',
                    f'raw_body={raw_body[:1000]}',
                ),
                'Debe enviar un JSON con los datos a grabar en CONTROL.'
            )
            return set_response([], 400, 'Debe enviar un JSON con los datos a grabar en CONTROL.')

        fecha = self._parse_datetime(
            record.get('Fecha') or record.get('fecha')
        ) or datetime.now()
        fh_hs_fin_proceso = self._parse_datetime(
            record.get('FhHsFinProceso')
            or record.get('fhHsFinProceso')
            or record.get('fh_hs_fin_proceso')
            or record.get('fhhsfinproceso')
        )

        data = {
            'IdCliente': self._safe_text(
                record.get('IdCliente')
                or record.get('idCliente')
                or record.get('idcliente')
                or record.get('cliente')
                or self.code_account,
                15
            ),
            'Fecha': fecha,
            'Secuencia': self._safe_int(record.get('Secuencia') or record.get('secuencia')),
            'NombrePC': self._safe_text(
                record.get('NombrePC')
                or record.get('nombrePC')
                or record.get('nombre_pc')
                or record.get('nombrepc'),
                150
            ),
            'ServidorSQL': self._safe_text(
                record.get('ServidorSQL')
                or record.get('servidorSQL')
                or record.get('servidor_sql')
                or record.get('servidorsql'),
                250
            ),
            'Usuario': self._safe_text(
                record.get('Usuario')
                or record.get('usuario')
                or record.get('user'),
                150
            ),
            'BaseDatos': self._safe_text(
                record.get('BaseDatos')
                or record.get('baseDatos')
                or record.get('base_datos')
                or record.get('basedatos'),
                150
            ),
            'NroError': self._safe_int(
                record.get('NroError')
                or record.get('nroError')
                or record.get('nro_error')
                or record.get('nroerror')
            ),
            'MensajeError': self._safe_text(
                record.get('MensajeError')
                or record.get('mensajeError')
                or record.get('mensaje_error')
                or record.get('mensajeerror'),
                255
            ),
            'Proceso': self._safe_text(
                record.get('Proceso')
                or record.get('proceso'),
                250
            ),
            'FhHsFinProceso': fh_hs_fin_proceso,
            'Archivo': self._safe_text(
                record.get('Archivo')
                or record.get('archivo'),
                200
            ),
        }

        if not data['IdCliente']:
            self._write_sync_log(
                raw_idcliente or 'sin_idcliente',
                'ERROR VALIDACION GrabarControlSincro',
                (f'payload={repr(payload)}',),
                'El campo IdCliente es obligatorio.'
            )
            return set_response([], 400, 'El campo IdCliente es obligatorio.')

        if not data['Proceso']:
            self._write_sync_log(
                data['IdCliente'] or raw_idcliente or 'sin_idcliente',
                'ERROR VALIDACION GrabarControlSincro',
                (f'payload={repr(payload)}',),
                'El campo Proceso es obligatorio para grabar en CONTROL.'
            )
            return set_response([], 400, 'El campo Proceso es obligatorio para grabar en CONTROL.')

        self._write_sync_log(
            data['IdCliente'],
            'INICIO PROCESO CONTROL',
            (
                f"Fecha={data['Fecha'].strftime('%Y-%m-%d %H:%M:%S') if data['Fecha'] else None}",
                f"Proceso={data['Proceso']}",
                f"Archivo={data['Archivo']}",
            ),
            'Se recibio llamado a guardar_estadisticas.'
        )

        connection = self._get_estadisticas_connection()
        if connection == '':
            self._write_sync_log(
                data['IdCliente'],
                'ERROR CONEXION CONTROL',
                (),
                'No se pudo obtener la conexion con la base ESTADISTICAS.'
            )
            return set_response([], 500, 'No se pudo obtener la conexion con la base ESTADISTICAS.')

        sql = """
SET NOCOUNT ON;
INSERT INTO dbo.CONTROL
    (IdCliente, Fecha, Secuencia, NombrePC, ServidorSQL, Usuario, BaseDatos, NroError, MensajeError, Proceso, FhHsFinProceso, Archivo)
VALUES
    (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
SELECT CAST(SCOPE_IDENTITY() AS int) AS Id;
""".strip()

        try:
            cursor = connection.cursor()
            params = (
                data['IdCliente'],
                data['Fecha'],
                data['Secuencia'],
                data['NombrePC'],
                data['ServidorSQL'],
                data['Usuario'],
                data['BaseDatos'],
                data['NroError'],
                data['MensajeError'],
                data['Proceso'],
                data['FhHsFinProceso'],
                data['Archivo'],
            )
            cursor.execute(sql, params)
            while cursor.description is None:
                if not cursor.nextset():
                    break
            inserted_row = cursor.fetchone() if cursor.description is not None else None
            inserted_id = inserted_row[0] if inserted_row else None
            connection.commit()
            self._write_sync_log(
                data['IdCliente'],
                'INSERT CONTROL OK',
                params,
                f'Insert realizado correctamente. Id={inserted_id}'
            )
        except Exception as e:
            try:
                connection.rollback()
            except Exception:
                pass
            self._write_sync_log(data['IdCliente'], sql, params, str(e))
            return set_response(
                [],
                500,
                f'No se pudo grabar el registro en CONTROL. {e}'
            )
        finally:
            try:
                connection.close()
            except Exception:
                pass

        return set_response(
            {
                'table': 'CONTROL',
                'inserted': True,
                'id': inserted_id,
                'record': {
                    'IdCliente': data['IdCliente'],
                    'Fecha': data['Fecha'].strftime('%Y-%m-%d %H:%M:%S') if data['Fecha'] else None,
                    'Secuencia': data['Secuencia'],
                    'NombrePC': data['NombrePC'],
                    'ServidorSQL': data['ServidorSQL'],
                    'Usuario': data['Usuario'],
                    'BaseDatos': data['BaseDatos'],
                    'NroError': data['NroError'],
                    'MensajeError': data['MensajeError'],
                    'Proceso': data['Proceso'],
                    'FhHsFinProceso': data['FhHsFinProceso'].strftime('%Y-%m-%d %H:%M:%S') if data['FhHsFinProceso'] else None,
                    'Archivo': data['Archivo'],
                },
            },
            200,
            ''
        )

    @route('/GrabarControlSincroTest', methods=['GET', 'POST'])
    def grabar_control_sincro_test(self):
        payload = request.get_json(silent=True) or {}
        idcliente = (
            payload.get('IdCliente')
            or payload.get('idCliente')
            or payload.get('idcliente')
            or request.args.get('IdCliente')
            or request.args.get('idCliente')
            or request.args.get('idcliente')
            or 'test_sincro'
        )
        now = datetime.now().strftime('%Y-%m-%d %H:%M:%S')
        insert_requested = str(
            request.args.get('insert')
            or payload.get('insert')
            or ''
        ).strip().lower() in ('1', 'true', 'si', 'yes')

        log_file = f'{self._safe_log_name(idcliente)}_sincro.log'

        self._write_sync_log(
            idcliente,
            'TEST CONTROL SINCRO',
            (
                f'payload={repr(payload)}',
                f'query={request.query_string.decode("utf-8", errors="ignore")}',
                f'insert_requested={insert_requested}',
            ),
            f'Se ejecuto GrabarControlSincroTest a las {now}'
        )

        response_data = {
            'ok': True,
            'message': 'GrabarControlSincroTest funcionando',
            'idcliente': idcliente,
            'timestamp': now,
            'log_file': log_file,
            'insert_requested': insert_requested,
            'db_connection': False,
            'inserted': False,
            'insert_id': None,
            'insert_error': None,
        }

        if insert_requested:
            connection = self._get_estadisticas_connection()
            if connection == '':
                response_data['ok'] = False
                response_data['message'] = 'GrabarControlSincroTest sin conexion a ESTADISTICAS'
                response_data['insert_error'] = 'No se pudo obtener la conexion con la base ESTADISTICAS.'
                self._write_sync_log(
                    idcliente,
                    'TEST CONTROL SINCRO - ERROR CONEXION',
                    (),
                    response_data['insert_error']
                )
                return set_response(response_data, 500, '')

            response_data['db_connection'] = True

            fecha_test = self._parse_datetime(
                payload.get('Fecha')
                or request.args.get('Fecha')
                or now
            ) or datetime.now()
            fh_hs_fin_test = self._parse_datetime(
                payload.get('FhHsFinProceso')
                or request.args.get('FhHsFinProceso')
                or now
            ) or fecha_test

            test_data = {
                'IdCliente': self._safe_text(idcliente, 15),
                'Fecha': fecha_test,
                'Secuencia': self._safe_int(
                    payload.get('Secuencia')
                    or request.args.get('Secuencia')
                    or 999
                ),
                'NombrePC': self._safe_text(
                    payload.get('NombrePC')
                    or request.args.get('NombrePC')
                    or 'TEST_BROWSER',
                    150
                ),
                'ServidorSQL': self._safe_text(
                    payload.get('ServidorSQL')
                    or request.args.get('ServidorSQL')
                    or 'TEST_BROWSER',
                    250
                ),
                'Usuario': self._safe_text(
                    payload.get('Usuario')
                    or request.args.get('Usuario')
                    or 'TEST_BROWSER',
                    150
                ),
                'BaseDatos': self._safe_text(
                    payload.get('BaseDatos')
                    or request.args.get('BaseDatos')
                    or 'TEST_BROWSER',
                    150
                ),
                'NroError': self._safe_int(
                    payload.get('NroError')
                    or request.args.get('NroError')
                    or 0
                ),
                'MensajeError': self._safe_text(
                    payload.get('MensajeError')
                    or request.args.get('MensajeError')
                    or 'Prueba manual desde GrabarControlSincroTest',
                    255
                ),
                'Proceso': self._safe_text(
                    payload.get('Proceso')
                    or request.args.get('Proceso')
                    or 'Prueba manual GrabarControlSincroTest',
                    250
                ),
                'FhHsFinProceso': fh_hs_fin_test,
                'Archivo': self._safe_text(
                    payload.get('Archivo')
                    or request.args.get('Archivo')
                    or 'TEST_CONTROL.SQL',
                    200
                ),
            }

            sql = """
SET NOCOUNT ON;
INSERT INTO dbo.CONTROL
    (IdCliente, Fecha, Secuencia, NombrePC, ServidorSQL, Usuario, BaseDatos, NroError, MensajeError, Proceso, FhHsFinProceso, Archivo)
VALUES
    (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
SELECT CAST(SCOPE_IDENTITY() AS int) AS Id;
""".strip()

            try:
                cursor = connection.cursor()
                params = (
                    test_data['IdCliente'],
                    test_data['Fecha'],
                    test_data['Secuencia'],
                    test_data['NombrePC'],
                    test_data['ServidorSQL'],
                    test_data['Usuario'],
                    test_data['BaseDatos'],
                    test_data['NroError'],
                    test_data['MensajeError'],
                    test_data['Proceso'],
                    test_data['FhHsFinProceso'],
                    test_data['Archivo'],
                )
                cursor.execute(sql, params)
                while cursor.description is None:
                    if not cursor.nextset():
                        break
                inserted_row = cursor.fetchone() if cursor.description is not None else None
                response_data['insert_id'] = inserted_row[0] if inserted_row else None
                connection.commit()
                response_data['inserted'] = True
                response_data['message'] = 'GrabarControlSincroTest funcionando e insertando'
                self._write_sync_log(
                    idcliente,
                    'TEST CONTROL SINCRO - INSERT OK',
                    params,
                    f"Insert de prueba realizado correctamente. Id={response_data['insert_id']}"
                )
            except Exception as e:
                try:
                    connection.rollback()
                except Exception:
                    pass
                response_data['ok'] = False
                response_data['message'] = 'GrabarControlSincroTest con error al insertar'
                response_data['insert_error'] = str(e)
                self._write_sync_log(
                    idcliente,
                    'TEST CONTROL SINCRO - INSERT ERROR',
                    locals().get('params', ()),
                    str(e)
                )
                return set_response(response_data, 500, '')
            finally:
                try:
                    connection.close()
                except Exception:
                    pass

        return set_response(
            response_data,
            200,
            ''
        )

    def _get_estadisticas_connection(self):
        try:
            return pyodbc.connect(
                'Driver={SQL Server Native Client ' + CONTROL_DB_VERSION + '};'
                + 'Server=' + CONTROL_DB_SERVER + ';'
                + 'Database=' + CONTROL_DB_NAME + ';'
                + 'uid=' + CONTROL_DB_USER + ';'
                + 'pwd=' + CONTROL_DB_PASS + ';'
                + 'MARS_Connection=Yes'
            )
        except Exception as e:
            self._write_sync_log(
                'conexion_control',
                'ERROR OPEN CONNECTION CONTROL',
                (
                    f'DB_SERVER={CONTROL_DB_SERVER}',
                    f'DB_NAME={CONTROL_DB_NAME}',
                    f'DB_USER={CONTROL_DB_USER}',
                    f'DB_VERSION={CONTROL_DB_VERSION}',
                ),
                str(e)
            )
            return ''

    def _safe_text(self, value, max_length: int):
        if value is None:
            return None
        text = str(value).strip()
        if not text:
            return None
        return text[:max_length]

    def _safe_int(self, value):
        if value is None or value == '':
            return None
        try:
            return int(value)
        except Exception:
            return None

    def _parse_datetime(self, value):
        if value is None:
            return None

        if isinstance(value, datetime):
            return value

        text = str(value).strip()
        if not text:
            return None

        formats = [
            '%Y-%m-%d %H:%M:%S',
            '%Y-%m-%d %H:%M',
            '%Y-%m-%d',
            '%d-%m-%Y %H:%M:%S',
            '%d-%m-%Y %H:%M',
            '%d-%m-%Y',
            '%d/%m/%Y %H:%M:%S',
            '%d/%m/%Y %H:%M',
            '%d/%m/%Y',
            '%Y-%m-%dT%H:%M:%S',
            '%Y-%m-%dT%H:%M',
        ]

        for fmt in formats:
            try:
                return datetime.strptime(text, fmt)
            except Exception:
                continue

        try:
            return datetime.fromisoformat(text)
        except Exception:
            return None

    def _write_sync_log(self, idcliente, sql, params, error_message):
        try:
            log_dir = Path(r'C:\inetpub\wwwroot\wsAlfa\LOG')
            log_dir.mkdir(parents=True, exist_ok=True)
            filename = f'{self._safe_log_name(idcliente)}_sincro.log'
            log_path = log_dir / filename
            lines = [
                f'[{datetime.now().strftime("%Y-%m-%d %H:%M:%S")}] CONTROL',
                f'SQL: {sql}',
                f'PARAMS: {repr(params)}',
                f'DETALLE: {error_message}',
                '-' * 80,
                '',
            ]
            with log_path.open('a', encoding='utf-8') as log_file:
                log_file.write('\n'.join(lines))
        except Exception:
            return

    def _safe_log_name(self, value):
        text = str(value or '').strip() or 'sin_idcliente'
        invalid_chars = '<>:"/\\|?*'
        for char in invalid_chars:
            text = text.replace(char, '_')
        return text
