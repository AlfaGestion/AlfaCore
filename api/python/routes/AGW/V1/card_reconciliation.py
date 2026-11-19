import calendar
import io
import random
import re
from datetime import datetime

from flask import request
from flask_classful import route

from configs.customer_connection import get_conn
from functions.responses import set_response
from routes.v2.master import MasterView

try:
    from pypdf import PdfReader
except Exception:
    PdfReader = None


class AGWCardReconciliationView(MasterView):

    @route('/analyze-pdfs', methods=['POST'])
    def analyze_pdfs(self):
        files = request.files.getlist('files')
        if not files:
            files = request.files.getlist('files[]')
        if not files:
            files = [value for key, value in request.files.items() if key.startswith('files[')]
        bank_hint = (request.form.get('bank_hint') or 'auto').strip().lower()

        if not files:
            return set_response([], 404, 'Debe adjuntar al menos un PDF.')

        parsed_files = []
        pdf_movements = []
        period_from_values = []
        period_to_values = []
        detected_brands = []
        detected_banks = []

        for item in files:
            filename = (item.filename or '').strip()
            extracted_text = self._extract_pdf_text(item)
            combined_text = f'{filename}\n{extracted_text}'

            brand = self._detect_brand(combined_text)
            bank = self._detect_bank(combined_text, bank_hint)
            period = self._detect_period(combined_text, filename)
            file_movements = self._extract_pdf_movements(extracted_text, filename, brand, bank, period)

            if brand != 'No detectada':
                detected_brands.append(brand)
            if bank != 'No detectado':
                detected_banks.append(bank)
            if period.get('from'):
                period_from_values.append(period['from'])
            if period.get('to'):
                period_to_values.append(period['to'])

            parsed_files.append({
                'name': filename,
                'size': item.content_length or 0,
                'detected_brand': brand,
                'detected_bank': bank,
                'period_from': period.get('from', ''),
                'period_to': period.get('to', ''),
                'period_label': period.get('label', ''),
                'movement_count': len(file_movements),
                'movements': file_movements,
                'debug_text_sample': extracted_text[:300],
            })
            pdf_movements.extend(file_movements)

        summary = {
            'pdf_count': len(parsed_files),
            'bank_hint': self._normalize_bank_hint(bank_hint),
            'detected_brands': self._unique(detected_brands),
            'detected_banks': self._unique(detected_banks),
            'date_from': min(period_from_values) if period_from_values else '',
            'date_to': max(period_to_values) if period_to_values else '',
            'period_labels': self._unique([row['period_label'] for row in parsed_files if row.get('period_label')]),
        }

        result = {
            'files': parsed_files,
            'summary': summary,
            'pdf_movements': self._deduplicate_pdf_movements(pdf_movements),
        }

        return set_response(result, 200, '')

    @route('/prepare-draft', methods=['POST'])
    def prepare_draft(self):
        payload = request.get_json(silent=True) or {}

        account = str(payload.get('account') or '').strip()
        date_from = str(payload.get('date_from') or '').strip()
        date_to = str(payload.get('date_to') or '').strip()
        criterion = str(payload.get('criterion') or '').strip() or 'auto'
        bank = str(payload.get('bank') or '').strip()
        brand = str(payload.get('brand') or '').strip()
        pdf_count = int(payload.get('pdf_count') or 0)
        pdf_names = payload.get('pdf_names') or []

        if not account or not date_from or not date_to:
            return set_response([], 400, 'Debe indicar cuenta y rango de fechas para preparar la conciliacion.')

        parsed_from = self._parse_iso_date(date_from)
        parsed_to = self._parse_iso_date(date_to)
        if not parsed_from or not parsed_to:
            return set_response([], 400, 'Las fechas recibidas no son validas.')

        draft_id = self._build_conciliation_id()
        observation_parts = [
            'CONCILIACION TARJETAS',
            f'Banco: {bank or "No detectado"}',
            f'Tarjeta: {brand or "No detectada"}',
            f'Criterio: {criterion}',
            f'PDFs: {pdf_count}',
        ]
        if pdf_names:
            observation_parts.append('Archivos: ' + ', '.join([str(name).strip() for name in pdf_names[:3] if str(name).strip()]))
        observation_text = ' | '.join(observation_parts)[:255]

        sql = """
INSERT INTO dbo.MV_CONCILIACION_CAB
    (IdConciliacion, Fecha, Cuenta, Observaciones, Usuario, FechaDesde, FechaHasta, UNegocio, Finalizada, Tipo, FechaDesde2, FechaHasta2)
VALUES
    (?, GETDATE(), ?, ?, ?, ?, ?, NULL, 0, ?, NULL, NULL)
""".strip()

        connection = get_conn(self.token_global)
        if connection == '':
            return set_response([], 500, 'No se pudo obtener la conexion con la base seleccionada.')

        try:
            cursor = connection.cursor()
            cursor.execute(
                sql,
                draft_id,
                account,
                observation_text,
                self.code_account,
                parsed_from,
                parsed_to,
                'TJ',
            )
            connection.commit()
        except Exception as e:
            try:
                connection.rollback()
            except Exception:
                pass
            return set_response([], 500, f'No se pudo guardar el borrador de conciliacion. {e}')
        finally:
            try:
                connection.close()
            except Exception:
                pass

        return set_response({
            'id_conciliacion': draft_id,
            'account': account,
            'date_from': date_from,
            'date_to': date_to,
            'bank': bank or 'No detectado',
            'brand': brand or 'No detectada',
            'criterion': criterion,
            'pdf_count': pdf_count,
            'status': 'draft_created',
        }, 200, '')

    @route('/system-movements', methods=['POST'])
    def system_movements(self):
        payload = request.get_json(silent=True) or {}

        reconciliation_id = str(payload.get('id_conciliacion') or '').strip()
        account = str(payload.get('account') or '').strip()
        date_from = str(payload.get('date_from') or '').strip()
        date_to = str(payload.get('date_to') or '').strip()
        bank = str(payload.get('bank') or '').strip()
        brand = str(payload.get('brand') or '').strip()

        if not reconciliation_id:
            return set_response([], 400, 'Debe indicar un IdConciliacion valido.')

        parsed_from = self._parse_iso_date(date_from) if date_from else None
        parsed_to = self._parse_iso_date(date_to) if date_to else None
        if (date_from and not parsed_from) or (date_to and not parsed_to):
            return set_response([], 400, 'Las fechas recibidas no son validas.')

        connection = get_conn(self.token_global)
        if connection == '':
            return set_response([], 500, 'No se pudo obtener la conexion con la base seleccionada.')

        try:
            cursor = connection.cursor()

            draft_sql = """
SELECT TOP 1
    RTRIM(IdConciliacion) AS id_conciliacion,
    RTRIM(Cuenta) AS account,
    CONVERT(varchar(10), FechaDesde, 23) AS date_from,
    CONVERT(varchar(10), FechaHasta, 23) AS date_to,
    RTRIM(Observaciones) AS observaciones,
    ISNULL(Finalizada, 0) AS finalizada
FROM dbo.MV_CONCILIACION_CAB
WHERE RTRIM(IdConciliacion) = ?
""".strip()
            cursor.execute(draft_sql, reconciliation_id)
            draft_row = cursor.fetchone()

            if not draft_row:
                return set_response([], 404, 'No se encontro el borrador de conciliacion indicado.')

            columns = [column[0] for column in cursor.description]
            draft = dict(zip(columns, draft_row))

            account = account or str(draft.get('account') or '').strip()
            date_from = date_from or str(draft.get('date_from') or '').strip()
            date_to = date_to or str(draft.get('date_to') or '').strip()

            parsed_from = self._parse_iso_date(date_from) if date_from else None
            parsed_to = self._parse_iso_date(date_to) if date_to else None

            if not account or not parsed_from or not parsed_to:
                return set_response([], 400, 'El borrador no tiene cuenta o fechas validas para cargar movimientos del sistema.')

            movement_sql = """
SELECT
    CONVERT(varchar(10), a.FECHA, 23) AS fecha,
    RTRIM(ISNULL(a.CUENTA, '')) AS cuenta,
    RTRIM(ISNULL(c.DESCRIPCION, '')) AS cuenta_descripcion,
    RTRIM(ISNULL(a.TC, '')) AS tc,
    RTRIM(ISNULL(a.SUCURSAL, '')) AS sucursal,
    RTRIM(ISNULL(a.NUMERO, '')) AS numero,
    RTRIM(ISNULL(a.LETRA, '')) AS letra,
    RTRIM(ISNULL(a.TC, '')) + '-' + RTRIM(ISNULL(a.SUCURSAL, '')) + RTRIM(ISNULL(a.NUMERO, '')) + RTRIM(ISNULL(a.LETRA, '')) AS comprobante,
    RTRIM(ISNULL(a.SUCURSAL, '')) + RTRIM(ISNULL(a.NUMERO, '')) + RTRIM(ISNULL(a.LETRA, '')) AS idcomprobante,
    RTRIM(ISNULL(a.DETALLE, '')) AS detalle,
    CAST(ISNULL(a.IMPORTE, 0) AS decimal(18, 2)) AS importe,
    CAST(ISNULL(a.IMPORTE, 0) AS decimal(18, 2)) AS saldo,
    RTRIM(ISNULL(a.[DEBE-HABER], '')) AS debe_haber,
    ISNULL(mc.ID, 0) AS conciliacion_det_id,
    ISNULL(mc.Conciliado, 0) AS conciliado,
    RTRIM(ISNULL(mc.Comprobante, '')) AS conciliado_con,
    RTRIM(ISNULL(mc.Concepto, '')) AS concepto_conciliacion
FROM dbo.MV_ASIENTOS a
LEFT JOIN dbo.MA_CUENTAS c ON a.CUENTA = c.CODIGO
LEFT JOIN dbo.MV_CONCILIACION mc
    ON RTRIM(mc.IdConciliacion) = ?
   AND RTRIM(ISNULL(mc.TC, '')) = RTRIM(ISNULL(a.TC, ''))
   AND RTRIM(ISNULL(mc.IdComprobante, '')) = RTRIM(ISNULL(a.SUCURSAL, '')) + RTRIM(ISNULL(a.NUMERO, '')) + RTRIM(ISNULL(a.LETRA, ''))
WHERE RTRIM(a.CUENTA) = ?
  AND CONVERT(date, a.FECHA) BETWEEN ? AND ?
ORDER BY a.FECHA, a.TC, a.SUCURSAL, a.NUMERO, a.LETRA
""".strip()
            cursor.execute(movement_sql, reconciliation_id, account, parsed_from, parsed_to)
            movement_columns = [column[0] for column in cursor.description]
            rows = [dict(zip(movement_columns, row)) for row in cursor.fetchall()]
        except Exception as e:
            return set_response([], 500, f'No se pudieron obtener los movimientos del sistema. {e}')
        finally:
            try:
                connection.close()
            except Exception:
                pass

        pending = []
        matched = []
        for index, row in enumerate(rows, start=1):
            normalized = self._serialize_system_movement(row, index)
            if normalized['estado_conciliacion'] == 'conciliado':
                matched.append(normalized)
            else:
                pending.append(normalized)

        return set_response({
            'summary': {
                'account': account,
                'date_from': date_from,
                'date_to': date_to,
                'pending_count': len(pending),
                'matched_count': len(matched),
                'total_count': len(rows),
                'bank': bank or 'No detectado',
                'brand': brand or 'No detectada',
                'id_conciliacion': reconciliation_id,
                'draft_finalizada': bool(draft.get('finalizada') or 0),
            },
            'pending': pending,
            'matched': matched,
        }, 200, '')

    def _serialize_system_movement(self, row: dict, index: int):
        tc = str(row.get('tc') or '').strip()
        idcomprobante = str(row.get('idcomprobante') or '').strip()
        conciliado = bool(row.get('conciliado') or 0)
        importe = row.get('importe')
        saldo = row.get('saldo')

        try:
            importe = float(importe or 0)
        except Exception:
            importe = 0.0

        try:
            saldo = float(saldo or 0)
        except Exception:
            saldo = importe

        return {
            'id': f'{tc or "MOV"}-{idcomprobante or index}',
            'fecha': str(row.get('fecha') or '').strip(),
            'comprobante': str(row.get('comprobante') or '').strip(),
            'detalle': str(row.get('detalle') or '').strip() or str(row.get('concepto_conciliacion') or '').strip(),
            'importe': importe,
            'saldo': 0.0 if conciliado else saldo,
            'sucursal': str(row.get('sucursal') or '').strip(),
            'terminal': '',
            'lote': '',
            'estado_conciliacion': 'conciliado' if conciliado else 'pendiente',
            'origen': 'sistema',
            'tc': tc,
            'idcomprobante': idcomprobante,
            'debe_haber': str(row.get('debe_haber') or '').strip(),
            'cuenta_descripcion': str(row.get('cuenta_descripcion') or '').strip(),
            'conciliado_con': str(row.get('conciliado_con') or '').strip(),
        }

    def _extract_pdf_movements(self, extracted_text: str, filename: str, brand: str, bank: str, period: dict):
        lines = self._prepare_pdf_lines(extracted_text)
        detected = []

        for index, line in enumerate(lines, start=1):
            movement = self._parse_pdf_movement_line(line, index, filename, brand, bank, period)
            if movement:
                detected.append(movement)

        if detected:
            return self._deduplicate_pdf_movements(detected)

        fallback = self._extract_summary_pdf_movements(extracted_text, filename, brand, bank, period)
        return self._deduplicate_pdf_movements(fallback)

    def _prepare_pdf_lines(self, text: str):
        prepared = []
        for raw_line in (text or '').splitlines():
            line = self._compact_spaces(raw_line)
            if not line or len(line) < 18:
                continue
            prepared.append(line)
        return prepared

    def _parse_pdf_movement_line(self, line: str, index: int, filename: str, brand: str, bank: str, period: dict):
        lowered = line.lower()
        if not re.search(r'(\d{2}/\d{2}(?:/\d{2,4})?|\d{4}-\d{2}-\d{2})', line):
            return None
        if not re.search(r'\d+[\.,]\d{2}', line):
            return None
        if re.search(r'(fecha de emision|entidad pagadora|resumen mensual|pagina\s+\d+|cuit\s*:)', lowered):
            return None

        amounts = self._extract_amounts(line)
        if not amounts:
            return None

        movement_date = self._extract_first_date_iso(line, period) or period.get('to') or period.get('from') or ''
        amount = amounts[-1]
        lote_match = re.search(r'\blote\s*:?\s*([A-Z0-9-]+)', line, re.IGNORECASE)
        terminal_match = re.search(r'(?:terminal|comercio|pdv|pos|n(?:ro|o)?\s*comercio)\s*:?\s*([A-Z0-9-]{3,})', line, re.IGNORECASE)
        comp_match = re.search(r'(?:liquidacion|cupon|operacion|comprobante|ref(?:erencia)?)\s*:?\s*([A-Z0-9-]{4,})', line, re.IGNORECASE)

        detail = re.sub(r'\s*\$?\s*-?\d{1,3}(?:\.\d{3})*,\d{2}', '', line).strip(' -')
        if not detail:
            detail = f'{brand or "Tarjeta"} {bank or "Banco"} movimiento PDF {index}'

        return {
            'id': f'PDF-{index}',
            'fecha': movement_date,
            'comprobante': comp_match.group(1).strip() if comp_match else self._build_pdf_reference(filename, index),
            'detalle': detail[:180],
            'importe': amount,
            'lote': lote_match.group(1).strip() if lote_match else '',
            'terminal': terminal_match.group(1).strip() if terminal_match else '',
            'estado_conciliacion': 'pendiente',
            'origen': bank or 'PDF',
        }

    def _extract_summary_pdf_movements(self, text: str, filename: str, brand: str, bank: str, period: dict):
        compact = self._compact_spaces(text)
        summary_rules = [
            ('Neto de pagos', [r'neto\s+de\s+pagos?\s*:?\s*([$\-\d\.,]+)']),
            ('Total presentado', [r'total\s+presentado\s*:?\s*([$\-\d\.,]+)']),
            ('Arancel', [r'arancel\s*:?\s*([$\-\d\.,]+)']),
            ('Retencion', [r'retencion\s*:?\s*([$\-\d\.,]+)']),
            ('Percepcion', [r'percepcion\s*:?\s*([$\-\d\.,]+)']),
        ]

        detected = []
        for label, patterns in summary_rules:
            for pattern in patterns:
                match = re.search(pattern, compact, re.IGNORECASE)
                if not match:
                    continue
                amount = self._parse_amount(match.group(1))
                if amount is None:
                    continue
                detected.append({
                    'id': f'SUM-{len(detected) + 1}',
                    'fecha': period.get('to') or period.get('from') or self._extract_first_date_iso(compact, period) or '',
                    'comprobante': self._build_pdf_reference(filename, len(detected) + 1),
                    'detalle': f'{label} {brand or "Tarjeta"} {bank or "Banco"}'.strip(),
                    'importe': amount,
                    'lote': '',
                    'terminal': self._extract_terminal(compact),
                    'estado_conciliacion': 'pendiente',
                    'origen': bank or 'PDF',
                })
                break

        return detected

    def _extract_amounts(self, text: str):
        found = []
        for raw in re.findall(r'(?<!\d)(-?\$?\s*\d{1,3}(?:\.\d{3})*,\d{2}|-?\$?\s*\d+,\d{2})(?!\d)', text or ''):
            amount = self._parse_amount(raw)
            if amount is not None:
                found.append(amount)
        return found

    def _parse_amount(self, raw_value: str):
        cleaned = (raw_value or '').strip()
        if not cleaned:
            return None
        cleaned = cleaned.replace('$', '').replace(' ', '')
        negative = cleaned.startswith('-')
        cleaned = cleaned.replace('-', '')
        if ',' in cleaned:
            cleaned = cleaned.replace('.', '').replace(',', '.')
        try:
            value = float(cleaned)
        except Exception:
            return None
        return -value if negative else value

    def _extract_first_date_iso(self, text: str, period: dict = None):
        match = re.search(r'(\d{2})/(\d{2})/(20\d{2})', text or '')
        if match:
            day, month, year = match.groups()
            parsed = self._safe_date_parse(year, month, day)
            if parsed:
                return parsed.strftime('%Y-%m-%d')

        match = re.search(r'(20\d{2})-(\d{2})-(\d{2})', text or '')
        if match:
            year, month, day = match.groups()
            parsed = self._safe_date_parse(year, month, day)
            if parsed:
                return parsed.strftime('%Y-%m-%d')

        match = re.search(r'(\d{2})/(\d{2})(?!/)', text or '')
        if match:
            day, month = match.groups()
            year_match = re.search(r'(20\d{2})', text or '')
            if year_match:
                parsed = self._safe_date_parse(year_match.group(1), month, day)
                if parsed:
                    return parsed.strftime('%Y-%m-%d')
            if period and period.get('to'):
                parsed = self._safe_date_parse(period.get('to')[:4], month, day)
                if parsed:
                    return parsed.strftime('%Y-%m-%d')

        return ''

    def _extract_terminal(self, text: str):
        match = re.search(r'(?:n(?:ro|o)?\s*comercio|terminal|pdv|pos)\s*:?\s*([A-Z0-9-]{4,})', text or '', re.IGNORECASE)
        return match.group(1).strip() if match else ''

    def _build_pdf_reference(self, filename: str, index: int):
        basename = re.sub(r'\.pdf$', '', filename or '', flags=re.IGNORECASE)
        basename = re.sub(r'[^A-Za-z0-9]+', '-', basename).strip('-')
        basename = basename[:24] if basename else 'PDF'
        return f'{basename}-{index:03d}'

    def _deduplicate_pdf_movements(self, movements):
        result = []
        seen = set()
        for item in movements:
            key = (
                str(item.get('fecha') or '').strip(),
                str(item.get('comprobante') or '').strip(),
                str(item.get('detalle') or '').strip(),
                float(item.get('importe') or 0),
            )
            if key in seen:
                continue
            seen.add(key)
            result.append(item)
        return result

    def _extract_pdf_text(self, file_item):
        try:
            raw_bytes = file_item.read() or b''
            file_item.stream.seek(0)

            if PdfReader is not None:
                reader = PdfReader(io.BytesIO(raw_bytes))
                text_parts = []
                for page in reader.pages[:8]:
                    text_parts.append(page.extract_text() or '')
                extracted = '\n'.join(text_parts).strip()
                if extracted:
                    return extracted

            return raw_bytes[:350000].decode('latin-1', errors='ignore')
        except Exception:
            try:
                file_item.stream.seek(0)
            except Exception:
                pass
            return ''

    def _normalize_bank_hint(self, bank_hint: str) -> str:
        mapping = {
            'auto': 'Detectar desde PDF',
            'banco-nacion': 'Banco Nacion',
            'banco-patagonia': 'Banco Patagonia',
            'prisma': 'Prisma',
            'fiserv': 'Fiserv / First Data',
            'mercado-pago': 'Mercado Pago',
            'otro': 'Otro / revisar manualmente',
        }
        return mapping.get(bank_hint, bank_hint or 'Detectar desde PDF')

    def _detect_brand(self, text: str) -> str:
        normalized = text.lower()
        patterns = [
            ('Mastercard', [r'mastercard', r'\bmaster\b']),
            ('Visa', [r'\bvisa\b']),
            ('Amex', [r'\bamex\b', r'american\s*express']),
            ('Cabal', [r'\bcabal\b']),
            ('Maestro', [r'\bmaestro\b']),
        ]

        for label, pattern_list in patterns:
            for pattern in pattern_list:
                if re.search(pattern, normalized, re.IGNORECASE):
                    return label

        return 'No detectada'

    def _detect_bank(self, text: str, bank_hint: str) -> str:
        normalized = text.lower()
        compact = self._compact_spaces(text)

        pagador_match = re.search(r'pagador\s*:?\s*(.+)', compact, re.IGNORECASE)
        if pagador_match:
            pagador_line = pagador_match.group(1)[:120]
            detected = self._match_known_bank(pagador_line)
            if detected:
                return detected

        detected = self._match_known_bank(normalized)
        if detected:
            return detected

        hardcoded_tokens = [
            ('Banco Patagonia', ['PATAGONIA', 'BANCO PATAGONIA']),
            ('Banco Nacion', ['BANCO NACION', 'NACION ARGENTINA']),
            ('Prisma', ['PAYWAY', 'PRISMA']),
            ('Fiserv / First Data', ['FISERV', 'FIRST DATA']),
        ]
        upper_text = text.upper()
        for label, tokens in hardcoded_tokens:
            if any(token in upper_text for token in tokens):
                return label

        normalized_hint = self._normalize_bank_hint(bank_hint)
        if normalized_hint != 'Detectar desde PDF':
            return normalized_hint

        return 'No detectado'

    def _match_known_bank(self, text: str):
        patterns = [
            ('Banco Patagonia', [r'banco\s+patagonia', r'patagonia\s+s\.a\.?', r'\bpatagonia\b']),
            ('Banco Nacion', [r'banco\s+nacion', r'banco\s+de\s+la\s+nacion', r'\bnacion\b']),
            ('Prisma', [r'\bprisma\b', r'payway']),
            ('Fiserv / First Data', [r'\bfiserv\b', r'first\s*data']),
            ('Mercado Pago', [r'mercado\s*pago']),
            ('Banco Santander', [r'\bsantander\b']),
            ('Banco Galicia', [r'\bgalicia\b']),
            ('BBVA', [r'\bbbva\b']),
        ]

        for label, pattern_list in patterns:
            for pattern in pattern_list:
                if re.search(pattern, text, re.IGNORECASE):
                    return label
        return None

    def _detect_period(self, text: str, filename: str):
        emission_date = self._extract_emission_date(text)
        if emission_date:
            return self._month_range(emission_date.year, emission_date.month)

        filename_date = self._extract_filename_date(filename)
        if filename_date:
            return self._month_range(filename_date.year, filename_date.month)

        return {}

    def _extract_emission_date(self, text: str):
        compact = self._compact_spaces(text)
        match = re.search(r'fecha\s+de\s+emision\s*:?\s*(\d{2})/(\d{2})/(20\d{2})', compact, re.IGNORECASE)
        if match:
            day, month, year = match.groups()
            return self._safe_date_parse(year, month, day)

        if self._is_monthly_summary(text):
            all_dates = self._extract_exact_dates(text)
            if all_dates:
                return max(all_dates)

        return None

    def _is_monthly_summary(self, text: str) -> bool:
        return bool(re.search(r'resumen\s+mensual\s+de\s+liquidaciones', text, re.IGNORECASE))

    def _extract_filename_date(self, filename: str):
        exact_date_match = re.search(r'(20\d{2})-(\d{2})-(\d{2})', filename)
        if exact_date_match:
            year, month, day = exact_date_match.groups()
            return self._safe_date_parse(year, month, day)

        compact_match = re.search(r'(20\d{2})(\d{1,2})(?!\d)', filename)
        if compact_match:
            year = compact_match.group(1)
            month = compact_match.group(2)
            return self._safe_date_parse(year, month, '01')

        return None

    def _extract_exact_dates(self, text: str):
        found = []

        for year, month, day in re.findall(r'(20\d{2})-(\d{2})-(\d{2})', text):
            parsed = self._safe_date_parse(year, month, day)
            if parsed:
                found.append(parsed)

        for day, month, year in re.findall(r'(\d{2})/(\d{2})/(20\d{2})', text):
            parsed = self._safe_date_parse(year, month, day)
            if parsed:
                found.append(parsed)

        return self._unique_dates(found)

    def _month_range(self, year: int, month: int):
        last_day = calendar.monthrange(year, month)[1]
        return {
            'from': f'{year}-{month:02d}-01',
            'to': f'{year}-{month:02d}-{last_day:02d}',
            'label': f'{year}-{month:02d}',
        }

    def _compact_spaces(self, text: str) -> str:
        return re.sub(r'\s+', ' ', text or '').strip()

    def _safe_date_parse(self, year: str, month: str, day: str):
        try:
            return datetime(int(year), int(month), int(day))
        except Exception:
            return None

    def _parse_iso_date(self, value: str):
        try:
            return datetime.strptime(value, '%Y-%m-%d')
        except Exception:
            return None

    def _build_conciliation_id(self):
        return datetime.now().strftime('%y%m%d%H%M%S') + f'{random.randint(0, 99):02d}'

    def _unique_dates(self, values):
        unique = []
        keys = set()
        for value in values:
            if not value:
                continue
            key = value.strftime('%Y-%m-%d')
            if key not in keys:
                keys.add(key)
                unique.append(value)
        return unique

    def _unique(self, values):
        result = []
        seen = set()
        for value in values:
            current = (value or '').strip()
            if current and current not in seen:
                seen.add(current)
                result.append(current)
        return result

