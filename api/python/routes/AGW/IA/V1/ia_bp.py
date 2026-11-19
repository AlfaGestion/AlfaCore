import json
import os
import time
from typing import Any, Callable, Dict, Optional

from flask import Blueprint, jsonify, request


ia_v1_bp = Blueprint('agw_ia_v1_bp', __name__)

_validate_ia_request: Optional[Callable[[str], Any]] = None
_get_ia_api_key: Optional[Callable[[Any, Any], str]] = None
_get_ia_openai_client: Optional[Callable[[str], Any]] = None
_extract_output_text: Optional[Callable[[Any], str]] = None
_save_audit: Optional[Callable[..., Optional[str]]] = None
_append_audit_fallback: Optional[Callable[..., None]] = None


def configure_ia_services(
    *,
    validate_ia_request: Callable[[str], Any],
    get_ia_api_key: Callable[[Any, Any], str],
    get_ia_openai_client: Callable[[str], Any],
    extract_output_text: Callable[[Any], str],
    save_audit: Callable[..., Optional[str]],
    append_audit_fallback: Callable[..., None],
) -> None:
    global _validate_ia_request
    global _get_ia_api_key
    global _get_ia_openai_client
    global _extract_output_text
    global _save_audit
    global _append_audit_fallback

    _validate_ia_request = validate_ia_request
    _get_ia_api_key = get_ia_api_key
    _get_ia_openai_client = get_ia_openai_client
    _extract_output_text = extract_output_text
    _save_audit = save_audit
    _append_audit_fallback = append_audit_fallback


def _services_ready() -> bool:
    return all([
        _validate_ia_request,
        _get_ia_api_key,
        _get_ia_openai_client,
        _extract_output_text,
        _save_audit,
        _append_audit_fallback,
    ])


def _customer_identity(payload: Dict[str, Any]) -> tuple[Any, Any]:
    metadata = payload.get('metadata') if isinstance(payload.get('metadata'), dict) else {}
    idcliente = (
        payload.get('id_cliente')
        if payload.get('id_cliente') is not None
        else payload.get('idcliente')
    )
    if idcliente is None:
        idcliente = metadata.get('idcliente')
    licencia_principal = (
        payload.get('licencia')
        or payload.get('licencia_principal')
        or payload.get('LicenciaPrincipal')
        or payload.get('licenciaprincipal')
        or metadata.get('licencia_principal')
        or metadata.get('LicenciaPrincipal')
        or metadata.get('licenciaprincipal')
    )
    return idcliente, licencia_principal


def _response(ok: bool, texto: str = '', error: str = ''):
    return jsonify({'ok': ok, 'texto': texto, 'error': error})


def _dll_opcion(metodo: str) -> str:
    opciones = {
        'consulta': 'DLL_Consulta',
        'mejorar_instruccion': 'DLL_MejorarPromt',
        'generar_python': 'DLL_GenerarPython',
    }
    return opciones[metodo]


def _save_dll_audit(idcliente: Any, metodo: str, ok: bool, error: str, duracion_ms: int) -> None:
    opcion = _dll_opcion(metodo)
    sql_error = _save_audit(idcliente, opcion, None, ok, error, duracion_ms)
    if sql_error:
        print(f'[AUDIT] {sql_error}')
        _append_audit_fallback(
            idcliente=idcliente,
            opcion=opcion,
            archivo='',
            ok=ok,
            error=error,
            duracion_ms=duracion_ms,
            sql_error=sql_error,
        )


def _authenticated_payload():
    if not _services_ready():
        return None, (jsonify({'ok': False, 'error': 'ia_services_not_configured'}), 500)

    raw_body = (request.get_data() or b'').decode('utf-8', errors='replace')
    auth_error = _validate_ia_request(raw_body)
    if auth_error:
        return None, auth_error

    try:
        return json.loads(raw_body), None
    except Exception:
        return None, (jsonify({'ok': False, 'error': 'invalid_json_body'}), 400)


