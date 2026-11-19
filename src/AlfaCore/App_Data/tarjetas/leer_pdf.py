"""Lectura local de liquidaciones Nación, Patagonia e Hipotecario y del extracto Galicia (cobros NAVE con tarjeta). No invoca IA ni escribe archivos.
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

def conceptos_lt(text):
    """Desglose explícito Nación, sin recalcular impuestos ni completar diferencias."""
    rows=[]
    for line in text.splitlines():
        m=re.match(r"\s*([+-])\s+(.+?)\s+\$\s*("+MONEY+r")\s*$",line)
        if not m: continue
        label=" ".join(m.group(2).split());upper=label.upper()
        role=("Tarjeta" if upper.startswith("VENTAS") else
              "IvaCredito" if upper.startswith(("IVA CRED","IVA RI SERV.OPER.")) else
              "Iibb" if "ING.BRUTOS" in upper or "SIRTAC" in upper or upper.startswith("QR RETENCION IIBB") else
              "Ganancias" if "GANANCIA" in upper else
              "PercepcionIva" if "PERCEPCION IVA" in upper or "RETENCION IVA" in upper else
              "Gastos" if upper.startswith(("ARANCEL","SERVICIO OPER.","DTO. FINANC","DESCUENTO FINANC")) else "SinClasificar")
        value=amount(m.group(3))
        if m.group(1)=="-": value=-value
        # Signo económico: ingreso positivo, descuento negativo.
        rows.append({"Concepto":label,"Rol":role,"Importe":str(value)})
    return rows

def liquidaciones_lt(text, banco):
    if banco!="Nación" or "BCO DE LA NACION" not in text: return []
    result=[];start=0
    for close in re.finditer(r"F.de Pago:[^\n]*el d.a\s+(\d{2}/\d{2}/\d{4})[^\n]*Nro. Liq:\s*(\d+)",text):
        block=text[start:close.start()];start=close.end()
        rows=conceptos_lt(block)
        net=re.findall(r"IMPORTE NETO DE PAGOS\s*\$\s*("+MONEY+r")",block)
        if not rows or len(net)!=1: continue
        result.append({"Numero":close.group(2),"Fecha":date(close.group(1)),
                       "Neto":str(amount(net[0])),"Conceptos":rows})
    return result

def resumen_lt(text, result):
    """Resumen fiscal mensual: nunca reparte impuestos entre liquidaciones diarias."""
    compact="\n".join(" ".join(line.split()) for line in text.splitlines())
    summary=re.search(r"Base Imponible IVA Monto Gravado\s*\n([\s\S]*?)Fin de la informaci.n",compact,re.I)
    reference=re.search(r"N. DE RESUMEN:\s*(\d+)",compact)
    emission=re.search(r"FECHA DE EMISION:\s*(\d{2}/\d{2}/\d{4})",compact)
    if not summary or not reference or not emission: return []
    s=summary.group(1)
    rows=[{"Concepto":"TOTAL PRESENTADO","Rol":"Tarjeta","Importe":result["Bruto"]}]
    patterns=[("Gastos","Base imponible IVA / gastos",r"^Tasa [\d,]+ % \$ ("+MONEY+r")$"),
              ("IvaCredito","IVA crédito fiscal",r"^IVA [\d,]+ % \$ ("+MONEY+r")$"),
              ("Iibb","Retención IB",r"^Ret\.IB[^\n]*\n[\d,]+ % \$ ("+MONEY+r")$"),
              ("PercepcionIva","Percepción/retención AFIP-DGI",r"^Percep\./Retenc\.AFIP - DGI \$ ("+MONEY+r")$")]
    for role,label,pattern in patterns:
        values=re.findall(pattern,s,re.M|re.I)
        if values: rows.append({"Concepto":label,"Rol":role,"Importe":str(-sum((amount(v) for v in values),Decimal(0)))})
    return [{"Numero":reference.group(1),"Fecha":date(emission.group(1)),"Neto":result["Neto"],
             "EsResumenMensual":True,"Conceptos":rows}]

# Los saldos deudores (sobregiro) se informan con el signo al final: 46.829,90-
GAL_IMPORTE=re.compile(r"(?<![\d.,])-?\d{1,3}(?:\.\d{3})*,\d{2}-?(?![\d,])")
def gal_monto(v): return amount(v)
def galicia_nave(pages, result):
    """Extracto de cuenta corriente Galicia: toma solo los créditos «NAVE PAGO CON TARJETA».
    Cada fila es un cobro individual acreditado (neto de comisiones). La devolución NAVE se descuenta
    únicamente si su operación corresponde a un cobro con tarjeta. El saldo corrido de TODAS las filas
    se verifica para garantizar que no se omitió ni se alteró ninguna línea."""
    first=pages[0]
    saldos=re.findall(r"\$\s*(\d{1,3}(?:\.\d{3})*,\d{2})",first)
    if len(saldos)<2: raise ValueError("No se encontraron los saldos inicial y final del extracto Galicia.")
    inicial,cierre=[Decimal(v.replace(".","").replace(",",".")) for v in saldos[:2]]
    cuenta=re.search(r"N.\s*(\d{7}-\d\s+\d{3}-\d)",first)
    rows=[];state={"prev":inicial,"cur":None}
    def flush():
        c=state["cur"]
        if c is None: return
        state["cur"]=None
        state["prev"]+=c["valor"]
        if state["prev"]!=c["saldo"]: raise ValueError("El saldo del extracto Galicia no coincide en la página %d. Revisá el PDF original."%c["page"])
        op=re.match(r"Operaci\S*n\s+(\S+)$",c["extra"][0]) if c["extra"] else None
        rows.append((c["fecha"],c["desc"].upper(),c["valor"],op.group(1) if op else ""))
    for page,text in enumerate(pages,1):
        in_table=False
        for line in text.splitlines():
            if re.match(r"\s*Fecha\s+Descripci.n\s+Origen",line): in_table=True;continue
            if not in_table: continue
            if re.search(r"Resumen de Cuenta Corriente en Pesos\s+P.gina",line): in_table=False;continue
            if re.match(r"\s*Total\b",line): flush();in_table=False;continue
            row=re.match(r"\s*(\d{2}/\d{2}/\d{2})\s{2,}(\S.*)$",line)
            if row:
                flush()
                desc=re.split(r"\s{2,}",row.group(2))[0].strip()
                values=[gal_monto(v) for v in GAL_IMPORTE.findall(row.group(2)[len(desc):])]
                if len(values)!=2 or not values[0]: raise ValueError("Una fila de la página %d del extracto Galicia no tiene importe y saldo legibles. No se importó el archivo."%page)
                state["cur"]={"fecha":row.group(1),"desc":desc,"valor":values[0],"saldo":values[1],"page":page,"extra":[]}
            elif state["cur"] is not None and line.strip(): state["cur"]["extra"].append(line.strip())
    flush()
    if state["prev"]!=cierre: raise ValueError("El saldo final del extracto Galicia no coincide con los movimientos leídos. No se importó el archivo.")
    cobros={op for _,d,v,op in rows if d.startswith("NAVE PAGO CON TARJETA") and v>0 and op}
    detalles=result["Detalles"]
    for fecha,desc,valor,op in rows:
        if desc.startswith("NAVE PAGO CON TARJETA") and valor>0:
            detalles.append({"Fecha":date(fecha),"FechaPago":date(fecha),"Liquidacion":op,"Lote":"","Cupon":"","Terminal":"NAVE","Cantidad":1,"Importe":str(valor),"Nivel":"Cupón"})
        elif desc.startswith("NAVE DEVOLUCI") and valor<0 and op in cobros:
            detalles.append({"Fecha":date(fecha),"FechaPago":date(fecha),"Liquidacion":op,"Lote":"","Cupon":"","Terminal":"NAVE","Cantidad":1,"Importe":str(valor),"Nivel":"Cupón"})
    result["Banco"]="Galicia";result["Comercio"]=cuenta.group(1) if cuenta else "";result["Tarjeta"]="NAVE"
    total=sum((Decimal(d["Importe"]) for d in detalles),Decimal(0))
    # El extracto no informa el bruto vendido: se toma el acreditado por NAVE con tarjeta como total del documento.
    result["Bruto"]=str(total);result["Neto"]=str(total)
    result["Avisos"].append("Extracto Galicia: los importes son los acreditados por NAVE (netos de comisiones e impuestos), no el bruto de la venta. Cada fila es un cobro individual sin número de cupón; se concilia por fecha e importe. Se omiten transferencias, débitos y devoluciones de otros medios.")
def read_pdf(data, name):
    reader=PdfReader(io.BytesIO(data))
    # Los extractos de cuenta corriente Galicia traen un renglón por cobro y superan las 150 páginas de una liquidación.
    es_galicia=bool(reader.pages) and bool(re.search(r"\b007\d{19}\b",reader.pages[0].extract_text() or ""))
    if len(reader.pages)>(400 if es_galicia else 150): raise ValueError("El documento supera las %d páginas."%(400 if es_galicia else 150))
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
    elif re.search(r"\b007\d{19}\b",first) and re.search(r"Resumen de\s+Cuenta Corriente en Pesos",first):
        if len(set(re.findall(r"\b007\d{19}\b",text)))>1: raise ValueError("El PDF contiene más de una cuenta bancaria. Descargá un archivo por cuenta.")
        galicia_nave(pages,result)
    elif "BANCO HIPOTECARIO" in first.upper() and "CUENTA CORRIENTE EN PESOS" in first.upper():
        raise ValueError("El documento es un extracto de cuenta corriente de Hipotecario, no una liquidación de tarjetas. No se importó. La lectura de este PDF bancario todavía no está habilitada en Conciliación bancaria.")
    elif "RESUMEN DE CUENTA" in first.upper() and re.search(r"CUENTA CORRIENTE\s*-?\s*PESOS",first,re.I):
        raise ValueError("El documento es un extracto de cuenta corriente, no una liquidación de tarjetas. No se importó. La lectura de este PDF bancario todavía no está habilitada en Conciliación bancaria.")
    else: raise ValueError("Formato no reconocido. Esta etapa admite los resúmenes mensuales Nación, Patagonia e Hipotecario y el extracto Galicia con cobros NAVE.")
    if not details: raise ValueError("No se pudo extraer detalle conciliable; no se importó el documento.")
    total=sum((Decimal(d["Importe"]) for d in details),Decimal(0))
    result["TotalDetalle"]=str(total)
    result["DetalleCompleto"]=(total==Decimal(result["Bruto"]))
    if not result["DetalleCompleto"]: result["Avisos"].append("El detalle extraído no coincide con el bruto del resumen. Revisar antes de conciliar.")
    result["LiquidacionesLt"]=liquidaciones_lt(text,result["Banco"])
    if not result["LiquidacionesLt"] and monthly_bank:
        result["LiquidacionesLt"]=resumen_lt(text,result)
    return result
if __name__=="__main__":
    try:
        out=read_pdf(sys.stdin.buffer.read(),sys.argv[1] if len(sys.argv)>1 else "")
        sys.stdout.buffer.write(json.dumps(out,ensure_ascii=False).encode("utf-8"))
    except Exception as e:
        sys.stderr.write(str(e));sys.exit(1)
