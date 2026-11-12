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
from pypdf.generic import ContentStream

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


def posicionadas(reader):
    """Líneas con la posición horizontal de cada texto. El PDF de Patagonia no separa columnas en el
    texto extraído: débito, crédito y saldo solo se distinguen por dónde termina cada importe."""
    paginas = []
    for page in reader.pages:
        fonts = page['/Resources'].get('/Font', {})
        font, size, line_x, lines = None, 1.0, 0.0, []
        for operands, operator in ContentStream(page.get_contents(), reader).operations:
            if operator == b'Tm':
                line_x = float(operands[4])
            elif operator == b'Td':
                line_x += float(operands[0])
            elif operator == b'Tf':
                font, size = fonts[operands[0]].get_object(), float(operands[1])
            elif operator == b'TJ' and font is not None and '/Widths' in font:
                scale = (font.get('/FontMatrix') or [0.001])[0]
                first, last, widths = font['/FirstChar'], font['/LastChar'], font['/Widths']
                x, items = line_x, []
                for item in operands[0]:
                    if isinstance(item, (int, float)) or hasattr(item, 'as_numeric'):
                        x -= float(item) * size / 1000
                        continue
                    raw = item.original_bytes if hasattr(item, 'original_bytes') else bytes(item)
                    width = sum(widths[c - first] for c in raw if first <= c <= last) * scale * size
                    items.append((x, x + width, raw.decode('latin1')))
                    x += width
                lines.append(items)
        paginas.append(lines)
    return paginas


def patagonia(reader):
    rows, initial, previous, final = [], None, None, None
    for page, lines in enumerate(posicionadas(reader), 1):
        if final is not None:
            break
        header = next((l for l in lines if {'DEBITOS', 'CREDITOS', 'SALDO'} <= {t for _, _, t in l}), None)
        if header is None:
            continue
        end = {t: x1 for _, x1, t in header}
        refer = next((x0 for x0, _, t in header if t == 'REFER.'), None)
        debit_end, credit_end, saldo_end = end['DEBITOS'], end['CREDITOS'], end['SALDO']
        for line in lines[lines.index(header) + 1:]:
            if not line or not re.fullmatch(r'\d{1,2}/\d{2}/\d{2}', line[0][2]):
                continue
            amounts = [(x1, t) for _, x1, t in line[1:] if re.fullmatch(AR, t)]
            texts = [(x0, t) for x0, _, t in line[1:] if not re.fullmatch(AR, t)]
            description = ' '.join(t for x0, t in texts if refer is None or x0 < refer - 20).strip()
            reference = ' '.join(t for x0, t in texts if refer is not None and x0 >= refer - 20)
            debit = credit = saldo = None
            for x1, t in amounts:
                if x1 <= (debit_end + credit_end) / 2:
                    column = 'debit'
                elif x1 <= (credit_end + saldo_end) / 2:
                    column = 'credit'
                else:
                    column = 'saldo'
                if {'debit': debit, 'credit': credit, 'saldo': saldo}[column] is not None:
                    raise ValueError(f'Extracto: una fila de la página {page} de Patagonia tiene dos importes en la misma columna. No se importó el archivo.')
                value = amount(t)
                debit, credit, saldo = (value if column == 'debit' else debit, value if column == 'credit' else credit, value if column == 'saldo' else saldo)
            if description.startswith('SALDO ANTERIOR'):
                if saldo is None or initial is not None:
                    raise ValueError('Extracto: el saldo anterior de Patagonia no se pudo leer o está repetido.')
                initial = previous = saldo
                continue
            if initial is None:
                raise ValueError('Extracto: no se encontró el saldo anterior de Patagonia antes de los movimientos.')
            if description.startswith('SALDO ACTUAL'):
                # Cierre del detalle: lo que sigue (débitos automáticos, transferencias recibidas) son tablas informativas.
                final = saldo
                break
            if (debit is None) == (credit is None):
                raise ValueError(f'Extracto: no se pudo separar débito y crédito de Patagonia en la página {page}. No se importó el archivo.')
            value = credit if debit is None else -debit
            if not value:
                raise ValueError('Extracto: hay un movimiento sin importe. Revisá el documento.')
            previous += value
            rows.append(movement(line[0][2], description, value, saldo, reference, page))
            if saldo is not None and saldo != previous:
                raise ValueError(f'Extracto: el saldo de Patagonia no coincide en la página {page}. Revisá el PDF original.')
    if initial is None or final is None or previous != final:
        raise ValueError('Extracto: el saldo final de Patagonia no coincide con los movimientos leídos. No se importó el archivo.')
    return rows, initial, previous


GAL_IMPORTE = re.compile(r'(?<![\d.,])-?\d{1,3}(?:\.\d{3})*,\d{2}(?![\d,])')


