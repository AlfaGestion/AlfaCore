#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
Descarga comprobantes emitidos o recibidos desde ARCA - Mis Comprobantes.

Uso:
    python arca_comprobantes.py CUIT_LOGIN CLAVE CUIT_REPRESENTADO PERIODO TIPO CARPETA [VISIBLE]

Ejemplo:
    python arca_comprobantes.py 20111111112 "CLAVE" 30111111118 202607 RECIBIDOS "C:\\ARCA\\Descargas" SI

Parametros:
    CUIT_LOGIN          CUIT/CUIL usado para iniciar sesion, con o sin guiones.
    CLAVE               Clave fiscal.
    CUIT_REPRESENTADO   CUIT de la persona/empresa que se seleccionara en Mis Comprobantes.
    PERIODO             AAAAMM, por ejemplo 202607.
    TIPO                RECIBIDOS o EMITIDOS.
    CARPETA             Carpeta donde se guardara el Excel (formato .xls, Excel 97-2003,
                        hoja "Sheet1" - requisito del importador de VB6).
    VISIBLE             Opcional. SI por defecto. Acepta SI/S/1/TRUE o NO/N/0/FALSE.

Codigos de salida:
    0  Descarga correcta.
    1  Parametros invalidos.
    2  Error de inicio de sesion.
    3  CUIT representado no encontrado.
    4  No se pudo abrir Mis Comprobantes.
    5  Error al realizar la consulta.
    6  Error al leer la tabla o generar el Excel.
    9  Error inesperado.

Aviso:
    ARCA puede modificar textos, controles o estructura HTML. Esta version usa varios
    selectores alternativos para tolerar cambios menores, pero puede requerir ajustes.
