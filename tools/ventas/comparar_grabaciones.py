"""Compara capturas de CargaComprobantesComparacionTests sin conectarse a SQL.

Comparación exacta de campos y multiplicidad de filas; solo ignora columnas
indicadas expresamente. No certifica equivalencia funcional entre programas.
"""
from __future__ import annotations

import argparse
from collections import Counter
from decimal import Decimal
import json
from pathlib import Path


SECCIONES = {
    "Cabecera", "Articulos", "Stock", "Tareas", "Conceptos", "Electronica",
    "Asientos", "AplicacionesEmitidas", "AplicacionesRecibidas",
    "OrigenCabecera", "OrigenArticulos",
}


def sin_claves_duplicadas(pares):
    resultado = {}
    for clave, valor in pares:
        if clave in resultado:
            raise ValueError(f"Clave JSON duplicada: {clave}")
        resultado[clave] = valor
    return resultado


def leer(ruta: Path) -> dict:
    return json.loads(ruta.read_text(encoding="utf-8-sig"), parse_float=Decimal,
                      object_pairs_hook=sin_claves_duplicadas,
                      parse_constant=lambda valor: (_ for _ in ()).throw(ValueError(f"Número inválido: {valor}")))


def validar(captura: dict):
    if not isinstance(captura, dict) or type(captura.get("Version")) is not int or captura["Version"] != 1:
        raise ValueError("Se requiere una captura de grabación versión 1.")
    secciones = captura.get("Secciones")
    if not isinstance(secciones, dict) or not SECCIONES <= secciones.keys():
        raise ValueError("La captura está incompleta: faltan secciones obligatorias.")
    for nombre, filas in secciones.items():
        if not isinstance(filas, list) or any(not isinstance(fila, dict) for fila in filas):
            raise ValueError(f"Sección inválida: {nombre}")
        if any(isinstance(valor, (dict, list)) for fila in filas for valor in fila.values()):
            raise ValueError(f"La sección {nombre} requiere valores escalares por campo.")
    if len(secciones["Cabecera"]) != 1 or not secciones["Asientos"]:
        raise ValueError("Se requiere una cabecera y al menos un renglón contable.")


def canonico(valor):
    # Diferencia NULL, cadena vacía, booleanos y números; conserva espacios de PK.
    if valor is None:
        return ("null",)
    if isinstance(valor, bool):
        return ("bool", valor)
    if isinstance(valor, (int, Decimal)):
        numero = Decimal(valor)
        if not numero.is_finite():
            raise ValueError("La captura contiene un número no finito.")
        return ("numero", numero)
    if isinstance(valor, str):
        return ("texto", valor)
    raise ValueError(f"Tipo no admitido en captura: {type(valor).__name__}")


