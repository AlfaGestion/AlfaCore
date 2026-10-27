"""Lectura local de liquidaciones Nación, Patagonia e Hipotecario. No invoca IA ni escribe archivos.
Usa los formatos relevados por IA_ProcesarDocumentos; nunca ajusta importes para balancear.
Entrada: PDF por stdin. Salida: JSON UTF-8. Dependencia: pypdf.
"""
import io, json, re, sys
from decimal import Decimal
from datetime import datetime
from pypdf import PdfReader
MONEY = r"-?[\d.]+,\d{2}-?"
def amount(text):
    value=text.replace(".", "").replace(",", ".")
    return -Decimal(value[:-1]) if value.endswith("-") else Decimal(value)
def date(text, year=None):
    if len(text)==5: text += "/" + str(year)
    return datetime.strptime(text, "%d/%m/%Y" if len(text.split("/")[-1])==4 else "%d/%m/%y").strftime("%Y-%m-%d")
def read_pdf(data, name):
    reader=PdfReader(io.BytesIO(data))
    if len(reader.pages)>150: raise ValueError("El documento supera las 150 páginas.")
    pages=[p.extract_text(extraction_mode="layout") or "" for p in reader.pages]
    first=pages[0]; text="\n".join(pages)
    monthly_bank=re.search(r"PAGADOR:\s*\d+\s+(BANCO HIPOTECARIO|BANCO DE LA NACION ARGENTINA|BANCO PATAGONIA)\b",first,re.I)
    result={"Banco":"", "Comercio":"", "Tarjeta":"", "Bruto":"0", "Neto":"0", "Detalles":[], "Avisos":[]}
    brand=re.search(r"visa|master|maestro|cabal|vabal|amex|naranja",name,re.I)
    result["Tarjeta"]=(brand.group(0).upper().replace("VABAL","CABAL") if brand else "")
    details=result["Detalles"]
    if "BCO DE LA NACION" in first:
        result["Banco"]="Nación"
        merchant=re.search(r"Comercio:\s*(\d+)",first)
        result["Comercio"]=merchant.group(1) if merchant else ""
        for label,key in [("Total presentado:","Bruto"),("Neto de pagos:","Neto")]:
            m=re.search(re.escape(label)+r"\s*("+MONEY+")",first)
            if not m: raise ValueError("No se pudo leer "+label)
            result[key]=str(amount(m.group(1)))
        pending=[]
        for line in text.splitlines():
            row=re.search(r"Venta.*?\s(\d{2}/\d{2}/\d{2})\s+(\d+)\s+(\d+)\s+(\d+)\s+(.*)",line)
            if row:
                monies=re.findall(MONEY,row.group(5))
                if monies:
                    pending.append({"Fecha":date(row.group(1)),"FechaPago":None,"Liquidacion":"","Lote":row.group(3),"Cupon":row.group(4),"Terminal":row.group(2),"Cantidad":1,"Importe":str(amount(monies[0])),"Nivel":"Cupón"})
            if "F.de Pago:" in line and "Nro. Liq:" in line:
                paid=re.search(r"el d.a\s+(\d{2}/\d{2}/\d{4})",line)
                liq=re.search(r"Nro. Liq:\s*(\d+)",line)
                if paid and liq and pending:
                    for d in pending:
                        d["FechaPago"]=date(paid.group(1));d["Liquidacion"]=liq.group(1)
                    details.extend(pending);pending=[]
        if pending: result["Avisos"].append("Hay operaciones sin cierre de liquidación identificado; revisar el documento.")
    elif monthly_bank or "BANCO PATAGONIA" in first:
        result["Banco"]={"BANCO HIPOTECARIO":"Hipotecario","BANCO DE LA NACION ARGENTINA":"Nación","BANCO PATAGONIA":"Patagonia"}[monthly_bank.group(1).upper()] if monthly_bank else "Patagonia"
        emission=re.search(r"FECHA DE EMISION:\s*(\d{2}/\d{2}/\d{4})",first)
        if not emission: raise ValueError("No se pudo leer la fecha de emisión.")
        year=datetime.strptime(emission.group(1),"%d/%m/%Y").year
        merchant=re.search(r"ESTABLECIMIENTO:\s*(\d+)",first)
        result["Comercio"]=merchant.group(1) if merchant else ""
        for label,key in [(r"TOTAL PRESENTADO \$[^\n]*","Bruto"),(r"SALDO \$[^\n]*","Neto")]:
            m=re.search(label+r"[\s\S]*?("+MONEY+")",first)
            if not m: raise ValueError("No se pudo leer el total del resumen " + result["Banco"] + ".")
            result[key]=str(amount(m.group(1)))
        paid=present=liq=lot=None
        for line in text.splitlines():
            # El resumen de planes que sigue al último cierre no son nuevas ventas.
            if re.search(r"Total del d.a",line):
                paid=present=liq=lot=None
                continue
            m=re.search(r"FECHA DE PAGO\s+(\d{2}/\d{2})",line)
            if m: paid=date(m.group(1),year);present=liq=lot=None
            m=re.search(r"Fecha de presentaci.n\s+(\d{2}/\d{2})",line)
            if m: present=date(m.group(1),year)
            m=re.search(r"Liq. N.\s*(\d+)\s*-\s*Lote N.\s*(\d+)",line)
            if m: liq,lot=m.group(1),m.group(2)
            m=re.search(r"(\d+)\s+Ventas?.*?\$\s*("+MONEY+")",line)
            if m and paid and present and liq and lot:
                details.append({"Fecha":present,"FechaPago":paid,"Liquidacion":liq,"Lote":lot,"Cupon":"","Terminal":"","Cantidad":int(m.group(1)),"Importe":str(amount(m.group(2))),"Nivel":"Lote"})
        result["Avisos"].append("El resumen " + result["Banco"] + " identifica lotes, no cupones individuales.")
    elif "BANCO HIPOTECARIO" in first.upper() and "CUENTA CORRIENTE EN PESOS" in first.upper():
        raise ValueError("El documento es un extracto de cuenta corriente de Hipotecario, no una liquidación de tarjetas. No se importó. La lectura de este PDF bancario todavía no está habilitada en Conciliación bancaria.")
    elif "RESUMEN DE CUENTA" in first.upper() and re.search(r"CUENTA CORRIENTE\s*-?\s*PESOS",first,re.I):
        raise ValueError("El documento es un extracto de cuenta corriente, no una liquidación de tarjetas. No se importó. La lectura de este PDF bancario todavía no está habilitada en Conciliación bancaria.")
    else: raise ValueError("Formato no reconocido. Esta etapa admite los resúmenes mensuales Nación, Patagonia e Hipotecario relevados.")
    if not details: raise ValueError("No se pudo extraer detalle conciliable; no se importó el documento.")
    total=sum((Decimal(d["Importe"]) for d in details),Decimal(0))
    result["TotalDetalle"]=str(total)
    result["DetalleCompleto"]=(total==Decimal(result["Bruto"]))
    if not result["DetalleCompleto"]: result["Avisos"].append("El detalle extraído no coincide con el bruto del resumen. Revisar antes de conciliar.")
    return result
if __name__=="__main__":
    try:
        out=read_pdf(sys.stdin.buffer.read(),sys.argv[1] if len(sys.argv)>1 else "")
        sys.stdout.buffer.write(json.dumps(out,ensure_ascii=False).encode("utf-8"))
    except Exception as e:
        sys.stderr.write(str(e));sys.exit(1)