@ia_v1_bp.route('/ConsultarGPTXCliente', methods=['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'OPTIONS'])
def consultar_gpt_por_cliente():
    if request.method == 'OPTIONS':
        return ('', 204)
    if request.method != 'POST':
        return _response(False, error='Método HTTP no permitido.'), 405

    if not _services_ready():
        return _response(False, error='Servicio de IA no configurado.'), 500

    try:
        payload = json.loads((request.get_data() or b'').decode('utf-8', errors='replace'))
    except Exception:
        return _response(False, error='JSON inválido.'), 400
    if not isinstance(payload, dict):
        return _response(False, error='JSON inválido.'), 400

    idcliente, licencia_principal = _customer_identity(payload)
    prompt = str(payload.get('prompt') or '').strip()
    metodo = str(payload.get('metodo') or 'consulta').strip().lower()
    tipo_tarea = str(payload.get('tipo_tarea') or '').strip().lower()
    metodos_validos = {'consulta', 'mejorar_instruccion', 'generar_python'}
    tipos_tarea_validos = {'crear_python', 'consulta', 'base_datos', 'general'}

    if not str(idcliente or '').strip() or not str(licencia_principal or '').strip():
        return _response(False, error='Cliente o licencia no configurados.'), 401
    if not prompt:
        return _response(False, error='El prompt está vacío.'), 400
    if metodo not in metodos_validos:
        return _response(False, error='Método no válido.'), 400
    if tipo_tarea and tipo_tarea not in tipos_tarea_validos:
        return _response(False, error='Tipo de tarea no válido.'), 400

    try:
        api_key = _get_ia_api_key(idcliente, licencia_principal)
    except ValueError:
        return _response(False, error='Licencia inválida.'), 403
    except Exception:
        return _response(False, error='No se pudo validar la licencia.'), 500

    instrucciones = {
        'consulta': 'Respondé la consulta de forma clara y útil.',
        'mejorar_instruccion': 'Mejorá la instrucción recibida. Devolvé únicamente la instrucción mejorada.',
        'generar_python': 'Generá la solución solicitada en Python. Devolvé únicamente el código Python.',
    }
    contexto_tarea = f' Tipo de tarea: {tipo_tarea}.' if tipo_tarea else ''
    input_text = f'{instrucciones[metodo]}{contexto_tarea}\n\n{prompt}'
    model = (os.getenv('OPENAI_MODEL') or os.getenv('IA_DEFAULT_MODEL') or 'gpt-4.1-mini').strip()
    t0 = time.time()
    try:
        response = _get_ia_openai_client(api_key).responses.create(
            model=model,
            input=input_text,
            store=False,
        )
        texto = _extract_output_text(response)
    except Exception:
        _save_dll_audit(
            idcliente,
            metodo,
            False,
            'No fue posible comunicarse con OpenAI.',
            max(0, int((time.time() - t0) * 1000)),
        )
        return _response(False, error='No fue posible comunicarse con OpenAI.'), 502

    if not texto:
        _save_dll_audit(
            idcliente,
            metodo,
            False,
            'OpenAI no devolvió texto.',
            max(0, int((time.time() - t0) * 1000)),
        )
        return _response(False, error='OpenAI no devolvió texto.'), 502

    _save_dll_audit(
        idcliente,
        metodo,
        True,
        '',
        max(0, int((time.time() - t0) * 1000)),
    )
    return _response(True, texto=texto), 200


@ia_v1_bp.route('/process', methods=['POST', 'OPTIONS'])
def ia_process_proxy():
    if request.method == 'OPTIONS':
        return ('', 204)

    payload, error_response = _authenticated_payload()
    if error_response:
        return error_response

    model = str(payload.get('model') or '').strip()
    max_output_tokens = int(payload.get('max_output_tokens') or 4000)
    input_payload = payload.get('input')
    text_payload = payload.get('text')
    idcliente, licencia_principal = _customer_identity(payload)
    metadata = payload.get('metadata') if isinstance(payload.get('metadata'), dict) else {}
    opcion = (str(payload.get('opcion') or payload.get('task') or metadata.get('opcion') or metadata.get('task') or 'FACTURAS').strip() or 'FACTURAS').upper()
    archivo = str(payload.get('archivo_nombre') or payload.get('source_filename') or payload.get('filename') or payload.get('archivoNombre') or payload.get('file_name') or payload.get('archivo') or metadata.get('archivo_nombre') or metadata.get('source_filename') or metadata.get('filename') or metadata.get('archivoNombre') or metadata.get('file_name') or metadata.get('archivo') or request.headers.get('X-IA-Archivo-Nombre') or request.headers.get('X-IA-Source-Filename') or request.headers.get('X-IA-Filename') or request.headers.get('X-IA-File-Name') or '').strip()

    if not model:
        return jsonify({'ok': False, 'error': 'model_required'}), 400
    if not isinstance(input_payload, list):
        return jsonify({'ok': False, 'error': 'input_required'}), 400

    try:
        api_key = _get_ia_api_key(idcliente, licencia_principal)
    except ValueError as e:
        return jsonify({'ok': False, 'error': str(e)}), 400
    except Exception as e:
        return jsonify({'ok': False, 'error': str(e)}), 500

    t0 = time.time()
    try:
        request_data: Dict[str, Any] = {
            'model': model,
            'max_output_tokens': max_output_tokens,
            'input': input_payload,
        }
        if text_payload is not None:
            request_data['text'] = text_payload

        response = _get_ia_openai_client(api_key).responses.create(**request_data)
        output_text = _extract_output_text(response)
    except Exception as e:
        dur_ms = max(0, int((time.time() - t0) * 1000))
        sql_err = _save_audit(idcliente, opcion, archivo, False, str(e), dur_ms)
        if sql_err:
            print(f'[AUDIT] {sql_err}')
        _append_audit_fallback(idcliente=idcliente, opcion=opcion, archivo=archivo, ok=False, error=str(e), duracion_ms=dur_ms, sql_error=sql_err or '')
        return jsonify({'ok': False, 'error': f'openai_error: {e}'}), 500

    if not output_text:
        dur_ms = max(0, int((time.time() - t0) * 1000))
        sql_err = _save_audit(idcliente, opcion, archivo, False, 'empty_model_response', dur_ms)
        if sql_err:
            print(f'[AUDIT] {sql_err}')
        _append_audit_fallback(idcliente=idcliente, opcion=opcion, archivo=archivo, ok=False, error='empty_model_response', duracion_ms=dur_ms, sql_error=sql_err or '')
        return jsonify({'ok': False, 'error': 'empty_model_response'}), 502

    dur_ms = max(0, int((time.time() - t0) * 1000))
    sql_err = _save_audit(idcliente, opcion, archivo, True, '', dur_ms)
    if sql_err:
        print(f'[AUDIT] {sql_err}')
    _append_audit_fallback(idcliente=idcliente, opcion=opcion, archivo=archivo, ok=True, error='', duracion_ms=dur_ms, sql_error=sql_err or '')

    return jsonify({'ok': True, 'model': model, 'output_text': output_text}), 200
