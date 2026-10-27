"""Extractos bancarios PDF de texto. Entrada stdin; JSON UTF-8; sin red ni archivos temporales.
Los saldos se usan solo para validar movimientos; nunca se importan como operaciones.
"""
import io
import json
import re
import sys
from datetime import datetime
from decimal import Decimal
from pypdf import PdfReader

AR = r"-?(?:\d{1,3}(?:\.\d{3})+|\d+),\d{2}-?"
US = r"-?(?:\d{1,3}(?:,\d{3})+|\d+)\.\d{2}"
DATE = re.compile(r"^\s*_*(\s*)(\d{2}/\d{2}/(?:\d{4}|\d{2}))\s+")


def amount(text, us=False):
    text = text.strip()
    if us:
        return Decimal(text.replace(',', ''))
    text = text.replace('.', '').replace(',', '.')
    return -Decimal(text[:-1]) if text.endswith('-') else Decimal(text)


def movement(day, description, value, balance=None, reference='', page=1):
    parsed = datetime.strptime(day, '%d/%m/%Y' if len(day) == 10 else '%d/%m/%y')
    return dict(MovementDate=parsed.strftime('%Y-%m-%d'), Description=description.strip(),
                Amount=str(value), Balance=str(balance) if balance is not None else None,
                ExternalReference=reference.strip(), RawDataJson=json.dumps({'pagina': page}))


def nacion(pages):
    text = '\n'.join(pages)
    opening = re.search(r'SALDO ANTERIOR\s*(' + AR + ')', text)
    closing = re.search(r'SALDO FINAL\s+(' + AR + ')', text)
    if not opening or not closing:
        raise ValueError('Extracto: no se encontraron los saldos inicial y final de Nación. No se importó ningún movimiento.')
    initial = previous = amount(opening[1])
    rows = []
    for page, text in enumerate(pages, 1):
        if not re.search(r'FECHA\s+MOVIMIENTOS\s+COMPROB\.', text):
            continue
        for line in text.splitlines():
            day = DATE.match(line)
            if not day:
                continue
            body = re.split(r'SALDO ANTERIOR|TRANSPORTE', line[day.end():])[0]
            values = list(re.finditer(AR, body))
            if len(values) != 2:
                raise ValueError(f'Extracto: una fila de la página {page} de Nación no tiene importe y saldo legibles. No se importó el archivo.')
            unsigned, balance = [amount(v[0]) for v in values]
            value = balance - previous
            if abs(value) != abs(unsigned) or not value:
                raise ValueError(f'Extracto: los movimientos y saldos de Nación no coinciden en la página {page}. Revisá el PDF original.')
            description = body[:values[0].start()].strip()
            ref = re.search(r'(\d{4,})\s*$', description)
            rows.append(movement(day[2], description, value, balance, ref[1] if ref else '', page))
            previous = balance
    if previous != amount(closing[1]):
        raise ValueError('Extracto: el saldo final de Nación no coincide con los movimientos leídos. No se importó el archivo.')
    return rows, initial, previous


