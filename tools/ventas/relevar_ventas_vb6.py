"""Inventario estático de fuentes y llamadas de configuración de un proyecto VB6.

Lee fuentes; no ejecuta VB6, no se conecta a SQL y no copia líneas de código ni
valores de conexión. Las llamadas detectadas requieren revisión funcional.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
from pathlib import Path


SOURCE_RE = re.compile(r"^(Form|Module|Class|UserControl)=(.+)$", re.I)
ROUTINE_RE = re.compile(
    r"^\s*(?:(?:Public|Private|Friend|Static)\s+)?"
    r"(?:Sub|Function|Property\s+(?:Get|Let|Set))\s+(\w+)", re.I
)
CFG_RE = re.compile(r"\b(Cfg2?|sp_Cfg)\$?\s*\(", re.I)


def without_comments(line: str) -> str:
    """Respeta comillas duplicadas y apóstrofes dentro de literales VB6."""
    if re.match(r"^\s*Rem(?:\s|$)", line, re.I):
        return ""
    quoted = False
    i = 0
    while i < len(line):
        if line[i] == '"':
            if quoted and i + 1 < len(line) and line[i + 1] == '"':
                i += 2
                continue
            quoted = not quoted
        elif line[i] == "'" and not quoted:
            return line[:i]
        i += 1
    return line


def statements(text: str):
    pending = ""
    first_line = 0
    for number, raw in enumerate(text.splitlines(), 1):
        line = without_comments(raw).strip()
        if not pending:
            first_line = number
        continuing = line.endswith(" _")
        pending += " " + (line[:-2] if continuing else line)
        if not continuing:
            yield first_line, pending.strip()
            pending = ""
    if pending:
        yield first_line, pending.strip()


def string_mask(statement: str) -> str:
    # Conserva posiciones y oculta literales para no detectar Cfg dentro de texto.
    return re.sub(r'"(?:[^"\n]|"")*"', lambda m: " " * len(m[0]), statement)


def first_argument(statement: str, start: int) -> str:
    quoted = False
    depth = 0
    i = start
    while i < len(statement):
        char = statement[i]
        if char == '"':
            if quoted and i + 1 < len(statement) and statement[i + 1] == '"':
                i += 2
                continue
            quoted = not quoted
        elif not quoted:
            if char == "(":
                depth += 1
            elif char == ")":
                if depth == 0:
                    break
                depth -= 1
            elif char == "," and depth == 0:
                break
        i += 1
    return statement[start:i].strip()


def config_calls(text: str, source: str) -> list[dict]:
    calls = []
    routine = "(declaraciones)"
    for line, statement in statements(text):
        declaration = ROUTINE_RE.match(statement)
        if declaration:
            routine = declaration[1]
            continue
        if re.match(r"^End\s+(Sub|Function|Property)\b", statement, re.I):
            routine = "(declaraciones)"
        for match in CFG_RE.finditer(string_mask(statement)):
            arg = first_argument(statement, match.end())
            literal = re.match(r'^"((?:[^"]|"")*)"', arg)
            # No exportar expresiones arbitrarias ni valores del resto de la línea.
            key = literal[1].replace('""', '"') if literal else "(expresión sin prefijo literal)"
            dynamic = not literal or bool(arg[literal.end():].strip())
            calls.append({
                "clave_o_prefijo": key,
                "lector": match[1],
                "archivo": source,
                "linea": line,
                "rutina": routine,
                "dinamica": "SI" if dynamic else "NO",
            })
    return calls


def inventory(project: Path) -> tuple[dict, list[dict]]:
    project = project.resolve(strict=True)
    files = []
    calls = []
    missing = []
    for raw in project.read_text(encoding="cp1252").splitlines():
        match = SOURCE_RE.match(raw)
        if not match:
            continue
        reference = match[2].split(";")[-1].strip()
        source = project.parent / reference
        if not source.is_file():
            missing.append(reference)
            continue
        data = source.read_bytes()
        text = data.decode("cp1252")
        found = config_calls(text, reference)
        calls.extend(found)
        files.append({
            "tipo": match[1], "archivo": reference,
            "bytes": len(data), "lineas": len(text.splitlines()),
            "sha256": hashlib.sha256(data).hexdigest(),
            "llamadas_configuracion": len(found),
        })
    summary = {
        "proyecto": project.name,
        "alcance": "Fuentes referenciadas por VBP; no incluye implementación de DLL/OCX binarios, SQL instalado ni análisis de alcanzabilidad.",
        "lectores": ["Cfg", "Cfg$", "Cfg2", "Cfg2$", "sp_Cfg", "sp_Cfg$"],
        "archivos": len(files), "lineas": sum(f["lineas"] for f in files),
        "llamadas_configuracion": len(calls),
        "claves_o_prefijos_literales_distintos": len({
            c["clave_o_prefijo"].upper() for c in calls
            if c["clave_o_prefijo"] != "(expresión sin prefijo literal)"
        }),
        "llamadas_con_clave_dinamica": sum(c["dinamica"] == "SI" for c in calls),
        "referencias_faltantes": missing, "fuentes": files,
    }
    return summary, calls


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("project", type=Path)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[2] / "docs/modulos/ventas-carga-comprobantes")
    args = parser.parse_args()
    summary, calls = inventory(args.project)
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "inventario-fuentes.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    with (args.output / "configuracion-vb6.csv").open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=["clave_o_prefijo", "lector", "archivo", "linea", "rutina", "dinamica"], lineterminator="\n")
        writer.writeheader()
        writer.writerows(sorted(calls, key=lambda c: (c["clave_o_prefijo"].upper(), c["archivo"], c["linea"])))
    print(json.dumps({k: v for k, v in summary.items() if k != "fuentes"}, ensure_ascii=False, indent=2))
    return 1 if summary["referencias_faltantes"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
