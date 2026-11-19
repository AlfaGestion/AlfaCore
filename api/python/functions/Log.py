import hashlib
import json
import os
from datetime import datetime
from pathlib import Path

from flask import has_request_context, request


_LOG_ROOT = Path(__file__).resolve().parent.parent / "logs"


class Log:
    @staticmethod
    def _sanitize_file_part(value):
        text = "" if value is None else str(value).strip()
        if not text:
            return ""

        for char in ['\\', '/', ':', '*', '?', '"', '<', '>', '|', ' ']:
            text = text.replace(char, "_")
        return text

    @staticmethod
    def _decode_token_account(token: str = "") -> str:
        token = (token or "").strip()
        if not token:
            return ""

        try:
            from functions.jwt import validate_token

            valid, payload = validate_token(token, output=True)
            if valid and isinstance(payload, dict):
                account = payload.get("account") or payload.get("alfaCustomerId") or payload.get("idcliente")
                return Log._sanitize_file_part(account)
        except Exception:
            return ""

        return ""

    @staticmethod
    def _extract_request_account() -> str:
        if not has_request_context():
            return ""

        authorization = request.headers.get("Authorization", "")
        if authorization.lower().startswith("bearer "):
            account = Log._decode_token_account(authorization.split(" ", 1)[1])
            if account:
                return account

        candidates = []
        view_args = request.view_args or {}
        candidates.extend(
            [
                view_args.get("account"),
                request.args.get("idcliente"),
                request.args.get("cliente_id"),
                request.args.get("account"),
            ]
        )

        try:
            json_data = request.get_json(silent=True)
        except Exception:
            json_data = None

        if isinstance(json_data, dict):
            candidates.extend(
                [
                    json_data.get("alfaCustomerId"),
                    json_data.get("idcliente"),
                    json_data.get("cliente_id"),
                    json_data.get("account"),
                ]
            )
        elif isinstance(json_data, list) and json_data and isinstance(json_data[0], dict):
            candidates.extend(
                [
                    json_data[0].get("alfaCustomerId"),
                    json_data[0].get("idcliente"),
                    json_data[0].get("cliente_id"),
                    json_data[0].get("account"),
                ]
            )

        for candidate in candidates:
            account = Log._sanitize_file_part(candidate)
            if account:
                return account

        return ""

    @staticmethod
    def _resolve_code_account(code_account="", token: str = "") -> str:
        account = Log._sanitize_file_part(code_account)
        if account:
            return account

        account = Log._decode_token_account(token)
        if account:
            return account

        return Log._extract_request_account()

    @staticmethod
    def _resolve_prefix(prefix: str | None = None) -> str:
        if prefix:
            return Log._sanitize_file_part(prefix) or "LOG"

        return "LOG"

    @staticmethod
    def _resolve_log_path(prefix: str, code_account="", token: str = ""):
        date = datetime.now().strftime("%d-%m-%Y")
        resolved_prefix = Log._resolve_prefix(prefix)
        resolved_account = Log._resolve_code_account(code_account, token)

        _LOG_ROOT.mkdir(parents=True, exist_ok=True)
        return str(_LOG_ROOT / f"{resolved_prefix}_{resolved_account}_{date}.log")

    @staticmethod
    def _resolve_v3_order_log_path(code_account="", token: str = ""):
        date = datetime.now().strftime("%d-%m-%Y")
        resolved_account = Log._resolve_code_account(code_account, token) or "GENERAL"

        log_dir = _LOG_ROOT / "V3"
        log_dir.mkdir(parents=True, exist_ok=True)
        return str(log_dir / f"LOG_{resolved_account}_{date}.log")

    @staticmethod
    def _resolve_state_path():
        _LOG_ROOT.mkdir(parents=True, exist_ok=True)
        return str(_LOG_ROOT / "_log_state.json")

    @staticmethod
    def _resolve_login_web_path():
        date = datetime.now().strftime("%d-%m-%Y")
        log_dir = _LOG_ROOT / "login_web"
        log_dir.mkdir(parents=True, exist_ok=True)
        return str(log_dir / f"login_web_{date}.log")

    @staticmethod
    def _write(prefix: str, data, code_account="", type="WARNING", token: str = ""):
        time = datetime.now().strftime("%d-%m-%Y %H:%M:%S")
        try:
            file_path = Log._resolve_log_path(prefix, code_account, token)
            with open(file_path, "a", encoding="utf-8") as file:
                file.write(f"\n{type}: {time}\n{data}")
        except Exception:
            pass

    @staticmethod
    def create(data, code_account="", type="WARNING", token: str = "", prefix: str | None = None):
        Log._write(prefix or "", data, code_account, type, token)

    @staticmethod
    def create_v3_order(data, code_account="", type="WARNING", token: str = ""):
        time = datetime.now().strftime("%d-%m-%Y %H:%M:%S")
        try:
            file_path = Log._resolve_v3_order_log_path(code_account, token)
            with open(file_path, "a", encoding="utf-8") as file:
                file.write(f"\n{type}: {time}\n{data}")
        except Exception:
            pass

    @staticmethod
    def create_once(
        data,
        code_account="",
        type="WARNING",
        token: str = "",
        prefix: str | None = None,
        dedupe_key: str = "",
    ):
        raw_key = dedupe_key or str(data)
        hash_key = hashlib.sha1(raw_key.encode("utf-8", errors="ignore")).hexdigest()

        try:
            file_path = Log._resolve_log_path(prefix or "", code_account, token)
            state_path = Log._resolve_state_path()

            state = {}
            if os.path.exists(state_path):
                try:
                    with open(state_path, "r", encoding="utf-8") as state_file:
                        state = json.load(state_file) or {}
                except Exception:
                    state = {}

            file_seen = state.get(file_path, [])
            if hash_key in file_seen:
                return

            file_seen.append(hash_key)
            state[file_path] = file_seen

            with open(state_path, "w", encoding="utf-8") as state_file:
                json.dump(state, state_file, ensure_ascii=False, indent=2)

            Log._write(prefix or "", data, code_account, type, token)
        except Exception:
            pass

    @staticmethod
    def createIngreso(data, code_account="", type="WARNING"):
        time = datetime.now().strftime("%d-%m-%Y %H:%M:%S")
        date = datetime.now().strftime("%d-%m-%Y")
        resolved_account = Log._sanitize_file_part(code_account)
        try:
            os.makedirs("logs", exist_ok=True)
            with open(f"logs/LOG_Ingreso{resolved_account}_{date}.log", "a", encoding="utf-8") as file:
                file.write(f"\n{type}: {time}\n{data}")
        except Exception:
            pass

    @staticmethod
    def create_login_web(data, type="INFO"):
        time = datetime.now().strftime("%d-%m-%Y %H:%M:%S")
        try:
            file_path = Log._resolve_login_web_path()
            with open(file_path, "a", encoding="utf-8") as file:
                file.write(f"\n{type}: {time}\n{data}")
        except Exception:
            pass
