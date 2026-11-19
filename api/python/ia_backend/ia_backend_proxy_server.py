#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Backend proxy mínimo para centralizar OPENAI_API_KEY fuera del cliente.

Variables requeridas:
- OPENAI_API_KEY
- IA_CLIENTS_JSON   (ej: {"cliente_oliva":"secreto_largo_1","cliente_demo":"secreto_largo_2"})

Opcionales:
- IA_BACKEND_HOST (default 0.0.0.0)
- IA_BACKEND_PORT (default 8787)
- IA_MAX_SKEW_SECONDS (default 300)
"""

from __future__ import annotations

import hashlib
import hmac
import json
import os
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any, Dict, Optional

from dotenv import load_dotenv
from openai import OpenAI


def _sql_conn_str() -> str:
    user = (os.getenv("SQL_USER") or "").strip()
    pwd = (os.getenv("SQL_PASSWORD") or "").strip()
    server = (os.getenv("SQL_SERVER") or "").strip()
    db = (os.getenv("SQL_DATABASE") or "").strip()
    driver = (os.getenv("SQL_DRIVER") or "").strip()
    if not all([user, pwd, server, db, driver]):
        return ""
    return (
        f"DRIVER={{{driver}}};"
        f"SERVER={server};"
        f"DATABASE={db};"
        f"UID={user};"
        f"PWD={pwd};"
        "TrustServerCertificate=yes;"
    )


def _save_audit(idcliente_raw: Any, opcion: str, archivo: str, ok: bool, err: str, duracion_ms: int) -> Optional[str]:
    if idcliente_raw is None or str(idcliente_raw).strip() == "":
        return None

    try:
        idcliente = int(str(idcliente_raw).strip())
    except Exception:
        return "idcliente invalido"

    conn_str = _sql_conn_str()
    if not conn_str:
        return "faltan variables SQL_*"

    try:
        import pyodbc  # type: ignore
    except Exception:
        return "falta pyodbc"

    # Evita perder auditoría si alguna columna en SQL es más corta de lo esperado.
    len_profiles = [
        (128, 255, 1024),
        (64, 128, 512),
        (32, 64, 256),
    ]
    variants_sql = [
        "INSERT INTO dbo.IA_ConsultasGPT (idcliente, opcion, archivo, ok, error, duracion_ms) VALUES (?, ?, ?, ?, ?, ?)",
        "INSERT INTO dbo.IA_ConsultasGPT (idcliente, opcion, archivo_nombre, ok, error, duracion_ms) VALUES (?, ?, ?, ?, ?, ?)",
        "INSERT INTO dbo.IA_ConsultasGPT (idcliente, opcion, archivo) VALUES (?, ?, ?)",
        "INSERT INTO dbo.IA_ConsultasGPT (idcliente, opcion, archivo_nombre) VALUES (?, ?, ?)",
    ]

    last_err = ""
    for max_op, max_arch, max_err in len_profiles:
        op = (str(opcion or "").strip() or "FACTURAS")[:max_op]
        ar = (str(archivo or "").strip())[:max_arch]
        er = (str(err or "").strip())[:max_err]
        variants = [
            (variants_sql[0], (idcliente, op, ar, 1 if ok else 0, er, int(duracion_ms))),
            (variants_sql[1], (idcliente, op, ar, 1 if ok else 0, er, int(duracion_ms))),
            (variants_sql[2], (idcliente, op, ar)),
            (variants_sql[3], (idcliente, op, ar)),
        ]
        for sql, params in variants:
            try:
                with pyodbc.connect(conn_str, timeout=5) as conn:
                    cur = conn.cursor()
                    cur.execute(sql, params)
                    conn.commit()
                    return None
            except Exception as e:
                last_err = str(e)

    return f"sql_insert_error: {last_err}"


def _append_audit_fallback(
    *,
    idcliente: Any,
    opcion: str,
    archivo: str,
    ok: bool,
    error: str,
    duracion_ms: int,
    sql_error: str,
) -> None:
    """Persistencia local cuando SQL no está disponible."""
    try:
        log_dir = Path(__file__).resolve().parent / "LOG"
        log_dir.mkdir(parents=True, exist_ok=True)
        row = {
            "ts": int(time.time()),
            "idcliente": idcliente,
            "opcion": opcion,
            "archivo_nombre": archivo,
            "ok": 1 if ok else 0,
            "error": error or "",
            "duracion_ms": int(duracion_ms),
            "sql_error": sql_error or "",
        }
        with (log_dir / "ia_audit_fallback.jsonl").open("a", encoding="utf-8") as f:
            f.write(json.dumps(row, ensure_ascii=False) + "\n")
    except Exception:
        # Nunca cortar la respuesta de API por error de fallback.
        return


NONCE_TTL_SECONDS = 600
_SEEN_NONCES: Dict[str, float] = {}


def _cleanup_nonces(now: float) -> None:
    expired = [k for k, ts in _SEEN_NONCES.items() if (now - ts) > NONCE_TTL_SECONDS]
    for k in expired:
        _SEEN_NONCES.pop(k, None)


def _build_signature(secret: str, timestamp: str, nonce: str, body: str) -> str:
    msg = f"{timestamp}.{nonce}.{body}".encode("utf-8")
    return hmac.new(secret.encode("utf-8"), msg, hashlib.sha256).hexdigest()


def _extract_output_text(resp: Any) -> str:
    out_text = (getattr(resp, "output_text", None) or "").strip()
    if out_text:
        return out_text
    parts = []
    for item in getattr(resp, "output", []) or []:
        for c in getattr(item, "content", []) or []:
            t = getattr(c, "text", None)
            if t:
                parts.append(t)
    return "\n".join(parts).strip()


class IAHandler(BaseHTTPRequestHandler):
    server_version = "IAProxy/1.0"

    def _json(self, status: int, payload: Dict[str, Any]) -> None:
        raw = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def do_POST(self) -> None:
        if self.path != "/v1/process":
            self._json(404, {"ok": False, "error": "not_found"})
            return

        length = int(self.headers.get("Content-Length", "0") or "0")
        body_bytes = self.rfile.read(length)
        body = body_bytes.decode("utf-8", errors="replace")

        client_id = (self.headers.get("X-IA-Client-Id") or "").strip()
        ts_raw = (self.headers.get("X-IA-Timestamp") or "").strip()
        nonce = (self.headers.get("X-IA-Nonce") or "").strip()
        sig = (self.headers.get("X-IA-Signature") or "").strip().lower()
        if not client_id or not ts_raw or not nonce or not sig:
            self._json(401, {"ok": False, "error": "missing_auth_headers"})
            return

        try:
            ts = int(ts_raw)
        except Exception:
            self._json(401, {"ok": False, "error": "bad_timestamp"})
            return

        max_skew = int(os.getenv("IA_MAX_SKEW_SECONDS", "300") or "300")
        now = int(time.time())
        if abs(now - ts) > max_skew:
            self._json(401, {"ok": False, "error": "timestamp_out_of_range"})
            return

        secret = CLIENTS.get(client_id)
        if not secret:
            self._json(403, {"ok": False, "error": "unknown_client"})
            return

        nonce_key = f"{client_id}:{nonce}"
        now_f = time.time()
        _cleanup_nonces(now_f)
        if nonce_key in _SEEN_NONCES:
            self._json(409, {"ok": False, "error": "replay_detected"})
            return

        expected = _build_signature(secret, ts_raw, nonce, body)
        if not hmac.compare_digest(expected, sig):
            self._json(403, {"ok": False, "error": "invalid_signature"})
            return
        _SEEN_NONCES[nonce_key] = now_f

        try:
            payload = json.loads(body)
        except Exception:
            self._json(400, {"ok": False, "error": "invalid_json_body"})
            return

        model = (payload.get("model") or "").strip()
        max_output_tokens = int(payload.get("max_output_tokens") or 4000)
        input_payload = payload.get("input")
        text_payload = payload.get("text")

        metadata = payload.get("metadata") if isinstance(payload.get("metadata"), dict) else {}
        idcliente = payload.get("idcliente")
        if idcliente is None:
            idcliente = metadata.get("idcliente")
        opcion = (str(payload.get("opcion") or payload.get("task") or metadata.get("opcion") or metadata.get("task") or "FACTURAS").strip() or "FACTURAS").upper()
        archivo = str(
            payload.get("archivo_nombre")
            or payload.get("source_filename")
            or payload.get("filename")
            or payload.get("archivoNombre")
            or payload.get("file_name")
            or payload.get("archivo")
            or metadata.get("archivo_nombre")
            or metadata.get("source_filename")
            or metadata.get("filename")
            or metadata.get("archivoNombre")
            or metadata.get("file_name")
            or metadata.get("archivo")
            or self.headers.get("X-IA-Archivo-Nombre")
            or self.headers.get("X-IA-Source-Filename")
            or self.headers.get("X-IA-Filename")
            or self.headers.get("X-IA-File-Name")
            or ""
        ).strip()

        if not model:
            self._json(400, {"ok": False, "error": "model_required"})
            return
        if not isinstance(input_payload, list):
            self._json(400, {"ok": False, "error": "input_required"})
            return

        t0 = time.time()
        try:
            req: Dict[str, Any] = {
                "model": model,
                "max_output_tokens": max_output_tokens,
                "input": input_payload,
            }
            if text_payload is not None:
                req["text"] = text_payload
            resp = OPENAI_CLIENT.responses.create(**req)
            output_text = _extract_output_text(resp)
        except Exception as e:
            dur_ms = max(0, int((time.time() - t0) * 1000))
            sql_err = _save_audit(idcliente, opcion, archivo, False, str(e), dur_ms)
            if sql_err:
                print(f"[AUDIT] {sql_err}")
            _append_audit_fallback(
                idcliente=idcliente,
                opcion=opcion,
                archivo=archivo,
                ok=False,
                error=str(e),
                duracion_ms=dur_ms,
                sql_error=sql_err or "",
            )
            self._json(500, {"ok": False, "error": f"openai_error: {e}"})
            return

        if not output_text:
            dur_ms = max(0, int((time.time() - t0) * 1000))
            sql_err = _save_audit(idcliente, opcion, archivo, False, "empty_model_response", dur_ms)
            if sql_err:
                print(f"[AUDIT] {sql_err}")
            _append_audit_fallback(
                idcliente=idcliente,
                opcion=opcion,
                archivo=archivo,
                ok=False,
                error="empty_model_response",
                duracion_ms=dur_ms,
                sql_error=sql_err or "",
            )
            self._json(502, {"ok": False, "error": "empty_model_response"})
            return

        dur_ms = max(0, int((time.time() - t0) * 1000))
        sql_err = _save_audit(idcliente, opcion, archivo, True, "", dur_ms)
        if sql_err:
            print(f"[AUDIT] {sql_err}")
        _append_audit_fallback(
            idcliente=idcliente,
            opcion=opcion,
            archivo=archivo,
            ok=True,
            error="",
            duracion_ms=dur_ms,
            sql_error=sql_err or "",
        )

        self._json(
            200,
            {
                "ok": True,
                "model": model,
                "output_text": output_text,
            },
        )

    def log_message(self, format: str, *args: Any) -> None:
        # Log mínimo en stdout para operación en servidor Windows/Linux.
        msg = format % args
        print(f"[{time.strftime('%Y-%m-%d %H:%M:%S')}] {self.address_string()} {msg}")


def _load_clients() -> Dict[str, str]:
    raw = (os.getenv("IA_CLIENTS_JSON") or "").strip()
    if not raw:
        raise SystemExit("ERROR: Falta IA_CLIENTS_JSON.")
    try:
        data = json.loads(raw)
    except Exception as e:
        raise SystemExit(f"ERROR: IA_CLIENTS_JSON inválido: {e}") from e
    if not isinstance(data, dict) or not data:
        raise SystemExit("ERROR: IA_CLIENTS_JSON debe ser un objeto con al menos un cliente.")

    out: Dict[str, str] = {}
    for k, v in data.items():
        key = str(k).strip()
        val = str(v).strip()
        if key and val:
            out[key] = val
    if not out:
        raise SystemExit("ERROR: IA_CLIENTS_JSON sin clientes válidos.")
    return out


if __name__ == "__main__":
    load_dotenv(override=False)
    api_key = (os.getenv("OPENAI_API_KEY") or "").strip()
    if not api_key:
        raise SystemExit("ERROR: Falta OPENAI_API_KEY en el backend.")

    CLIENTS = _load_clients()
    OPENAI_CLIENT = OpenAI(api_key=api_key)

    host = (os.getenv("IA_BACKEND_HOST") or "0.0.0.0").strip()
    port = int(os.getenv("IA_BACKEND_PORT") or "8787")
    httpd = ThreadingHTTPServer((host, port), IAHandler)
    print(f"IA backend escuchando en http://{host}:{port}/v1/process")
    httpd.serve_forever()

