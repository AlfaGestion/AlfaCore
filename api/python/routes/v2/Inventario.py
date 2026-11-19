from datetime import datetime
from flask import request
from functions.general_customer import exec_customer_sql
from functions.responses import set_response
from .master import MasterView


class InventarioView(MasterView):

    def post(self):
        data = request.get_json() or {}
        items = data.get('items', []) or []

        observaciones = data.get('observacion', '')
        if not observaciones:
            observaciones = 'Creada desde el colector'

        id_deposito = data.get('iddeposito', '   1')
        if not id_deposito:
            id_deposito = '   1'
        usuario = data.get('usuario', 'Colector')

        fecha = datetime.now().strftime('%d/%m/%Y')
        sql_header = f"""
        DECLARE @pRes INT
        DECLARE @pMensaje NVARCHAR(250)
        DECLARE @pIdInventario INT

        set nocount on; EXEC sp_web_MV_INVENTARIOCAB '{fecha}','{id_deposito}','{usuario}','{observaciones}',@pRes OUTPUT, @pMensaje OUTPUT,@pIdInventario OUTPUT

        SELECT @pRes as pRes, @pMensaje as pMensaje, @pIdInventario pIdInventario
        """

        try:
            result, error = exec_customer_sql(sql_header, " al grabar el inventario", self.token_global, True)
        except Exception as e:
            error = True

        if error:
            self.log(str(result[0]['message']) + "\nSENTENCIA : " + sql_header)
            return set_response(None, 404, "Ocurrio un error al grabar el inventario.")

        result_code = result[0][0]
        if result_code != 11:
            self.log(str(result[0][1]) + "\nSENTENCIA : " + sql_header)
            return set_response(None, 404, result[0][1])

        inventario_id = result[0][2]

        def _to_number(value):
            if value is None:
                return 0
            try:
                if isinstance(value, str):
                    value = value.replace(',', '.').strip()
                return float(value)
            except Exception:
                return 0
        print(items)
        for item in items:
            id_articulo = item.get('idarticulo', '') or item.get('product', '')
            if id_articulo is None:
                id_articulo = ''
            id_articulo = str(id_articulo)
            if len(id_articulo) < 25:
                id_articulo = id_articulo.rjust(25)
            else:
                id_articulo = id_articulo[:25]
            id_unidad = item.get('idunidad', '')
            conteo1 = item.get('conteo1', item.get('quantity', 0))
            costo = item.get('costo', item.get('amount', item.get('unitary', 0)))
            conteo1 = _to_number(conteo1)
            costo = _to_number(costo)

            sql_detail = f"""
            INSERT INTO MV_INVENTARIOS
                (IdInventario, IdArticulo, IdUnidad, Stock, Conteo1, Conteo2, Diferencia, Costo)
            VALUES
                ({inventario_id}, '{id_articulo}', '{id_unidad}', 0, {conteo1}, 0, 0, {costo})
            """

            try:
                _, error = exec_customer_sql(sql_detail, " al grabar el detalle del inventario", self.token_global, False)
            except Exception as e:
                error = True

            if error:
                self.log(str(result[0]['message']) + "\nSENTENCIA : " + sql_detail)
                return set_response(None, 404, "Ocurrio un error al grabar el detalle del inventario.")

        return set_response([{ 'idInventario': inventario_id }], 200, "Inventario grabado correctamente.")
