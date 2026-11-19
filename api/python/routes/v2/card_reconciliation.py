import calendar
import re
from datetime import datetime

from flask import request
from flask_classful import route

from functions.responses import set_response
from routes.v2.master import MasterView


class CardReconciliationView(MasterView):

    @route('/analyze-pdfs', methods=['POST'])
    def analyze_pdfs(self):
        files = request.files.getlist('files')
        bank_hint = (request.form.get('bank_hint') or 'auto').strip().lower()

        if not files:
            return set_response([], 404, 'Debe adjuntar al menos un PDF.')

        parsed_files = []
        period_from_values = []
        period_to_values = []
        detected_brands = []
        detected_banks = []

        for item in files:
            filename = (item.filename or '').strip()
            raw_preview = item.read(350000) or b''
            item.stream.seek(0)
            preview_text = raw_preview.decode('latin-1', errors='ignore')
            combined_text = f'{filename}\n{preview_text}'

            brand = self._detect_brand(combined_text)
            bank = self._detect_bank(combined_text, bank_hint)
            period = self._detect_period(combined_text, filename)

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
                'size': item.content_length or len(raw_preview),
                'detected_brand': brand,
                'detected_bank': bank,
                'period_from': period.get('from', ''),
                'period_to': period.get('to', ''),
                'period_label': period.get('label', ''),
            })

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
        }

        return set_response(result, 200, '')

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
        patterns = [
            ('Banco Nacion', [r'banco\s+nacion', r'\bnacion\b']),
            ('Banco Patagonia', [r'banco\s+patagonia', r'\bpatagonia\b']),
            ('Prisma', [r'\bprisma\b']),
            ('Fiserv / First Data', [r'\bfiserv\b', r'first\s*data']),
            ('Mercado Pago', [r'mercado\s*pago']),
            ('Banco Santander', [r'\bsantander\b']),
            ('Banco Galicia', [r'\bgalicia\b']),
            ('BBVA', [r'\bbbva\b']),
        ]

        for label, pattern_list in patterns:
            for pattern in pattern_list:
                if re.search(pattern, normalized, re.IGNORECASE):
                    return label

        normalized_hint = self._normalize_bank_hint(bank_hint)
        if normalized_hint != 'Detectar desde PDF':
            return normalized_hint

        return 'No detectado'

    def _detect_period(self, text: str, filename: str):
        exact_dates = self._extract_exact_dates(text)
        if exact_dates:
            first_date = min(exact_dates)
            last_date = max(exact_dates)
            return {
                'from': first_date.strftime('%Y-%m-%d'),
                'to': last_date.strftime('%Y-%m-%d'),
                'label': first_date.strftime('%Y-%m') if first_date.year == last_date.year and first_date.month == last_date.month else f"{first_date.strftime('%Y-%m')} a {last_date.strftime('%Y-%m')}",
            }

        compact_match = re.search(r'(20\d{2})(\d{1,2})(?!\d)', filename)
        if compact_match:
            year = int(compact_match.group(1))
            month = int(compact_match.group(2))
            if 1 <= month <= 12:
                last_day = calendar.monthrange(year, month)[1]
                return {
                    'from': f'{year}-{month:02d}-01',
                    'to': f'{year}-{month:02d}-{last_day:02d}',
                    'label': f'{year}-{month:02d}',
                }

        return {}

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

    def _safe_date_parse(self, year: str, month: str, day: str):
        try:
            return datetime(int(year), int(month), int(day))
        except Exception:
            return None

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