"""

from __future__ import annotations

import os

# Tiene que ir ANTES de importar playwright: hace que use el Chromium que
# esta instalado adentro de la carpeta portable python\ del proyecto, en vez
# de buscarlo en %LOCALAPPDATA%\ms-playwright (que no viaja si se copia la
# carpeta python\ a otra PC, y que en esta PC esta vacia a proposito).
os.environ.setdefault("PLAYWRIGHT_BROWSERS_PATH", "0")

import calendar
import csv
import re
import sys
import traceback
import zipfile
from datetime import datetime
from pathlib import Path

import win32com.client as win32
from playwright.sync_api import (
    Browser,
    BrowserContext,
    Error as PlaywrightError,
    Page,
    TimeoutError as PlaywrightTimeoutError,
    sync_playwright,
)

VERSION = "1.1.00"
URL_ARCA = "https://www.arca.gob.ar/landing/default.asp"
TIMEOUT_MS = 30_000
NAVIGATION_TIMEOUT_MS = 60_000


class BotError(Exception):
    def __init__(self, message: str, exit_code: int) -> None:
        super().__init__(message)
        self.exit_code = exit_code


def normalizar_cuit(valor: str) -> str:
    return re.sub(r"\D", "", valor or "")


def parse_visible(valor: str | None) -> bool:
    if valor is None:
        return True
    normalizado = valor.strip().upper()
    if normalizado in {"SI", "S", "1", "TRUE", "YES", "Y"}:
        return True
    if normalizado in {"NO", "N", "0", "FALSE"}:
        return False
    raise BotError("El parametro VISIBLE debe ser SI o NO.", 1)


def fechas_periodo(periodo: str) -> tuple[str, str]:
    if not re.fullmatch(r"\d{6}", periodo):
        raise BotError("El periodo debe tener formato AAAAMM, por ejemplo 202607.", 1)
    anio = int(periodo[:4])
    mes = int(periodo[4:])
    if mes < 1 or mes > 12:
        raise BotError("El mes del periodo debe estar entre 01 y 12.", 1)
    ultimo_dia = calendar.monthrange(anio, mes)[1]
    return f"01/{mes:02d}/{anio}", f"{ultimo_dia:02d}/{mes:02d}/{anio}"


class Logger:
    def __init__(self, carpeta: Path) -> None:
        self.carpeta = carpeta
        self.carpeta.mkdir(parents=True, exist_ok=True)
        self.path = carpeta / "arca_comprobantes.log"

    def write(self, mensaje: str) -> None:
        linea = f"[{datetime.now():%Y-%m-%d %H:%M:%S}] {mensaje}"
        try:
            print(linea, flush=True)
        except UnicodeError:
            # Con la salida redirigida (cp1252) un mensaje con caracteres especiales no debe cortar el proceso.
            print(linea.encode("ascii", "replace").decode("ascii"), flush=True)
        with self.path.open("a", encoding="utf-8") as fh:
            fh.write(linea + "\n")


def click_first(page: Page, selectors: list[str], descripcion: str, timeout: int = TIMEOUT_MS) -> None:
    ultimo_error: Exception | None = None
    for selector in selectors:
        try:
            locator = page.locator(selector).first
            locator.wait_for(state="visible", timeout=min(timeout, 8_000))
            locator.click(timeout=min(timeout, 8_000))
            return
        except Exception as exc:
            ultimo_error = exc
    raise PlaywrightTimeoutError(f"No se encontro o no se pudo pulsar: {descripcion}. Ultimo error: {ultimo_error}")


def fill_first(page: Page, selectors: list[str], valor: str, descripcion: str) -> None:
    ultimo_error: Exception | None = None
    for selector in selectors:
        try:
            locator = page.locator(selector).first
            locator.wait_for(state="visible", timeout=8_000)
            locator.fill(valor, timeout=8_000)
            return
        except Exception as exc:
            ultimo_error = exc
    raise PlaywrightTimeoutError(f"No se encontro el campo {descripcion}. Ultimo error: {ultimo_error}")


def esperar_estable(page: Page) -> None:
    try:
        page.wait_for_load_state("domcontentloaded", timeout=NAVIGATION_TIMEOUT_MS)
    except PlaywrightTimeoutError:
        pass
    try:
        page.wait_for_load_state("networkidle", timeout=8_000)
    except PlaywrightTimeoutError:
        pass


def iniciar_sesion(page: Page, context: BrowserContext, cuit_login: str, clave: str, log: Logger, carpeta: Path) -> Page:
    log.write("Abriendo portal de ARCA.")
    page.goto(URL_ARCA, wait_until="domcontentloaded", timeout=NAVIGATION_TIMEOUT_MS)
    log.write(f"URL tras abrir el portal: {page.url}")

    # El login real de ARCA suele estar en otro subdominio (auth.afip.gob.ar
    # o similar); a veces esa navegacion abre en una pestania nueva, como
    # paso con "Mis comprobantes" en el script de Casa Alberto. Se detecta
    # con expect_page() y, si no abre nada nuevo, se sigue en la misma pagina.
    pagina_login = page
    try:
        with context.expect_page(timeout=5_000) as page_info:
            click_first(
                page,
                [
                    "text=Iniciar sesión",
                    "text=Iniciar sesion",
                    "a:has-text('Iniciar sesión')",
                    "button:has-text('Iniciar sesión')",
                ],
                "Iniciar sesion",
            )
        pagina_login = page_info.value
    except PlaywrightTimeoutError:
        pass
    esperar_estable(pagina_login)
    page = pagina_login
    log.write(f"URL tras click en 'Iniciar sesion': {page.url}")

    fill_first(
        page,
        [
            "input[aria-label*='CUIT']",
            "input[placeholder*='CUIT']",
            "input[type='number']",
            "input[type='text']",
        ],
        cuit_login,
        "CUIT/CUIL",
    )
    click_first(page, ["button:has-text('Siguiente')", "text=Siguiente"], "Siguiente")
    esperar_estable(page)
    log.write(f"URL tras cargar CUIT y click en 'Siguiente': {page.url}")

    fill_first(
        page,
        [
            "input[aria-label*='CLAVE' i]",
            "input[placeholder*='CLAVE' i]",
            "input[type='password']",
        ],
        clave,
        "clave fiscal",
    )

    click_first(page, ["button:has-text('Ingresar')", "text=Ingresar"], "Ingresar")
    esperar_estable(page)
    log.write(f"URL tras cargar clave y click en 'Ingresar': {page.url}")

    # El click puede no alcanzar a disparar el submit real del formulario
    # (paso lo mismo con el login de Casa Alberto). Si seguimos viendo el
    # campo de clave, se prueba Enter dentro del campo como metodo
    # alternativo de envio, que es lo estandar para este tipo de formularios.
    if page.locator("input[type='password']").count() > 0:
        log.write("Sigue visible el campo de clave tras el click; se prueba Enter como alternativa.")
        try:
            campo_clave = page.locator("input[type='password']").first
            campo_clave.press("Enter")
            esperar_estable(page)
            log.write(f"URL tras presionar Enter en el campo de clave: {page.url}")
        except Exception as exc:
            log.write(f"No se pudo enviar el formulario con Enter: {exc}")

    # Captura siempre (no solo en error) justo despues del click en Ingresar,
    # para ver el estado real de la pantalla en ese momento exacto sin
    # depender de que el fallo posterior (mas adelante en el flujo) la
    # capture demasiado tarde.
    try:
        ruta_post_login = carpeta / f"post_login_{datetime.now():%Y%m%d_%H%M%S}.png"
        page.screenshot(path=str(ruta_post_login), full_page=True)
        log.write(f"Captura post-login guardada: {ruta_post_login}")
    except Exception as exc:
        log.write(f"No se pudo guardar la captura post-login: {exc}")

    if page.locator("text=/clave.*incorrect|credenciales.*incorrect|datos.*incorrect/i").count() > 0:
        raise BotError("ARCA rechazo el CUIT o la clave fiscal.", 2)

    log.write("Inicio de sesion realizado.")
    return page


def abrir_mis_comprobantes(page: Page, context: BrowserContext, log: Logger) -> Page:
    log.write("Buscando el servicio Mis Comprobantes.")

    fill_first(
        page,
        [
            "input[placeholder*='Buscar' i]",
            "input[type='search']",
            "input.form-control",
            "input[type='text']",
        ],
        "Mis comprobantes",
        "buscador de servicios",
    )

    page.wait_for_timeout(1_000)

    selectores_mis_comprobantes = [
        "text=/^Mis comprobantes$/i",
        "a:has-text('Mis comprobantes')",
        "button:has-text('Mis comprobantes')",
    ]

    # No se confia solo en context.expect_page(): si ARCA tarda mas de su
    # ventana de espera (o directamente navega la misma pestania en vez de
    # abrir una nueva), el fallback se quedaba trabajando sobre la pagina
    # vieja sin darse cuenta (paso en una corrida real). En cambio, se
    # registran las pestanias abiertas ANTES del click y despues se revisan
    # TODAS las que haya, sea cual sea la que efectivamente cambio.
    paginas_antes = set(context.pages)

    try:
        click_first(page, selectores_mis_comprobantes, "Mis Comprobantes")
    except Exception as exc:
        raise BotError(f"No se pudo abrir Mis Comprobantes: {exc}", 4) from exc

    destino = page
    for intento in range(5):
        page.wait_for_timeout(2_000)
        for candidata in context.pages:
            if candidata not in paginas_antes:
                destino = candidata
        esperar_estable(destino)
        if "mcmp" in destino.url.lower() or "fes.afip" in destino.url.lower():
            break
        log.write(f"Intento {intento + 1}: todavia no se ve Mis Comprobantes (URL actual: {destino.url}).")

    if "mcmp" not in destino.url.lower() and "fes.afip" not in destino.url.lower():
        raise BotError(
            f"Se hizo click en 'Mis Comprobantes' pero no se llego a esa pantalla (URL actual: {destino.url}).",
            4,
        )

    log.write(f"Servicio Mis Comprobantes abierto ({destino.url}).")
    return destino


def seleccionar_representado(page: Page, cuit_representado: str, log: Logger) -> None:
    cuit = normalizar_cuit(cuit_representado)
    cuit_formateado = f"{cuit[:2]}-{cuit[2:10]}-{cuit[10:]}"
    log.write(f"Seleccionando CUIT representado {cuit} ({cuit_formateado}).")

    # La pantalla "Elegi una persona para ingresar" muestra tarjetas con
    # nombre + CUIT con guiones. No se puede saber de antemano si el texto
    # del CUIT cae en un <span> suelto o en la tarjeta clickeable completa,
    # asi que se prueban TODOS los elementos que lo contengan, del mas
    # anidado (mas especifico) al mas general, y despues de cada intento se
    # verifica que realmente se salio de esta pantalla antes de dar exito
    # (el click puede "no tirar error" sin haber navegado a ningun lado,
    # como paso con el boton Ingresar del login).
    for texto in (cuit_formateado, cuit):
        candidatos = page.locator("a, button, div, li").filter(has_text=texto)
        cantidad = candidatos.count()
        log.write(f"'{texto}': {cantidad} elementos candidatos.")
        for i in range(cantidad - 1, -1, -1):
            try:
                elegido = candidatos.nth(i)
                elegido.scroll_into_view_if_needed(timeout=3_000)
                elegido.click(timeout=4_000)
                esperar_estable(page)
                if page.locator("text=/Elegi una persona/i").count() == 0:
                    log.write("CUIT representado seleccionado.")
                    return
            except Exception:
                continue

    # Si no aparece selector, puede haber ingresado directamente como titular.
    cuerpo = normalizar_cuit(page.locator("body").inner_text(timeout=8_000))
    if cuit in cuerpo and page.locator("text=/Emitidos|Recibidos/i").count() > 0:
        log.write("El sistema ingreso directamente con el CUIT solicitado.")
        return

    raise BotError(f"No se encontro el CUIT representado {cuit}.", 3)


def abrir_tipo(page: Page, tipo: str, log: Logger) -> None:
    etiqueta = "Recibidos" if tipo == "RECIBIDOS" else "Emitidos"
    log.write(f"Ingresando a comprobantes {etiqueta.lower()}.")
    try:
        click_first(
            page,
            [
                f"text=/^{etiqueta}$/i",
                f"a:has-text('{etiqueta}')",
                f"div:has-text('{etiqueta}')",
            ],
            etiqueta,
        )
        esperar_estable(page)
    except Exception as exc:
        raise BotError(f"No se pudo abrir la seccion {etiqueta}: {exc}", 5) from exc


def cargar_periodo_y_buscar(page: Page, desde: str, hasta: str, log: Logger) -> None:
    log.write(f"Cargando periodo {desde} a {hasta}.")

    # El campo "Fecha del Comprobante" (obligatorio) abre un selector con dos
    # mini-calendarios y un rango como texto "dd/mm/aaaa - dd/mm/aaaa". El
    # formulario tiene OTROS campos (Numero desde, CUIT del Emisor, Codigo
    # de Autorizacion, etc.) - una version anterior de este script buscaba
    # inputs por nombre/id conteniendo "desde"/"hasta" en TODA la pagina, y
    # eso hacia que agarrara "Numero desde" por error (coincidencia de
    # nombre), rompiendo el filtro. Ahora se abre el selector puntual por su
    # etiqueta, y adentro se identifican los dos inputs de fecha por su
    # VALOR actual (unicos con forma dd/mm/aaaa), no por nombre/atributo.
    try:
        # Primero se intenta clickear la caja que muestra el rango actual
        # como texto ("dd/mm/aaaa - dd/mm/aaaa"), que es el control real; si
        # no esta (campo vacio/sin seleccion previa), se cae a la etiqueta.
        campo_fecha = page.locator(r"text=/\d{2}\/\d{2}\/\d{4}\s*-\s*\d{2}\/\d{2}\/\d{4}/").first
        if campo_fecha.count() == 0:
            campo_fecha = page.locator("text=/Fecha del Comprobante/i").first
        campo_fecha.click(timeout=8_000)
    except Exception as exc:
        raise BotError(f"No se pudo abrir el selector de 'Fecha del Comprobante': {exc}", 5) from exc

    page.wait_for_timeout(500)

    inputs_fecha = []
    try:
        visibles = page.locator("input:visible")
        for i in range(visibles.count()):
            loc = visibles.nth(i)
            try:
                valor = (loc.input_value() or "").strip()
                if re.fullmatch(r"\d{2}/\d{2}/\d{4}", valor):
                    inputs_fecha.append(loc)
            except Exception:
                continue
    except Exception as exc:
        raise BotError(f"No se pudo inspeccionar el selector de fecha: {exc}", 5) from exc

    if len(inputs_fecha) < 2:
        raise BotError(
            f"No se encontraron los dos campos de fecha del selector "
            f"(se encontraron {len(inputs_fecha)} con formato de fecha).",
            5,
        )

    try:
        inputs_fecha[0].fill(desde)
        inputs_fecha[1].fill(hasta)
        click_first(page, ["button:has-text('Aplicar')", "text=/^Aplicar$/i"], "Aplicar periodo")
        esperar_estable(page)
    except Exception as exc:
        raise BotError(f"No se pudo cargar el periodo: {exc}", 5) from exc

    try:
        click_first(page, ["button:has-text('BUSCAR')", "button:has-text('Buscar')", "text=/^BUSCAR$/i"], "Buscar")
        esperar_estable(page)
    except Exception as exc:
        raise BotError(f"No se pudo ejecutar la consulta: {exc}", 5) from exc

    # La tabla de resultados se carga con una llamada AJAX aparte, que puede
    # tardar mas que la espera generica de arriba. Si se sigue de largo
    # antes de que termine, la exportacion a Excel sale practicamente vacia
    # (paso en una corrida real: solo quedo el titulo y el encabezado
    # "Fecha", sin ninguna fila). Se espera activamente a que aparezca
    # alguna fila de datos real, o el mensaje de "sin resultados".
    mensaje_sin_resultados = "text=/sin resultados|no se encontraron|no existen comprobantes/i"
    filas_con_datos = 0
    for _ in range(15):
        if page.locator(mensaje_sin_resultados).count() > 0:
            break
        filas_con_datos = page.locator("table tbody tr").count()
        if filas_con_datos > 0:
            break
        page.wait_for_timeout(1_000)

    if page.locator(mensaje_sin_resultados).count() > 0:
        raise BotError("La consulta no devolvio comprobantes para el periodo solicitado.", 5)

    if filas_con_datos == 0:
        raise BotError(
            "La consulta no mostro filas de resultados dentro del tiempo de espera "
            "(ARCA puede estar tardando mas de lo normal en cargar la tabla).",
            5,
        )

    log.write(f"Consulta realizada ({filas_con_datos} filas encontradas).")


def seleccionar_todas_columnas(page: Page, log: Logger) -> None:
    log.write("Intentando habilitar todas las columnas del resultado.")
    try:
        # Boton de visibilidad de columnas: normalmente esta entre CSV y el icono de impresion.
        botones = page.locator("button, a")
        boton_columnas = None
        for i in range(botones.count()):
            loc = botones.nth(i)
            try:
                titulo = " ".join(
                    filter(None, [loc.get_attribute("title"), loc.get_attribute("aria-label"), loc.inner_text()])
                ).lower()
                clase = (loc.get_attribute("class") or "").lower()
                if any(x in titulo for x in ["column", "columna", "visibilidad"]) or "columns" in clase:
                    boton_columnas = loc
                    break
            except Exception:
                continue

        if boton_columnas is None:
            # Selectores frecuentes de DataTables Buttons.
            for selector in ["button.buttons-colvis", "a.buttons-colvis", ".buttons-colvis"]:
                loc = page.locator(selector).first
                if loc.count() > 0:
                    boton_columnas = loc
                    break

        if boton_columnas is None:
            log.write("No se identifico el menu de columnas; se continuara con las columnas actuales.")
            return

        boton_columnas.click()
        page.wait_for_timeout(500)

        opciones = page.locator(".dt-button-collection button, .dt-button-collection a, div[role='menu'] button")
        for i in range(opciones.count()):
            opcion = opciones.nth(i)
            try:
                clase = (opcion.get_attribute("class") or "").lower()
                aria_pressed = (opcion.get_attribute("aria-pressed") or "").lower()
                # En DataTables, active suele indicar que la columna ya esta visible.
                if "active" not in clase and aria_pressed != "true":
                    opcion.click()
            except Exception:
                continue

        # Escape NO cierra este menu en la practica (confirmado con una
        # captura real: el menu de columnas seguia abierto tapando la tabla
        # varios pasos despues). La forma confiable de cerrarlo es clickear
        # el mismo boton que lo abrio - es un toggle, asi que el segundo
        # click lo cierra igual que lo haria una persona.
        try:
            boton_columnas.click(timeout=3_000)
        except Exception:
            page.keyboard.press("Escape")

        try:
            page.locator(".dt-button-background").wait_for(state="hidden", timeout=3_000)
        except PlaywrightTimeoutError:
            pass

        if page.locator(".dt-button-collection").count() > 0:
            log.write("Advertencia: el menu de columnas parece seguir abierto tras intentar cerrarlo; se fuerza el cierre.")
            # El toggle-click no siempre cierra el menu (confirmado en corridas
            # reales). Si sigue en el DOM, se lo elimina a la fuerza via JS -
            # mismo recurso que ya usa descargar_excel_oficial para este mismo
            # overlay - porque si queda tapando la tabla, los clicks
            # posteriores (ej. paginacion "siguiente") fallan en silencio por
            # quedar el elemento real cubierto.
            try:
                page.locator(".dt-button-background, .dt-button-collection").evaluate_all(
                    "els => els.forEach(el => el.remove())"
                )
            except Exception:
                pass

        log.write("Columnas visibles configuradas.")
    except Exception as exc:
        log.write(f"Advertencia al configurar columnas: {exc}")

    # Diagnostico: confirmar si la tabla sigue teniendo filas despues de
    # tocar el menu de columnas (para saber si el Excel vacio es por esto
    # o por el mecanismo de exportacion en si).
    try:
        filas_tras_columnas = page.locator("table tbody tr").count()
        log.write(f"Filas visibles tras configurar columnas: {filas_tras_columnas}.")
    except Exception:
        pass


def ampliar_filas_por_pagina(page: Page, log: Logger) -> None:
    """
    Busca un <select> de "filas por pagina" y elige la opcion mas grande
    disponible, para minimizar cuantas paginas hay que recorrer despues.
    Si no lo encuentra, no pasa nada: extraer_tabla_completa igual recorre
    pagina por pagina.
    """
    try:
        selects = page.locator("select")
        for i in range(selects.count()):
            sel = selects.nth(i)
            try:
                opciones = sel.locator("option")
                valores_numericos = []
                for j in range(opciones.count()):
                    valor = (opciones.nth(j).get_attribute("value") or "").strip()
                    if valor.isdigit():
                        valores_numericos.append(int(valor))
                if valores_numericos:
                    mejor = str(max(valores_numericos))
                    sel.select_option(mejor)
                    page.wait_for_timeout(1_000)
                    log.write(f"Filas por pagina configuradas a {mejor}.")
                    return
            except Exception:
                continue
    except Exception:
        pass


def extraer_datos_via_datatables_api(page: Page, log: Logger) -> tuple[list[str], list[list[str]]] | None:
    """
    La grilla de resultados es una DataTable de jQuery (se confirma por las
    clases vistas en pantalla: dt-button-collection, dataTables_wrapper,
    etc.). En vez de leer el DOM visual - que esta pagina divide en mas de
    un <table> para el scroll horizontal, complicando mucho identificar
    filas/columnas reales -, se lee directo de la API interna de la
    libreria: da los datos "crudos" sin importar como se muestren en
    pantalla, y permite pasar de pagina con su propio metodo (mas
    confiable que clickear el boton "siguiente").
    Devuelve None si la pagina no usa DataTables (para caer al scraping
    del DOM como respaldo).
    """
    try:
        hay_api = page.evaluate(
            "() => !!(window.jQuery && window.jQuery.fn && window.jQuery.fn.dataTable "
            "&& window.jQuery.fn.dataTable.tables({visible: true, api: true}).length > 0)"
        )
    except Exception as exc:
        log.write(f"No se pudo verificar si hay API de DataTables: {exc}")
        return None

    if not hay_api:
        log.write("No se detecto una DataTable en la pagina; se usara lectura del DOM.")
        return None

    try:
        info = page.evaluate(
            """
            () => {
                const tablas = jQuery.fn.dataTable.tables({ visible: true, api: true });
                let mejorIdx = 0, mejorFilas = -1;
                for (let i = 0; i < tablas.length; i++) {
                    const cantidad = tablas.eq(i).page.info().recordsTotal || 0;
                    if (cantidad > mejorFilas) { mejorFilas = cantidad; mejorIdx = i; }
                }
                const api = tablas.eq(mejorIdx);
                const headers = api.columns().header().toArray().map(el => (el.textContent || "").trim());
                return { indice: mejorIdx, headers: headers, total: api.page.info().recordsTotal };
            }
            """
        )
    except Exception as exc:
        log.write(f"No se pudo leer info inicial de la DataTable: {exc}")
        return None

    if not info or not info.get("headers"):
        return None

    indice = info["indice"]
    encabezados = info["headers"]
    log.write(f"API de DataTables detectada: {info.get('total', 0)} registros totales esperados.")

    datos: list[list[str]] = []
    for _ in range(200):  # limite de seguridad
        try:
            pagina_actual = page.evaluate(
                """
                (indice) => {
                    const api = jQuery.fn.dataTable.tables({visible: true, api: true}).eq(indice);
                    return api.rows({page: 'current'}).data().toArray().map(fila => {
                        const valores = Array.isArray(fila) ? fila : Object.values(fila);
                        return valores.map(v => (v === null || v === undefined) ? "" : String(v));
                    });
                }
                """,
                indice,
            )
        except Exception as exc:
            log.write(f"Error leyendo una pagina via API: {exc}")
            break

        if pagina_actual:
            datos.extend(pagina_actual)

        try:
            avanzo = page.evaluate(
                """
                (indice) => {
                    const api = jQuery.fn.dataTable.tables({visible: true, api: true}).eq(indice);
                    const info = api.page.info();
                    if (info.page >= info.pages - 1) return false;
                    api.page('next').draw('page');
                    return true;
                }
                """,
                indice,
            )
        except Exception:
            break

        if not avanzo:
            break
        page.wait_for_timeout(600)

    log.write(f"Leido via API de DataTables: {len(datos)} filas, {len(encabezados)} columnas.")
    return encabezados, datos


def extraer_tabla_completa(page: Page, log: Logger) -> tuple[list[str], list[list[str]]]:
    """
    Respaldo por si la pagina no resulta ser una DataTable de jQuery (ver
    extraer_datos_via_datatables_api, que es el metodo preferido). Lee la
    tabla de resultados directamente del DOM, recorriendo todas las
    paginas (el boton Excel de ARCA resulto no ser confiable: un overlay
    fantasma bloquea el click de forma consistente incluso con el filtro
    correcto, y probado a mano tambien). Misma tecnica que usan Georgalos,
    Yuyu y Neopel en este proyecto para armar el Excel con Python.
    """
    # Puede haber mas de una <table> en el DOM (ej. una version oculta para
    # imprimir/exportar, con columnas extra tipo "Filtro"/"Fecha de
    # Ejecucion"). Filtrar por ":visible" y, si aun asi hay varias, elegir
    # la que tenga mas filas (la real de resultados). Se loguea el detalle
    # de cada una para poder ver, si algo sale mal, cual se eligio y por que.
    tablas = page.locator("table:visible")
    cantidad_tablas = tablas.count()
    tabla = tablas.first
    mejor_cantidad = -1
    for i in range(cantidad_tablas):
        candidata = tablas.nth(i)
        try:
            filas_i = candidata.locator("tbody tr").count()
            cols_i = candidata.locator("thead th").count()
        except Exception:
            filas_i, cols_i = 0, 0
        log.write(f"  Tabla visible {i}: {filas_i} filas, {cols_i} columnas de encabezado.")
        if filas_i > mejor_cantidad:
            mejor_cantidad = filas_i
            tabla = candidata
    log.write(f"Tablas visibles detectadas: {cantidad_tablas}.")

    # DataTables envuelve la tabla real en un div.dataTables_wrapper que
    # tambien contiene SU paginacion como hermano (no adentro de la tabla).
    # Si hay dos tablas en la pagina, cada una puede tener su propia
    # paginacion; buscar "siguiente" en TODA la pagina (como se hacia antes)
    # podia terminar clickeando la paginacion de la tabla equivocada. Se
    # acota la busqueda al wrapper de la tabla elegida.
    wrapper = tabla.locator("xpath=ancestor::div[contains(@class,'dataTables_wrapper')]").first
    contenedor = wrapper if wrapper.count() > 0 else page

    encabezados_loc = tabla.locator("thead th")
    encabezados = [encabezados_loc.nth(i).inner_text().strip() for i in range(encabezados_loc.count())]

    datos: list[list[str]] = []
    for pagina in range(1, 201):  # limite de seguridad
        filas_dom = tabla.locator("tbody tr")
        cantidad = filas_dom.count()
        for i in range(cantidad):
            celdas = filas_dom.nth(i).locator("td")
            cantidad_celdas = celdas.count()
            if cantidad_celdas == 0:
                continue
            fila = [celdas.nth(j).inner_text().strip() for j in range(cantidad_celdas)]
            datos.append(fila)

        # El markup mas comun de DataTables+Bootstrap pone "paginate_button"/
        # "next"/"disabled" en el <li>, no en el <a> (que suele no tener
        # clase y mostrar solo el simbolo "»", sin aria-label). Por eso se
        # busca primero el <li>, y se cliquea su <a> interno; si no hay <li>
        # (otro skin de DataTables), se cae a buscar el <a> directamente.
        siguiente_li = contenedor.locator("li.paginate_button.next").first
        if siguiente_li.count() > 0:
            clase = (siguiente_li.get_attribute("class") or "").lower()
            if "disabled" in clase:
                break
            boton_click = siguiente_li.locator("a").first
        else:
            siguiente_a = contenedor.locator(
                "a.paginate_button.next, a[aria-label*='Next' i], a:has-text('Siguiente')"
            ).first
            if siguiente_a.count() == 0:
                break
            clase = (siguiente_a.get_attribute("class") or "").lower()
            aria_disabled = (siguiente_a.get_attribute("aria-disabled") or "").lower()
            if "disabled" in clase or aria_disabled == "true":
                break
            boton_click = siguiente_a

        try:
            boton_click.click(timeout=4_000)
            page.wait_for_timeout(1_200)
        except Exception:
            break
    else:
        log.write("Advertencia: se llego al limite de paginas de seguridad (200).")

    log.write(f"Se leyeron {len(datos)} filas en total ({len(encabezados)} columnas de encabezado).")
    return encabezados, datos


# 56 = xlExcel8 (.xls, formato Excel 97-2003). El importador de VB6 exige
# tanto este formato como que la hoja se llame "Sheet1" (cartel en pantalla:
# "El archivo debe ser en formato xls, excel 97-2003" / "la hoja excel debe
# llamarse Sheet1, mantener la estructura original").
XL_EXCEL8 = 56


class ExcelNoDisponible(Exception):
    """Excel no esta instalado/registrado (COM) en esta maquina, p. ej. un servidor sin Office."""


def abrir_excel():
    try:
        return win32.DispatchEx("Excel.Application")
    except Exception as exc:
        raise ExcelNoDisponible(str(exc)) from exc


def convertir_a_xls_sheet1(origen: Path, destino_xls: Path, log: Logger) -> Path:
    """
    Abre `origen` (cualquier formato que Excel pueda leer: .xlsx, .xls, o el
    Excel-que-en-realidad-es-HTML que suelen generar los botones "Excel" de
    grillas web) con Excel COM, renombra la primera hoja a "Sheet1" y guarda
    como .xls (Excel 97-2003) en `destino_xls`. Borra `origen` si quedo en una
    ruta distinta a la final.
    """
    excel = abrir_excel()
    excel.Visible = False
    excel.DisplayAlerts = False
    libro = None
    try:
        libro = excel.Workbooks.Open(str(origen.resolve()), UpdateLinks=0, ReadOnly=True)
        ws = libro.Worksheets(1)
        # En un Excel con configuracion regional en espanol la primera hoja
        # de un libro nuevo/descargado no siempre se llama "Sheet1" (puede
        # ser "Hoja1", o el nombre que haya puesto la pagina de origen).
        try:
            ws.Name = "Sheet1"
        except Exception:
            pass

        if destino_xls.exists():
            destino_xls.unlink()
        libro.SaveAs(str(destino_xls.resolve()), XL_EXCEL8)
    finally:
        if libro is not None:
            libro.Close(SaveChanges=False)
        excel.Quit()

    if origen.resolve() != destino_xls.resolve():
        try:
            origen.unlink()
        except Exception as exc:
            log.write(f"ADVERTENCIA: no se pudo borrar el archivo intermedio {origen}: {exc}")

    if not destino_xls.exists() or destino_xls.stat().st_size == 0:
        raise BotError("No se pudo guardar el Excel en formato .xls.", 6)

    return destino_xls


def descargar_excel_oficial(page: Page, carpeta: Path, tipo: str, cuit: str, periodo: str, log: Logger) -> Path | None:
    """
    Mis Comprobantes arma un Excel propio con DataTables Buttons. Ese archivo
    incluye todas las filas que DataTables tiene cargadas internamente, aunque
    el DOM muestre solo una pagina de 5/10/25 registros. Si la descarga oficial
    falla, el flujo vuelve al armado local como respaldo.
    """
    nombre_base = f"ARCA_{tipo}_{normalizar_cuit(cuit)}_{periodo}"
    temp = carpeta / f"{nombre_base}_descarga.xlsx"
    destino = carpeta / f"{nombre_base}.xls"

    try:
        page.keyboard.press("Escape")
        page.locator(".dt-button-background, .dt-button-collection").evaluate_all(
            "els => els.forEach(el => el.remove())"
        )
    except Exception:
        pass

    boton_excel = page.locator(
        "button.buttons-excel:visible, a.buttons-excel:visible, "
        "button:has-text('Excel'):visible, a:has-text('Excel'):visible"
    ).first

    try:
        boton_excel.wait_for(state="visible", timeout=8_000)
    except Exception as exc:
        log.write(f"No se encontro el boton Excel oficial; se usara respaldo local. Detalle: {exc}")
        return None

    try:
        if temp.exists():
            temp.unlink()

        with page.expect_download(timeout=30_000) as download_info:
            boton_excel.click(timeout=8_000, force=True)
        descarga = download_info.value
        descarga.save_as(str(temp))

        if not temp.exists() or temp.stat().st_size == 0:
            log.write("La descarga oficial de Excel no genero un archivo valido; se usara respaldo local.")
            return None

        log.write(f"Archivo descargado desde Excel oficial de ARCA: {temp}")
        try:
            destino = convertir_a_xls_sheet1(temp, destino, log)
            log.write(f"Convertido a .xls (Excel 97-2003): {destino}")
            return destino
        except ExcelNoDisponible as exc:
            # Sin Excel no se puede armar el .xls del importador VB6: AlfaCore lee el .xlsx oficial tal cual.
            final = carpeta / f"{nombre_base}.xlsx"
            if final.exists():
                final.unlink()
            temp.replace(final)
            if destino.exists():
                destino.unlink()
            log.write(f"Excel no esta disponible en este equipo ({exc}); se conserva el .xlsx oficial: {final}")
            return final
    except Exception as exc:
        log.write(f"No se pudo descargar el Excel oficial; se usara respaldo local. Detalle: {exc}")
        return None


def _mejor_delimitador(texto: str) -> str:
    """
    Prueba "," y ";" parseando las primeras lineas reales con cada uno, y
    elige el que produzca mas de una columna de forma consistente.
    No se puede confiar en contar caracteres sueltos (";" vs ","): los
    importes en formato argentino usan "," como separador decimal (ej.
    "1.234,56"), lo que infla el conteo de comas aunque el separador real de
    columnas sea ";" - eso rompia la deteccion antes (parseaba todo como una
    sola columna). csv.Sniffer() tiene el mismo problema con este formato de
    numeros, asi que tampoco se usa.
    """
    lineas_muestra = [linea for linea in texto.splitlines()[:15] if linea.strip()]

    mejor_delim = ","
    mejor_columnas = 1
    for candidato in (";", ","):
        conteos = []
        for linea in lineas_muestra:
            try:
                fila = next(csv.reader([linea], delimiter=candidato))
            except StopIteration:
                continue
            conteos.append(len(fila))
        if not conteos:
            continue
        # min() en vez de max(): exige que TODAS las lineas muestreadas
        # tengan al menos esa cantidad de columnas con este delimitador,
        # para no confundir una linea suelta con muchas comas decimales
        # con el separador de columnas real.
        columnas_consistentes = min(conteos)
        if columnas_consistentes > mejor_columnas:
            mejor_columnas = columnas_consistentes
            mejor_delim = candidato

    return mejor_delim


def _bytes_csv_real(ruta: Path) -> bytes:
    """
    El boton "CSV" de Mis Comprobantes en realidad descarga un .zip que
    contiene un unico archivo .csv adentro (confirmado en una descarga real:
    el archivo baja con extension .csv pero su contenido es un ZIP valido,
    con un miembro tipo "comprobantes_consulta_csv_emitidos_...csv"). Si lo
    descargado es un zip, se extrae ese miembro; si no, se asume que ya es
    el CSV en texto plano (por si ARCA cambia esto a futuro).
    """
    if zipfile.is_zipfile(ruta):
        with zipfile.ZipFile(ruta) as z:
            nombres = z.namelist()
            if not nombres:
                raise BotError("El .zip descargado (boton CSV) no contiene ningun archivo.", 6)
            return z.read(nombres[0])
    return ruta.read_bytes()


def _parsear_csv(ruta: Path) -> tuple[list[str], list[list[str]]]:
    """
    Lee un CSV (o el .zip que lo envuelve, ver _bytes_csv_real) detectando
    automaticamente el separador (algunos exports de ARCA usan ",", otros
    ";") y la codificacion. No se abre con Excel (Workbooks.Open) porque en
    un Excel con configuracion regional es-AR el separador de listas por
    defecto es ";", no ",": si el CSV viene con comas, Excel puede volcar
    todo en una sola columna en vez de separarlas.
    """
    contenido_bytes = _bytes_csv_real(ruta)
    texto = None
    for codificacion in ("utf-8-sig", "utf-8", "cp1252", "latin-1"):
        try:
            texto = contenido_bytes.decode(codificacion)
            break
        except UnicodeDecodeError:
            continue
    if texto is None:
        texto = contenido_bytes.decode("latin-1", errors="replace")

    dialecto = csv.excel
    dialecto.delimiter = _mejor_delimitador(texto)

    filas = list(csv.reader(texto.splitlines(), dialect=dialecto))
    filas = [fila for fila in filas if any(celda.strip() for celda in fila)]

    if not filas:
        return [], []

    encabezados = [celda.strip() for celda in filas[0]]
    datos = filas[1:]
    return encabezados, datos


def descargar_csv_oficial(page: Page, carpeta: Path, tipo: str, cuit: str, periodo: str, log: Logger) -> Path | None:
    """
    Alternativa al Excel oficial (ver descargar_excel_oficial): algunas
    grillas de Mis Comprobantes (confirmado en Emitidos) no ofrecen boton
    "Excel", pero si "CSV". Descargar el CSV entero y parsearlo en Python es
    mucho mas rapido que leer la grilla fila por fila con extraer_tabla_completa
    cuando hay miles de registros (ese camino queda como ultimo respaldo).
    """
    try:
        page.keyboard.press("Escape")
        page.locator(".dt-button-background, .dt-button-collection").evaluate_all(
            "els => els.forEach(el => el.remove())"
        )
    except Exception:
        pass

    boton_csv = page.locator(
        "button.buttons-csv:visible, a.buttons-csv:visible, button:has-text('CSV'):visible, a:has-text('CSV'):visible"
    ).first

    try:
        boton_csv.wait_for(state="visible", timeout=8_000)
    except Exception as exc:
        log.write(f"No se encontro el boton CSV oficial; se usara respaldo local. Detalle: {exc}")
        return None

    temp = carpeta / f"ARCA_{tipo}_{normalizar_cuit(cuit)}_{periodo}_descarga.csv"

    try:
        if temp.exists():
            temp.unlink()

        with page.expect_download(timeout=30_000) as download_info:
            boton_csv.click(timeout=8_000, force=True)
        descarga = download_info.value
        descarga.save_as(str(temp))

        if not temp.exists() or temp.stat().st_size == 0:
            log.write("La descarga oficial de CSV no genero un archivo valido; se usara respaldo local.")
            return None

        log.write(f"Archivo descargado desde CSV oficial de ARCA: {temp}")
    except Exception as exc:
        log.write(f"No se pudo descargar el CSV oficial; se usara respaldo local. Detalle: {exc}")
        return None

    try:
        encabezados, datos = _parsear_csv(temp)
    except Exception as exc:
        log.write(f"No se pudo interpretar el CSV descargado; se usara respaldo local. Detalle: {exc}")
        return None

    if not datos:
        log.write("El CSV descargado no tiene filas; se usara respaldo local.")
        try:
            temp.unlink()
        except Exception:
            pass
        return None

    if len(encabezados) <= 1:
        # No deberia pasar con el detector de separador actual, pero si
        # pasa, no tiene sentido seguir armando un Excel con una sola
        # columna gigante (ya causo un error de Excel COM en una corrida
        # real). Se conserva el CSV crudo para diagnostico (mismo criterio
        # que el HTML de diagnostico en el camino de scraping) y se cae al
        # respaldo de lectura de la grilla.
        log.write(
            f"ADVERTENCIA: el CSV se interpreto con una sola columna, algo no encaja. "
            f"Se conserva el archivo original para revisar: {temp}. Se usara respaldo local."
        )
        return None

    log.write(f"CSV interpretado: {len(datos)} filas, {len(encabezados)} columnas.")
    try:
        temp.unlink()
    except Exception:
        pass

    return generar_excel(encabezados, datos, carpeta, tipo, cuit, periodo, log)


def escribir_xlsx_simple(encabezados: list[str], datos: list[list[str]], destino: Path) -> None:
    """Escribe un .xlsx de una hoja ("Sheet1") con todo como texto, sin Excel ni librerias externas."""
    from xml.sax.saxutils import escape

    def col(n: int) -> str:
        nombre = ""
        while n:
            n, r = divmod(n - 1, 26)
            nombre = chr(65 + r) + nombre
        return nombre

    def celda(fila: int, columna: int, valor: str) -> str:
        texto = "".join(ch for ch in str(valor) if ch.isprintable() or ch.isspace())
        return f'<c r="{col(columna)}{fila}" t="inlineStr"><is><t xml:space="preserve">{escape(texto)}</t></is></c>'

    filas = [encabezados] + list(datos)
    hoja = "".join(
        f'<row r="{i}">' + "".join(celda(i, j, v) for j, v in enumerate(fila, 1)) + "</row>"
        for i, fila in enumerate(filas, 1)
    )
    if destino.exists():
        destino.unlink()
    with zipfile.ZipFile(destino, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>')
        z.writestr("_rels/.rels", '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>')
        z.writestr("xl/workbook.xml", '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>')
        z.writestr("xl/_rels/workbook.xml.rels", '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>')
        z.writestr("xl/worksheets/sheet1.xml", f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>{hoja}</sheetData></worksheet>')


def generar_excel(encabezados: list[str], datos: list[list[str]], carpeta: Path, tipo: str, cuit: str, periodo: str, log: Logger) -> Path:
    if not datos:
        raise BotError("No se leyeron filas de datos de la tabla.", 6)

    nombre_final = f"ARCA_{tipo}_{normalizar_cuit(cuit)}_{periodo}.xls"
    destino = carpeta / nombre_final
    if destino.exists():
        destino.unlink()

    try:
        excel = abrir_excel()
    except ExcelNoDisponible as exc:
        # Sin Excel (servidor sin Office) no hay .xls: se arma un .xlsx simple que AlfaCore lee igual.
        final = carpeta / f"ARCA_{tipo}_{normalizar_cuit(cuit)}_{periodo}.xlsx"
        escribir_xlsx_simple(encabezados, datos, final)
        log.write(f"Excel no esta disponible en este equipo ({exc}); archivo generado sin Excel: {final}")
        return final
    excel.Visible = False
    excel.DisplayAlerts = False
    libro = None
    try:
        libro = excel.Workbooks.Add()
        ws = libro.ActiveSheet

        # El importador de VB6 exige que la hoja se llame "Sheet1" (cartel en
        # pantalla). En un Excel con configuracion regional en espanol, la
        # hoja activa de un libro nuevo se llama "Hoja1", no "Sheet1": sin
        # este rename, el importador no encuentra los datos.
        try:
            ws.Name = "Sheet1"
        except Exception:
            pass

        # Escribir celda por celda via COM es muy lento con miles de filas
        # (cada asignacion es un viaje de ida y vuelta COM). Asignar todo el
        # bloque de una sola vez con un Range es ordenes de magnitud mas
        # rapido. Range.Value exige un array 2D parejo, por eso se rellenan
        # las filas mas cortas antes de armar la tupla de tuplas.
        cant_columnas = max(len(encabezados), max((len(fila) for fila in datos), default=0))

        encabezados_norm = tuple(encabezados) + ("",) * (cant_columnas - len(encabezados))
        ws.Range(ws.Cells(1, 1), ws.Cells(1, cant_columnas)).Value = encabezados_norm

        filas_norm = tuple(
            tuple(fila) + ("",) * (cant_columnas - len(fila)) for fila in datos
        )
        ws.Range(ws.Cells(2, 1), ws.Cells(1 + len(datos), cant_columnas)).Value = filas_norm

        libro.SaveAs(str(destino), XL_EXCEL8)
    finally:
        if libro is not None:
            libro.Close(SaveChanges=False)
        excel.Quit()

    if not destino.exists() or destino.stat().st_size == 0:
        raise BotError("No se pudo generar el Excel a partir de la tabla leida.", 6)

    log.write(f"Archivo generado desde la tabla: {destino}")
    return destino


def guardar_captura(page: Page | None, carpeta: Path, log: Logger) -> Path | None:
    if page is None:
        return None
    ruta = carpeta / f"error_{datetime.now():%Y%m%d_%H%M%S}.png"
    try:
        page.screenshot(path=str(ruta), full_page=True)
        log.write(f"Captura de error guardada: {ruta}")
        return ruta
    except Exception as exc:
        log.write(f"No se pudo guardar la captura de error: {exc}")
        return None


def main() -> int:
    if len(sys.argv) not in {7, 8}:
        print(
            "Uso: python arca_comprobantes.py CUIT_LOGIN CLAVE CUIT_REPRESENTADO "
            "PERIODO RECIBIDOS|EMITIDOS CARPETA [VISIBLE]"
        )
        return 1

    _, cuit_login_raw, clave, cuit_representado_raw, periodo, tipo_raw, carpeta_raw, *resto = sys.argv

    cuit_login = normalizar_cuit(cuit_login_raw)
    cuit_representado = normalizar_cuit(cuit_representado_raw)
    tipo = tipo_raw.strip().upper()
    visible = parse_visible(resto[0] if resto else None)
    desde, hasta = fechas_periodo(periodo)

    if len(cuit_login) != 11 or len(cuit_representado) != 11:
        raise BotError("CUIT_LOGIN y CUIT_REPRESENTADO deben tener 11 digitos.", 1)
    if not clave:
        raise BotError("La clave fiscal no puede estar vacia.", 1)
    if tipo not in {"RECIBIDOS", "EMITIDOS"}:
        raise BotError("TIPO debe ser RECIBIDOS o EMITIDOS.", 1)

    carpeta = Path(carpeta_raw).expanduser().resolve()
    log = Logger(carpeta)
    log.write("=" * 60)
    log.write(f"ARCA Comprobantes v{VERSION}")
    log.write(f"CUIT login: {cuit_login}")
    log.write(f"CUIT representado: {cuit_representado}")
    log.write(f"Periodo: {periodo} ({desde} a {hasta})")
    log.write(f"Tipo: {tipo}")
    log.write(f"Navegador visible: {'SI' if visible else 'NO'}")

    page: Page | None = None
    browser: Browser | None = None

    try:
        with sync_playwright() as p:
            browser = p.chromium.launch(headless=not visible)
            context = browser.new_context(
                accept_downloads=True,
                # Ancho grande a proposito: la tabla de Mis Comprobantes tiene
                # 30 columnas, y con una ventana angosta ARCA la separa en dos
                # tablas (una con columnas "congeladas" y otra con scroll),
                # lo que complica mucho leerla del DOM. Con una ventana bien
                # ancha, en teoria deberia entrar todo en una sola tabla.
                viewport={"width": 2600, "height": 1000},
                locale="es-AR",
            )
            context.set_default_timeout(TIMEOUT_MS)
            context.set_default_navigation_timeout(NAVIGATION_TIMEOUT_MS)
            page = context.new_page()

            try:
                page = iniciar_sesion(page, context, cuit_login, clave, log, carpeta)
                page = abrir_mis_comprobantes(page, context, log)
                seleccionar_representado(page, cuit_representado, log)
                abrir_tipo(page, tipo, log)
                cargar_periodo_y_buscar(page, desde, hasta, log)
                archivo = descargar_excel_oficial(page, carpeta, tipo, cuit_representado, periodo, log)

                if archivo is None:
                    # Algunas grillas (confirmado en Emitidos) no ofrecen boton
                    # "Excel" pero si "CSV". Descargar el CSV entero es mucho
                    # mas rapido que leer la grilla fila por fila cuando hay
                    # miles de registros - eso queda como ultimo respaldo.
                    archivo = descargar_csv_oficial(page, carpeta, tipo, cuit_representado, periodo, log)

                if archivo is None:
                    seleccionar_todas_columnas(page, log)

                    # Diagnostico: se guarda el HTML completo tal cual esta en
                    # este momento, para poder revisar la estructura real de la
                    # tabla en vez de seguir adivinando selectores a ciegas.
                    try:
                        ruta_html = carpeta / f"debug_tabla_{datetime.now():%Y%m%d_%H%M%S}.html"
                        ruta_html.write_text(page.content(), encoding="utf-8")
                        log.write(f"HTML de diagnostico guardado: {ruta_html}")
                    except Exception as exc:
                        log.write(f"No se pudo guardar el HTML de diagnostico: {exc}")

                    resultado_api = extraer_datos_via_datatables_api(page, log)
                    if resultado_api is not None:
                        encabezados, datos = resultado_api
                    else:
                        ampliar_filas_por_pagina(page, log)
                        encabezados, datos = extraer_tabla_completa(page, log)
                    archivo = generar_excel(encabezados, datos, carpeta, tipo, cuit_representado, periodo, log)

                log.write("PROCESO FINALIZADO CORRECTAMENTE")
                log.write(f"RESULTADO={archivo}")

                context.close()
                browser.close()
                return 0
            except Exception:
                # Captura de pantalla ADENTRO del "with": el navegador todavia
                # esta vivo aca. Afuera del "with" ya se cerro la conexion de
                # Playwright y la captura falla sola ("Event loop is closed").
                guardar_captura(page, carpeta, log)
                raise

    except BotError as exc:
        log.write(f"ERROR {exc.exit_code}: {exc}")
        return exc.exit_code
    except (PlaywrightTimeoutError, PlaywrightError) as exc:
        log.write(f"ERROR 9 PLAYWRIGHT: {exc}")
        return 9
    except Exception as exc:
        log.write(f"ERROR 9 INESPERADO: {exc}")
        log.write(traceback.format_exc())
        return 9
    finally:
        try:
            if browser is not None and browser.is_connected():
                browser.close()
        except Exception:
            pass


if __name__ == "__main__":
    try:
        sys.exit(main())
    except BotError as exc:
        print(f"ERROR {exc.exit_code}: {exc}")
        sys.exit(exc.exit_code)
