from .general_customer import get_customer_response, exec_customer_sql
# from rich import print
from datetime import datetime

token_global = ''


def get_cajas_segun_fh_operativa(fecha: str, token: str) -> list:
    """
    Retorna las cajas de una fecha especifica
    """
    result = []

    fecha = datetime.strptime(
        fecha, '%d%m%Y').strftime('%d/%m/%Y') if fecha else datetime.today().strftime('%d/%m/%Y')

    sql = f"""
    SELECT ltrim(mv_asientos.idcajas) as idcaja, V_Ta_Cajas.descripcion FROM MV_ASIENTOS LEFT JOIN V_TA_CAJAS
    ON V_Ta_Cajas.IdCajas = MV_ASIENTOS.IdCajas WHERE FECHA='{fecha}' and mv_asientos.idcajas<>''
    GROUP BY mv_asientos.IdCajas,V_Ta_Cajas.Descripcion
    """

    result, error = get_customer_response(
        sql, f" las cajas de la fecha {fecha}", True, token=token)

    return result, error


def get_cierre_caja(fecha: str = '', idcaja: str = '', config={}, token: str = '') -> list:
    """
    Retorna la información del cierre de caja
    """
    global token_global
    token_global = token
    response = []

    # Genero el where según fecha e idcaja
    # where = ''
    fecha = datetime.strptime(
        fecha, '%d%m%Y').strftime('%d/%m/%Y') if fecha else datetime.today().strftime('%d/%m/%Y')

    # where = f" fecha = '{fecha}' "
    # where = where + f" and idcajas = '   {idcaja}'" if idcaja else where

    show_cancel = config.get("show_cancel", False)  # Comprobantes cancelados
    category_detail = config.get("category_detail", False)  # Detalle por rubro
    products_sale = config.get("products_sale", False)  # Venta por productos
    sale_detail = config.get("sale_detail", False)  # Detalle de ventas
    daily_detail = config.get("daily_detail", False)  # Diario
    monthly_detail = config.get("monthly_detail", False)  # Mensual
    payment_detail = config.get(
        "payment_detail", False)  # Detalle de cobranzas
    initial_balance = config.get('initial_balance', False)  # Saldo inicial
    initial_balance = 1 if initial_balance else 0

    response = [{
        "fecha": fecha,
        "idcaja": idcaja,
        # Consolidado
        "consolidado": _get_consolidado(fecha, idcaja, initial_balance),
        # Saldo de tarjetas
        "saldo_tarjeta": _get_saldo_tarjeta(fecha, idcaja),
        # Acumulado ventas
        "acumulado_ventas": _get_acumulado_ventas(fecha, idcaja),
        # Cobranzas en cuenta corriente
        "cb_ctacte": _get_cobranzas_ctacte(fecha, idcaja),
        # Ventas en cuenta corriente
        "ventas_ctacte": _get_ventas_ctacte(fecha, idcaja),
        # Transferencias
        "transferencias": _get_transferencias_realizadas(fecha, idcaja),
        # Comprobantes cancelados
        "comprobantes_cancelados": _get_comprobantes_cancelados(fecha, idcaja) if show_cancel else [],
        # Detalle de efectivo
        "detalle_efectivo": _get_detalle_efectivo(fecha, idcaja, initial_balance),
        # Ingresos
        "ingresos": _get_ingresos_egresos(fecha, idcaja, "I"),
        "egresos": _get_ingresos_egresos(fecha, idcaja, "E"),  # Egresos
        # Detalle por rubro
        "detalle_rubro": _get_detalle_por_rubro(fecha) if category_detail else [],
        # Detalle por producto
        "detalle_producto": _get_detalle_por_producto(fecha) if products_sale else [],
        # Detalle ventas
        "detalle_ventas": _get_detalle_ventas(fecha, idcaja, False) if sale_detail else [],
        # Detalle cobranzas
        "detalle_cobranzas": _get_detalle_ventas(fecha, idcaja, True) if payment_detail else [],
        # Detalle diario
        "detalle_diario": _get_detalle_diario(fecha, idcaja) if daily_detail else [],
        # Detalle mensual
        "detalle_mensual": _get_detalle_diario(fecha, idcaja, True) if monthly_detail else [],
    }]

    return response


def _get_consolidado(fecha: str, idcaja: str, initial: int = 0) -> list:
    """
    Retorna la información consolidada de la caja
    """
    result = []

    sql = f"""
    exec sp_web_getSaldoConsolidadoCaja '{fecha}','{idcaja}',{initial}
    """

    result, _ = get_customer_response(
        sql, " el saldo consolidado", True, token=token_global)
    return result


