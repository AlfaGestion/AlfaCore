from datetime import datetime
from functions.general_customer import get_customer_response, exec_customer_sql
from functions.responses import set_response
from flask_classful import route
from flask import request
from routes.v2.master import MasterView


class ViewCustomer(MasterView):
    def index(self):
        sql = """
        SELECT a.codigo,isnull(CodigoOpcional,'') as codigo_opcional,ltrim(RAZON_SOCIAL) as razon_social,
        isnull(isnull(ltrim(calle),'') + ' ' + isnull(numero,'') + ' ' + isnull(piso,'') +  ' ' + isnull(DEPARTAMENTO,''),'') as calle,
        isnull(ltrim(numero),'') as numero_calle,
        ltrim(LOCALIDAD) as localidad,isnull(ltrim(NUMERO_DOCUMENTO),'') as cuit,isnull(Clase,1) as clase,isnull(ltrim(iva),'') as iva,
        isnull(descuento,0) as dto,'FC' as cpte_default,isnull(ltrim(IdVendedor),'') as idvendedor,isnull(ltrim(TELEFONO),'') as telefono, isnull(LTRIM(MAIL),'') as email,
        isnull(ltrim(a.PROVINCIA),'') as provincia_id,isnull(ltrim(c.DESCRIPCION),'') as provincia_nombre,
        isnull(ltrim(a.PAIS),'') as pais_id,isnull(ltrim(d.DESCRIPCION),'') as pais_nombre
        FROM VT_CLIENTES a
        LEFT JOIN TA_ESTADOS c ON a.PROVINCIA = c.CODIGO
        LEFT JOIN TA_PAISES d ON a.PAIS = d.CODIGO
        where dada_de_baja=0 and CTACODIGO <> 0 and a.CODIGO <> 0;
        """
        result, error = get_customer_response(sql, " al obtener los clientes", True, self.token_global)
        return set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])

    def post(self):
        data = request.get_json()

        name = data.get('name', '')
        email = data.get('email', '')
        address = data.get('address', '')
        number = data.get('number', '')
        location = data.get('location', '')
        phone = data.get('phone', '')
        contact = data.get('contact', '')
        obs = data.get('obs', '')
        cuit = data.get('cuit', '')
        seller = data.get('seller', '')
        iva = data.get('iva', '')
        cp = data.get('cp', '')
        docType = data.get('docType', '')
        code = data.get('code', '')
        app_id = data.get('appId', data.get('idcliente', data.get('idCliente', '')))
        latitude = data.get('latitude', '')
        longitude = data.get('longitude', '')

        if code == 'None' or code is None:
            code = ''
        if code and not str(code).strip().startswith('11201'):
            app_id = app_id or code
            code = ''

        query = f"""
        DECLARE @Codigo NVARCHAR(9)
        set nocount on;EXEC sp_web_altaCliente '{code}','{name}','{email}','{address}','{number}','{location}','{phone}','{contact}','{cuit}','{obs}','{seller}','{iva}','{cp}','{docType}',@Codigo OUTPUT
        SELECT @Codigo as codigo
        """

        rows = self.get_response(query, "Error al crear el cliente", True, True)
        new_code = rows[0][0] if rows and rows[0] else ''

        try:
            lat = str(latitude).strip() if latitude is not None else ''
            lng = str(longitude).strip() if longitude is not None else ''
            if new_code and lat not in ['', '0', '0.0'] and lng not in ['', '0', '0.0']:
                sql_coords = f"UPDATE MA_CUENTASADIC SET X='{lat}', Y='{lng}' WHERE CODIGO='{new_code}'"
                self.get_response(sql_coords, f"Error al actualizar coordenadas del cliente {new_code}", True, True)
            if new_code and app_id:
                safe_app_id = str(app_id).replace("'", "''")
                sql_web = f"UPDATE MA_CUENTASADIC SET WEB='id_app {safe_app_id}' WHERE CODIGO='{new_code}'"
                self.get_response(sql_web, f"Error al actualizar el marcador web del cliente {new_code}", True, True)
        except Exception:
            pass

        return set_response({'codigo': new_code}, 200)

    @route('/<string:code>/expired_invoice', methods=['GET'])
    def get_expired_invoice(self, code: str):
        sql = "SELECT valor,clave FROM TA_CONFIGURACION WHERE CLAVE='NoFacturaVencidos'"
        response = self.get_response(sql, "Error al obtener la configuracion del sistema", True, False)

        can_charge_invoice = False
        if response:
            can_charge_invoice = response[0].get('valor', 'NO') == 'SI'

        if not can_charge_invoice:
            return set_response({'factura_vencidos': True, 'facturas': ''}, 200)

        sql = f"""
        IF NOT Exists(Select * from ma_clavepin where Fecha is null and Cuenta = '{code}' and motivo = '10')
        SELECT tc,sucursal,numero,letra,convert(varchar(10),fecha,103) as fecha,convert(varchar(10),vencimiento,103) as vencimiento,convert(varchar,convert(decimal(15,2),importe)) as importe  FROM VE_CPTES_IMPAGOS
        WHERE CUENTA = '{code}' and datediff(day,VENCIMIENTO,GETDATE())>7 and (TC='FP' OR TC='FC')
        """
        response = self.get_response(sql, "Error al obtener las facturas vencidas", True, False)

        if response:
            return set_response({'factura_vencidos': False, 'facturas': response}, 200)
        return set_response({'factura_vencidos': True, 'facturas': ''}, 200)

    @route('/paginate/<int:page>')
    def paginate(self, page: int):
        page_size = 100

        sql = f"""
        DECLARE @PageNumber int
        DECLARE @PageSize int
        DECLARE @rs NVARCHAR(MAX)
        DECLARE @from NVARCHAR(10), @until NVARCHAR(10)

        set @PageNumber = {page}
        set @PageSize = {page_size}

        IF @PageNumber = 1
            SET @from = 1
        ELSE
            SET @from = (@PageNumber * @PageSize) - @PageSize + 1

        SET @until = (@PageNumber * @PageSize)

         SET @rs = 'SELECT * FROM (
        SELECT ROW_NUMBER() OVER (ORDER BY a.codigo) as RowNr,
        a.codigo,isnull(CodigoOpcional,'''') as codigo_opcional,ltrim(RAZON_SOCIAL) as razon_social,
        ltrim(isnull(isnull(ltrim(calle),'''') + '' '' + isnull(numero,'''') + '' '' + isnull(piso,'''') +  '' '' + isnull(DEPARTAMENTO,''''),'''')) as calle,
        isnull(ltrim(numero),'''') as numero_calle,
        ltrim(LOCALIDAD) as localidad,isnull(ltrim(NUMERO_DOCUMENTO),'''') as cuit,isnull(Clase,1) as clase,isnull(ltrim(iva),'''') as iva,
        isnull(descuento,0) as dto,''FC'' as cpte_default,isnull(ltrim(IdVendedor),'''') as idvendedor,isnull(ltrim(TELEFONO),'''') as telefono, isnull(LTRIM(MAIL),'''') as email,isnull(ltrim(idlista),'''') as lista,
        isnull(ltrim(X),'''') as campo_x,
        isnull(ltrim(Y),'''') as campo_y,
        isnull(ltrim(a.PROVINCIA),'''') as provincia_id,isnull(ltrim(c.DESCRIPCION),'''') as provincia_nombre,
        isnull(ltrim(a.PAIS),'''') as pais_id,isnull(ltrim(d.DESCRIPCION),'''') as pais_nombre
        FROM VT_CLIENTES a
        LEFT JOIN TA_ESTADOS c ON a.PROVINCIA = c.CODIGO
        LEFT JOIN TA_PAISES d ON a.PAIS = d.CODIGO
        WHERE DADA_DE_BAJA=0) g
        WHERE RowNr BETWEEN ' + @from + ' AND ' + @until

        EXEC(@rs)
        """

        result, error = get_customer_response(sql, f" al obtener los clientes en pagina {page}", True, self.token_global)

        total = 0
        pages = 0
        try:
            count_sql = "SELECT COUNT(*) FROM VT_CLIENTES a WHERE DADA_DE_BAJA=0"
            count_rows, count_error = exec_customer_sql(count_sql, " al contar clientes", self.token_global, True)
            if not count_error and count_rows:
                total = int(count_rows[0][0])
                pages = int((total + page_size - 1) / page_size) if total > 0 else 0
        except Exception:
            pass

        payload = {"rows": result, "total": total, "pages": pages, "page": page, "page_size": page_size}
        return set_response(payload, 200 if not error else 404, "" if not error else result[0]['message'])

    @route('get_balance/<string:codigo>/<string:fhd>/<string:fhh>/<int:solo_pendiente>')
    def get_balance(self, codigo: str, fhd: str, fhh: str, solo_pendiente: int = 0):
        fecha_desde = datetime.strptime(fhd, '%Y%m%d').strftime("%d/%m/%Y")
        fecha_hasta = datetime.strptime(fhh, '%Y%m%d').strftime("%d/%m/%Y")

        sql = f"""
        SELECT orden,fh,fecha,tc,idcomprobante,detalle,convert(varchar,convert(decimal(15,2),importe)) as importe FROM (
        SELECT 2 as orden, fecha as fh,convert(varchar(10),fecha,103) as fecha,tc, sucursal + numero + letra as idcomprobante,detalle,
        case [debe-haber] when 'D' then importe when 'H' then importe * -1 end as importe
        FROM MV_ASIENTOS WHERE (cuenta = '{codigo}') and (fecha >= '{fecha_desde}' and fecha <= '{fecha_hasta}')
        UNION
        SELECT 1 as orden,'{fecha_hasta}' as fh, '' as fecha, '' as tc, 'SALDO ACTUAL' as idcomprobante, '' as detalle,
        isnull(SUM(CASE [DEBE-HABER] WHEN 'D' THEN IMPORTE WHEN 'H' THEN IMPORTE * - 1 END),0) AS SumaDeImporte
        FROM dbo.MV_ASIENTOS WHERE CUENTA = '{codigo}'
        ) AS A order by orden,fh desc
        """

        result, error = get_customer_response(sql, f" al obtener la cuenta corriente del cliente {codigo}", True, self.token_global)
        return set_response(result, 200 if not error else 404, "" if not error else result[0]['message'])

    @route('<string:code>/files')
    def get_customer_files(self, code: str):
        query = f"SELECT id,ruta_archivo as path,observaciones as name FROM MA_CUENTAS_ARCHIVOS_RELACIONADOS WHERE CUENTA='{code}'"
        files = self.get_response(query, f"Error al obtener los archivos de la cuenta {code}.", True)
        return set_response(files, 200)

    @route('<string:code>/file/save', methods=['POST'])
    def register_file_in_database(self, code: str):
        data = request.get_json()
        name = data.get('name', '')
        filename = data.get('filename', '')
        query = f"INSERT INTO MA_CUENTAS_ARCHIVOS_RELACIONADOS (CUENTA,RUTA_ARCHIVO,OBSERVACIONES) VALUES ('{code}','{filename}','{name}')"
        response = self.get_response(query, f"Error al registrar el archivo {filename} en la cuenta {code}.", False, True)
        return set_response(response, 200)

    @route('file/<string:id>/delete', methods=['DELETE'])
    def delete_file_from_database(self, id: str):
        query = f"DELETE FROM MA_CUENTAS_ARCHIVOS_RELACIONADOS WHERE id={id}"
        response = self.get_response(query, f"Error al eliminar el archivo {id}.", False, True)
        return set_response(response, 200)

    @route('/block', methods=["POST"])
    def block(self):
        data = request.get_json()
        code = data.get('code', '')
        if not code:
            return set_response([], 404, 'Debe informar el codigo de cliente')
        query = f"UPDATE MA_CUENTAS SET dada_de_baja=1 WHERE codigo='{code}'"
        result, error = exec_customer_sql(query, f" al bloquear la cuenta {code}", self.token_global)
        return set_response(result, 200 if not error else 404, "" if not error else f"Error al bloquear la cuenta {code}")

    @route('/unblock', methods=["POST"])
    def unblock(self):
        data = request.get_json()
        code = data.get('code', '')
        if not code:
            return set_response([], 404, 'Debe informar el codigo de cliente')
        query = f"UPDATE MA_CUENTAS SET dada_de_baja=0 WHERE codigo='{code}'"
        result, error = exec_customer_sql(query, f" al desbloquear la cuenta {code}", self.token_global)
        return set_response(result, 200 if not error else 404, "" if not error else f"Error al desbloquear la cuenta {code}")
