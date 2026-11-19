from .general_customer import get_customer_response, exec_customer_sql
from datetime import datetime


def get_depositos(token: str):
    """
    Retorna los depositos
    """

    result = []

    sql = f"""
    SELECT ltrim(iddeposito) as code, descripcion as name 
    FROM V_TA_DEPOSITO
    """

    result, _ = get_customer_response(
        sql, f" los depositos", True, token=token)

    return result


def get_saldo_query(deposit: str = '', product: str = '', page=1, token: str = ''):
    """
    Retorna el saldo 

    Si deposito o producto son igual a *, los blanqueo
    """

    result = []
    total_rows = []
    data = []
    data_cards = []
    error = False

    deposit = '' if deposit == '*' else deposit
    product = '' if product == '*' else product

    total_rows, error = get_customer_response(
        f"exec sp_web_getSaldosStockPagination '{deposit}','{product}'", "get pagination", True, token=token)

    if error:
        return total_rows

    data_cards, error = get_customer_response(
        f"exec sp_web_getSaldosStockCardTotal '{deposit}','{product}'", "get total cards", True, token=token)

    if error:
        return data_cards

    sql = f"""
    exec sp_web_getSaldosStock '{deposit}','{product}',{page}
    """

    data, _ = get_customer_response(
        sql, f" el saldo de los productos", True, token=token)

    result.append({
        'pagination': total_rows,
        'data': data,
        'info_cards': data_cards
    })

    return result


def get_product_stock_file(deposit: str = '', product: str = '', date: str = '', token: str = ''):
    """
    Retorna la ficha de stock de un producto

    Si deposito,producto o fecha son igual a *, los blanqueo
    """

    result = []

    deposit = '' if deposit == '*' else deposit
    product = '' if product == '*' else product

    date = '' if date == '*' else datetime.strptime(
        date, '%d%m%Y').strftime('%d/%m/%Y')

    sql = f"""
    exec sp_web_getFichaStockArticulo '{deposit}','{product}','{date}'
    """

    result, _ = get_customer_response(
        sql, f" la ficha de stock del producto {product}", True, token=token)

    return result