def comparar(esperado: dict, obtenido: dict, ignorar=()) -> dict:
    validar(esperado)
    validar(obtenido)
    exclusiones = {}
    for expresion in ignorar:
        seccion, separador, campo = expresion.partition(".")
        if not separador or not campo or seccion not in esperado["Secciones"] or seccion not in obtenido["Secciones"]:
            raise ValueError(f"Exclusión inválida: {expresion}; usar Seccion.Campo exacto.")
        if not any(campo in fila for captura in (esperado, obtenido) for fila in captura["Secciones"][seccion]):
            raise ValueError(f"La columna excluida no existe: {expresion}")
        exclusiones.setdefault(seccion, set()).add(campo)
    diferencias = []
    for seccion in sorted(esperado["Secciones"].keys() | obtenido["Secciones"].keys()):
        if seccion not in esperado["Secciones"] or seccion not in obtenido["Secciones"]:
            diferencias.append({"Seccion": seccion, "Motivo": "Sección presente en una sola captura"})
            continue
        def indexar(filas):
            conteos = Counter()
            originales = {}
            for fila in filas:
                visible = {k: v for k, v in fila.items() if k not in exclusiones.get(seccion, ())}
                clave = tuple((campo, canonico(valor)) for campo, valor in sorted(visible.items()))
                conteos[clave] += 1
                originales[clave] = visible
            return conteos, originales
        a, originales_a = indexar(esperado["Secciones"][seccion])
        b, originales_b = indexar(obtenido["Secciones"][seccion])
        if a != b:
            campos = []
            filas_a = esperado["Secciones"][seccion]
            filas_b = obtenido["Secciones"][seccion]
            pares = []
            if seccion == "Cabecera":
                pares = [("Cabecera", filas_a[0], filas_b[0])]
            elif all("SECUENCIA" in fila for fila in filas_a + filas_b) and "SECUENCIA" not in exclusiones.get(seccion, ()):
                por_a = {canonico(fila["SECUENCIA"]): fila for fila in filas_a}
                por_b = {canonico(fila["SECUENCIA"]): fila for fila in filas_b}
                if len(por_a) == len(filas_a) and len(por_b) == len(filas_b):
                    pares = [(str(por_a[k]["SECUENCIA"]), por_a[k], por_b[k]) for k in por_a.keys() & por_b.keys()]
            for renglon, fila_a, fila_b in pares:
                for campo in sorted((fila_a.keys() | fila_b.keys()) - exclusiones.get(seccion, set())):
                    if campo not in fila_a or campo not in fila_b or canonico(fila_a[campo]) != canonico(fila_b[campo]):
                        diferencia = {"Renglon": renglon, "Campo": campo,
                            "PresenteEsperado": campo in fila_a, "PresenteObtenido": campo in fila_b,
                            "Esperado": fila_a.get(campo), "Obtenido": fila_b.get(campo)}
                        va, vb = fila_a.get(campo), fila_b.get(campo)
                        if type(va) in (int, Decimal) and type(vb) in (int, Decimal):
                            diferencia["Delta"] = Decimal(vb) - Decimal(va)
                        campos.append(diferencia)
            diferencias.append({"Seccion": seccion,
                "FilasEsperadas": sum(a.values()), "FilasObtenidas": sum(b.values()),
                "CamposDiferentes": campos,
                "SoloEsperado": [{"Veces": veces, "Campos": originales_a[clave]} for clave, veces in (a - b).items()],
                "SoloObtenido": [{"Veces": veces, "Campos": originales_b[clave]} for clave, veces in (b - a).items()]})
    return {"Version": 1, "Coincide": not diferencias, "ColumnasIgnoradas": sorted(set(ignorar)),
        "Advertencia": "Compara registros capturados; no certifica por sí solo equivalencia VB6/AlfaCore. Metadatos y configuración se muestran como contexto.",
        "ContextoEsperado": {k: v for k, v in esperado.items() if k != "Secciones"},
        "ContextoObtenido": {k: v for k, v in obtenido.items() if k != "Secciones"},
        "Diferencias": diferencias}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("esperado", type=Path)
    parser.add_argument("obtenido", type=Path)
    parser.add_argument("--ignorar", action="append", default=[], metavar="Seccion.Campo")
    parser.add_argument("--salida", type=Path, required=True)
    args = parser.parse_args()
    destinos = [args.esperado.resolve(), args.obtenido.resolve()]
    if args.salida.resolve() in destinos:
        parser.error("La salida debe ser distinta de las capturas de entrada.")
    try:
        resultado = comparar(leer(args.esperado), leer(args.obtenido), args.ignorar)
        args.salida.parent.mkdir(parents=True, exist_ok=True)
        args.salida.write_text(json.dumps(resultado, ensure_ascii=False, indent=2,
                                         default=lambda x: str(x) if isinstance(x, Decimal) else x), encoding="utf-8")
    except (OSError, ValueError, TypeError) as exc:
        parser.exit(2, f"No se pudo comparar: {exc}\n")
    print(f"{'COINCIDE' if resultado['Coincide'] else 'DIFIERE'}: {len(resultado['Diferencias'])} secciones con diferencias. Informe: {args.salida}")
    return 0 if resultado["Coincide"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