def hipotecario(pages):
    first = pages[0]
    summary = re.search(r'SALDO INICIAL\s+CREDITOS\s+DEBITOS\s+I\.V\.A\.\s+SALDO FINAL\s+([^\n]+)', first)
    values = re.findall(US, summary[1]) if summary else []
    if len(values) != 5:
        raise ValueError('Extracto: no se pudo validar el cuadro de saldos de Hipotecario.')
    initial, credits, debits, iva, final = map(lambda s: amount(s, True), values)
    previous = initial
    rows = []
    for page, text in enumerate(pages, 1):
        header = next((line for line in text.splitlines() if re.search(r'FECHA\s+DESCRIPCION.*REFERENCIA.*DEBITOS.*CREDITOS.*SALDO', line)), None)
        if header is None:
            continue
        # Se respeta la posición de las columnas en el PDF, no el orden de los números del texto.
        credit_start = header.index('CREDITOS') - (header.index('CREDITOS') - header.index('DEBITOS')) / 2
        saldo_start = header.index('SALDO') - (header.index('SALDO') - header.index('CREDITOS')) / 2
        detail_start = header.index('SUC.')
        reference_start = header.index('REFERENCIA')
        pending = None
        for line in text.splitlines()[text.splitlines().index(header)+1:]:
            day = DATE.match(line)
            if not day:
                # Descripciones extendidas, sin absorber leyendas o datos del pie de página.
                if pending is not None and line[day_end:detail_start].strip() and not line[:day_end].strip() and not line[detail_start:].strip():
                    pending['Description'] += ' ' + line[day_end:detail_start].strip()
                continue
            day_end = day.end()
            description = line[day_end:detail_start].strip()
            nums = list(re.finditer(US, line))
            if description.startswith('SALDO '):
                if len(nums) != 1 or amount(nums[0][0], True) != previous:
                    raise ValueError(f'Extracto: el saldo de Hipotecario no coincide en la página {page}.')
                pending = None
                continue
            if len(nums) != 1 or nums[0].start() < reference_start or nums[0].end() >= saldo_start:
                raise ValueError(f'Extracto: no se pudo separar débito y crédito de Hipotecario en la página {page}.')
            value = amount(nums[0][0], True)
            if nums[0].end() <= credit_start:
                value = -value
            if not value:
                raise ValueError('Extracto: hay un movimiento sin importe. Revisá el documento.')
            previous += value
            reference = line[reference_start:nums[0].start()].strip()
            pending = movement(day[2], description, value, previous, reference, page)
            rows.append(pending)
    if previous != final or sum(Decimal(r['Amount']) for r in rows if Decimal(r['Amount']) > 0) != credits or -sum(Decimal(r['Amount']) for r in rows if Decimal(r['Amount']) < 0) != debits + iva:
        raise ValueError('Extracto: los créditos, débitos y saldos de Hipotecario no coinciden. No se importó el archivo.')
    return rows, initial, final


def read_pdf(data):
    reader = PdfReader(io.BytesIO(data))
    if len(reader.pages) > 150:
        raise ValueError('Extracto: el PDF supera las 150 páginas.')
    pages = [p.extract_text(extraction_mode='layout') or '' for p in reader.pages]
    text = '\n'.join(pages)
    if not text.strip():
        raise ValueError('Extracto: el PDF no contiene texto legible. Para documentos escaneados, utilizá el Excel o CSV del banco.')
    if len(set(re.findall(r'\b011\d{19}\b', text))) > 1 or len(set(re.findall(r'CUENTA CORRIENTE EN PESOS[^\n]*?(\d-\d{3}-\d+-\d)', text))) > 1:
        raise ValueError('Extracto: el PDF contiene más de una cuenta bancaria. Descargá un archivo por cuenta para no mezclar movimientos.')
    if re.search(r'CUENTA CORRIENTE- PESOS', text) and re.search(r'\b011\d{19}\b', text):
        bank, result = 'Nacion', nacion(pages)
    elif 'Banco Hipotecario S.A.' in text and 'DETALLE DE MOVIMIENTOS' in text and 'CUENTA CORRIENTE EN PESOS' in text:
        bank, result = 'Hipotecario', hipotecario(pages)
    else:
        raise ValueError('Extracto: formato PDF no reconocido. Se admiten los resúmenes de cuenta en pesos de Nación e Hipotecario; otros formatos requieren adaptación.')
    rows, initial, final = result
    if not rows or len(rows) > 10000:
        raise ValueError('Extracto: el documento no contiene entre 1 y 10000 movimientos conciliables.')
    for row in rows:
        row['Source'] = bank
    return dict(Banco=bank, Movimientos=rows, SaldoInicial=str(initial), SaldoFinal=str(final))


if __name__ == '__main__':
    try:
        print(json.dumps(read_pdf(sys.stdin.buffer.read()), ensure_ascii=False))
    except ValueError as ex:
        print(str(ex), file=sys.stderr)
        sys.exit(1)