def galicia(pages):
    """Resumen de cuenta corriente en pesos de Galicia: cada fila trae importe con signo (débito negativo) y saldo.
    El saldo corrido de cada fila valida el signo y el importe; las líneas siguientes son el detalle de la operación."""
    saldos = re.findall(r'\$\s*(\d{1,3}(?:\.\d{3})*,\d{2})', pages[0])
    if len(saldos) < 2:
        raise ValueError('Extracto: no se encontraron los saldos inicial y final de Galicia. No se importó ningún movimiento.')
    initial, closing = [Decimal(v.replace('.', '').replace(',', '.')) for v in saldos[:2]]
    rows, state = [], {'previous': initial, 'current': None}

    def flush():
        current = state['current']
        if current is None:
            return
        state['current'] = None
        extra = current['extra']
        description, reference = current['description'], ''
        operation = re.match(r'Operaci\S*n\s+(\S+)$', extra[0]) if extra else None
        if operation:
            reference = operation[1]
        elif extra:
            description = f"{description} {extra[0]}".strip()
            reference = ' '.join(extra[1:])[:120]
        state['previous'] += current['value']
        if state['previous'] != current['saldo']:
            raise ValueError(f"Extracto: el saldo de Galicia no coincide en la página {current['page']}. Revisá el PDF original.")
        rows.append(movement(current['day'], description, current['value'], current['saldo'], reference, current['page']))

    for page, text in enumerate(pages, 1):
        in_table = False
        for line in text.splitlines():
            if re.match(r'\s*Fecha\s+Descripci.n\s+Origen', line):
                in_table = True
                continue
            if not in_table:
                continue
            if re.search(r'Resumen de Cuenta Corriente en Pesos\s+P.gina', line):
                in_table = False
                continue
            if re.match(r'\s*Total\b', line):
                flush()
                in_table = False
                continue
            row = re.match(r'\s*(\d{2}/\d{2}/\d{2})\s{2,}(\S.*)$', line)
            if row:
                flush()
                description = re.split(r'\s{2,}', row[2])[0].strip()
                values = [Decimal(v.replace('.', '').replace(',', '.')) for v in GAL_IMPORTE.findall(row[2][len(description):])]
                if len(values) != 2 or not values[0]:
                    raise ValueError(f'Extracto: una fila de la página {page} de Galicia no tiene importe y saldo legibles. No se importó el archivo.')
                state['current'] = dict(day=row[1], description=description, value=values[0], saldo=values[1], page=page, extra=[])
            elif state['current'] is not None and line.strip():
                state['current']['extra'].append(line.strip())
    flush()
    if state['previous'] != closing:
        raise ValueError('Extracto: el saldo final de Galicia no coincide con los movimientos leídos. No se importó el archivo.')
    return rows, initial, state['previous']


def read_pdf(data):
    reader = PdfReader(io.BytesIO(data))
    if len(reader.pages) > 400:
        raise ValueError('Extracto: el PDF supera las 400 páginas.')
    pages = [p.extract_text(extraction_mode='layout') or '' for p in reader.pages]
    text = '\n'.join(pages)
    if not text.strip():
        raise ValueError('Extracto: el PDF no contiene texto legible. Para documentos escaneados, utilizá el Excel o CSV del banco.')
    plain = '\n'.join(p.extract_text() or '' for p in reader.pages)
    if len(set(re.findall(r'\b011\d{19}\b', text))) > 1 or len(set(re.findall(r'CUENTA CORRIENTE EN PESOS[^\n]*?(\d-\d{3}-\d+-\d)', text))) > 1:
        raise ValueError('Extracto: el PDF contiene más de una cuenta bancaria. Descargá un archivo por cuenta para no mezclar movimientos.')
    if re.search(r'CUENTA CORRIENTE- PESOS', text) and re.search(r'\b011\d{19}\b', text):
        bank, result = 'Nacion', nacion(pages)
    elif 'Banco Hipotecario S.A.' in text and 'DETALLE DE MOVIMIENTOS' in text and 'CUENTA CORRIENTE EN PESOS' in text:
        bank, result = 'Hipotecario', hipotecario(pages)
    elif re.search(r'ESTADO DE CUENTAS UNIFICADO', plain) and 'SALDO ANTERIOR' in plain and 'Patagonia' in plain:
        if len(set(re.findall(r'SUBCTA\s+\d+\s+SUC\s+\d+', plain))) > 1:
            raise ValueError('Extracto: el PDF contiene más de una cuenta bancaria. Descargá un archivo por cuenta para no mezclar movimientos.')
        bank, result = 'Patagonia', patagonia(reader)
    elif re.search(r'\b007\d{19}\b', plain) and re.search(r'Resumen de\s+Cuenta Corriente en Pesos', plain):
        if len(set(re.findall(r'\b007\d{19}\b', plain))) > 1:
            raise ValueError('Extracto: el PDF contiene más de una cuenta bancaria. Descargá un archivo por cuenta para no mezclar movimientos.')
        bank, result = 'Galicia', galicia(pages)
    else:
        raise ValueError('Extracto: formato PDF no reconocido. Se admiten los resúmenes de cuenta en pesos de Nación, Hipotecario, Patagonia y Galicia; otros formatos requieren adaptación.')
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