def _get_saldo_tarjeta(fecha: str, idcaja: str) -> list:
    """
    Retorna el saldo de las tarjetas
    """
    result = []

    sql = f"""
    exec sp_web_getSaldoTJCaja '{fecha}','{idcaja}'
    """
    result, _ = get_customer_response(
        sql, " el saldo de tarjetas", True, token=token_global)
    return result


def _get_acumulado_ventas(fecha: str, idcaja: str) -> list:
    """
    Retorna el acumulado de ventas
    """
    result = []

    sql = f"""
    exec sp_web_getAcumuladoVentasCaja '{fecha}','{idcaja}'
    """

    result, _ = get_customer_response(
        sql, " el acumulado de ventas", True, token=token_global)
    return result


def _get_cobranzas_ctacte(fecha: str, idcaja: str) -> list:
    """
    Retorna las cobranzas en cuenta corriente
    """
    result = []

    sql = f"""exec sp_web_getComprobantesCtaCte '{fecha}','{idcaja}','CB'"""

    result, _ = get_customer_response(
        sql, " las cobranzas en cta cte", True, token=token_global)
    return result


def _get_ventas_ctacte(fecha: str, idcaja: str) -> list:
    """
    Retorna las ventas en cuenta corriente
    """
    result = []

    sql = f"""exec sp_web_getComprobantesCtaCte '{fecha}','{idcaja}','FC'"""

    result, _ = get_customer_response(
        sql, " las ventas en cta cte", True, token=token_global)
    return result


def _get_transferencias_realizadas(fecha: str, idcaja: str) -> list:
    """
    Retorna las transferencias realizadas
    """
    result = []

    sql = f"""exec sp_web_getTransferenciasCaja '{fecha}','{idcaja}'"""

    result, _ = get_customer_response(
        sql, " las transferencias realizadas", True, token=token_global)
    return result


def _get_comprobantes_cancelados(fecha: str, idcaja: str) -> list:
    """
    Retorna los comprobantes cancelados
    """
    result = []

    sql = f"""
    exec sp_web_getComprobantesCanceladosCaja '{fecha}','{idcaja}'
    """
    result, _ = get_customer_response(
        sql, " los comprobantes cancelados", True, token=token_global)
    return result


def _get_detalle_efectivo(fecha: str, idcaja: str, initial: int = 0) -> list:
    """
    Retorna el detalle de los movimientos del efectivo
    """
    result = []

    sql = f"""
        exec sp_web_getDetalleEfectivoCaja '{fecha}','{idcaja}',{initial}
    """

    result, _ = get_customer_response(
        sql, " el detalle de efectivo", True, token=token_global)
    return result


def _get_ingresos_egresos(fecha: str, idcaja: str, tipo: str) -> list:
    """
    Retorna los ingresos/egresos según corresponda
    """
    result = []

    sql = f"""
        exec sp_web_getIngresosEgresosCaja '{fecha}','   {idcaja}','{tipo}'
    """

    result, _ = get_customer_response(
        sql, " los ingresos/egresos", True, token=token_global)
    return result


def _get_detalle_por_rubro(fecha: str) -> list:
    """
    Retorna el detalle de ventas por rubro
    """
    result = []
    result_tmp = []
    error = False

    result_tmp, error = exec_customer_sql("EXEC NW_RECONS_ESTADISTICAS_PASO1 'V';",
                                          "reconstruir estadisticas rubros", token=token_global)
    if error:
        return result_tmp

    sql = f"""
        exec sp_web_getDetallePorRubroCaja '{fecha}'
    """

    result, _ = get_customer_response(
        sql, " el detalle por rubro", True, token=token_global)
    return result


def _get_detalle_por_producto(fecha: str) -> list:
    """
    Retorna el detalle de ventas por producto
    """
    result = []

    sql = f"""
        exec sp_web_getDetallePorProductoCaja '{fecha}'
    """

    result, _ = get_customer_response(
        sql, " el detalle por producto", True, token=token_global)
    return result


def _get_detalle_ventas(fecha: str, idcaja: str, cobranzas: bool = False) -> list:
    """
    Retorna el detalle de ventas por comprobante
    """
    result = []

    sql = f"""
        exec sp_web_getDetalleVentasPorCpteCaja '{fecha}','{idcaja}',{1 if cobranzas else 0}
        """

    result, _ = get_customer_response(
        sql, " el detalle de ventas por comprobante", True, token=token_global)
    return result


def _get_detalle_diario(fecha: str, idcaja: str, esMensual: bool = False) -> list:
    """
    Retorna el detalle de ventas diario
    """
    result = []

    if esMensual:
        sql = f"""
           exec sp_web_getDetalleDiarioCaja '{fecha}','{idcaja}', 1
        """
    else:
        sql = f"""
            exec sp_web_getDetalleDiarioCaja '{fecha}','{idcaja}', 0
        """

    result, _ = get_customer_response(
        sql, " el detalle de ventas diario/mensual", True, token=token_global)
    return result
