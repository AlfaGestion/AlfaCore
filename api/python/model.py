import base64
import random

from conexion import conn

page_size = 30
IS_REACT_APP = False


def cerrarConexion():
    conn.close()


def db_getArticulos(page):
    lista = []
    if page == 0:
        page = 1

    try:
        with conn.cursor() as cursor:
            if page == -1:
                cursor.execute("SELECT idArticulo,descripcion,idRubro,ltrim(idfamilia) as idfamilia,impuestos,tasaIva,exento,precio1,precio2,precio3,precio4,precio5,precio6,precio7,precio8,precio9,precio10 from WsSysmobileArticulos")
            else:
                cursor.execute(
                    "EXEC dbo.wsSysMobileSPPaginacionArticulos {}, {}".format(page, page_size))

            res = cursor.fetchall()
            cursor.commit()
            lista = []

            for row in res:
                lista.append(
                    {
                        'idArticulo': row.idArticulo.strip(),
                        'descripcion': row.descripcion.strip(),
                        'idRubro': row.idRubro.strip(),
                        'idFamilia': row.idfamilia,
                        'impuestosInternos': float(row.impuestos),
                        'iva': row.tasaIva,
                        'exento': row.exento,
                        'precio1': float(row.precio1),
                        'precio2': float(row.precio2),
                        'precio3': float(row.precio3),
                        'precio4': float(row.precio4),
                        'precio5': float(row.precio5),
                        'precio6': float(row.precio6),
                        'precio7': float(row.precio7),
                        'precio8': float(row.precio8),
                        'precio9': float(row.precio9),
                        'precio10': float(row.precio10),
                    }
                )
    except Exception as e:
        print("Ocurrió un error al obtener los articulos: ", e)
    finally:
        return lista


def db_getClientes(page):

    if page == 0:
        page = 1

    try:
        with conn.cursor() as cursor:
            if page == -1:
                cursor.execute(
                    "SELECT codigo,codigoOpcional,Razon_Social,calle,numero,piso,departamento,localidad,numero_documento,iva,clase,descuento,cpteDefault,idVendedor,telefono,mail from wsSysMobileClientes")
            else:
                cursor.execute(
                    "EXEC dbo.wsSysMobileSPPaginacionClientes {}, {}".format(page, page_size))

            res = cursor.fetchall()
            cursor.commit()
            lista = []

            for row in res:
                lista.append(
                    {
                        'codigo': row.codigo,
                        'codigoOpcional': row.codigoOpcional.strip(),
                        'razonSocial': row.Razon_Social.strip(),
                        'calleNroPisoDpto': row.calle.strip() + ' ' + row.numero.strip() + ' ' + row.piso.strip() + ' ' + row.departamento.strip(),
                        'localidad': row.localidad.strip(),
                        'cuit': row.numero_documento.strip(),
                        'iva': row.iva.strip(),
                        'claseDePrecio': row.clase,
                        'porcDto': row.descuento,
                        'cpteDefault': row.cpteDefault.strip(),
                        'idVendedor': row.idVendedor.strip(),
                        'telefono': row.telefono.strip(),
                        'email': row.mail.strip(),
                    }
                )
    except Exception as e:
        print("Ocurrió un error al obtener los clientes: ", e)
    finally:
        return lista


def db_getDepositos():

    try:
        with conn.cursor() as cursor:
            cursor.execute("Select * from wsSysMobileDepositos")
            res = cursor.fetchall()
            cursor.commit()
            lista = []

            for row in res:
                lista.append(
                    {
                        'idDeposito': row.idDeposito.strip(),
                        'descripcion': row.Descripcion.strip()
                    }
                )
    except Exception as e:
        print("Ocurrió un error al obtener los depositos: ", e)
    finally:
        return lista


def db_getRubros():
    lista = []
    try:
        with conn.cursor() as cursor:
            cursor.execute("Select * from wsSysMobileRubros")
            res = cursor.fetchall()
            cursor.commit()
            lista = []

            for row in res:
                if IS_REACT_APP:
                    lista.append(
                        {'codigo': row.idRubro.strip(
                        ), 'descripcion': row.Descripcion.strip()}
                    )
                else:
                    lista.append(
                        {
                            'idRubro': row.idRubro.strip(),
                            'descripcion': row.Descripcion.strip()
                        }
                    )
    except Exception as e:
        print("Ocurrió un error al obtener los rubros: ", e)
    finally:
        return lista


def db_getFamilias():
    lista = []
    try:
        with conn.cursor() as cursor:
            cursor.execute("Select idfamilia,descripcion from V_TA_FAMILIAS")
            res = cursor.fetchall()
            cursor.commit()

            for row in res:
                if IS_REACT_APP:
                    lista.append(
                        {
                            'codigo': row.idfamilia.strip(),
                            'descripcion': row.descripcion.strip()
                        }
                    )
                else:
                    lista.append(
                        {
                            'idFamilia': row.idfamilia.strip(),
                            'descripcion': row.descripcion.strip()
                        }
                    )
    except Exception as e:
        print("Ocurrió un error al obtener las familias: ", e)
    finally:
        return lista


def db_getVendedores():
    lista = []
    try:
        with conn.cursor() as cursor:
            cursor.execute("Select * from wsSysMobileVendedores")
            res = cursor.fetchall()
            cursor.commit()
            for row in res:
                if IS_REACT_APP:
                    lista.append(
                        {
                            'codigo': row.idVendedor.strip(),
                            'descripcion': row.Nombre.strip(),
                            'codigoValidacion': row.codigoValidacion.strip()
                        }
                    )
                else:
                    lista.append(
                        {
                            'idVendedor': row.idVendedor.strip(),
                            'nombre': row.Nombre.strip(),
                            'codigoValidacion': row.codigoValidacion.strip()
                        }
                    )
    except Exception as e:
        print("Ocurrió un error al obtener los vendedores: ", e)
    finally:
        return lista


def db_getRegistros():
    lista = []
    try:
        with conn.cursor() as cursor:
            cursor.execute("Select * from wsSysMobileTotalRegistrosTablas")
            res = cursor.fetchall()
            cursor.commit()
            lista = []

            for row in res:
                lista.append(
                    {
                        'tabla': row.TABLA.strip(),
                        'cantidadRegistros': row.CANTIDAD,
                    }
                )
    except Exception as e:
        print("Ocurrió un error al obtener los vendedores: ", e)
    finally:
        return lista


# def db_getCtaCte(idcliente, fhd, fhh, soloPendiente):

#     # Las fechas vienen en formato YYYYMMDD, las paso a DD/MM/YYYY
#     fechaD = fhd[6:8] + '/' + fhd[4:6] + '/' + fhd[0:4]
#     fechaH = fhh[6:8] + '/' + fhh[4:6] + '/' + fhh[0:4]

#     lista = []
#     lerror = False
#     orden = 0

#     # Saldo anterior
#     try:
#         with conn.cursor() as cursor:
#             cursor.execute("SELECT isnull(SUM(CASE [DEBE-HABER] WHEN 'D' THEN IMPORTE WHEN 'H' THEN IMPORTE * - 1 END),0) AS SumaDeImporte "
#                            + " FROM dbo.MV_ASIENTOS WHERE CUENTA = '" + idcliente + "' AND FECHA < '" + fechaD + "'")

#             res = cursor.fetchall()
#             cursor.commit()

#             for row in res:
#                 lista.append(
#                     {
#                         'orden': orden,
#                         'fecha': '',
#                         'tc': '',
#                         'sucNroLetra': 'Saldo Anterior:',
#                         'detalle': '',
#                         'importe': float(row.SumaDeImporte),
#                     }
#                 )
#     except Exception as e:
#         print("Ocurrió un error al obtener el saldo anterior: ", e)
#     finally:
#         lerror = False

#     # Detalle de movimientos
#     try:
#         with conn.cursor() as cursor:
#             if soloPendiente == 1:
#                 cursor.execute("SELECT fh, fecha,tc,sucNroLetra,detalle,importe,saldo FROM (SELECT fecha as fh, convert(varchar(10),fecha,103) as fecha,tc, sucursal + numero + letra as sucNroLetra,detalle,"
#                                + " case [debe-haber] when 'D' then importe when 'H' then importe * -1 end as importe, " +
#                                " (select saldo from VE_CPTES_SALDOS_TODOS where comprobante=a.sucursal+a.numero+a.letra and tc=a.tc) as saldo " +
#                                "FROM MV_ASIENTOS a WHERE (cuenta = '" + idcliente + "') and (fecha >= '" + fechaD + "' and fecha <= '" + fechaH + "') ) as b where saldo<>0 order by fh desc")
#             else:
#                 cursor.execute("SELECT fecha as fh,convert(varchar(10),fecha,103) as fecha,tc, sucursal + numero + letra as sucNroLetra,detalle,"
#                                + " case [debe-haber] when 'D' then importe when 'H' then importe * -1 end as importe" +
#                                " FROM MV_ASIENTOS WHERE (cuenta = '" + idcliente + "') and (fecha >= '" + fechaD + "' and " +
#                                " fecha <= '" + fechaH + "') order by fh desc")

#             res = cursor.fetchall()
#             cursor.commit()

#             for row in res:
#                 orden += 1
#                 lista.append(
#                     {
#                         'orden': orden,
#                         'fecha': row.fecha,
#                         'tc': row.tc.strip(),
#                         'sucNroLetra': row.sucNroLetra,
#                         'detalle': row.detalle.strip(),
#                         'importe': float(row.importe),
#                     }
#                 )

#     except Exception as e:
#         print("Ocurrió un error al obtener los movimientos: ", e)
#     finally:
#         lerror = False

#     # Saldo Actual
#     try:
#         with conn.cursor() as cursor:
#             cursor.execute("SELECT isnull(SUM(CASE [DEBE-HABER] WHEN 'D' THEN IMPORTE WHEN 'H' THEN IMPORTE * - 1 END),0) AS SumaDeImporte "
#                            + " FROM dbo.MV_ASIENTOS WHERE CUENTA = '" + idcliente + "'")

#             res = cursor.fetchall()
#             cursor.commit()
#             orden += 1

#             for row in res:
#                 lista.append(
#                     {
#                         'orden': orden,
#                         'fecha': '',
#                         'tc': '',
#                         'sucNroLetra': 'Saldo Actual:',
#                         'detalle': '',
#                         'importe': float(row.SumaDeImporte),
#                     }
#                 )
#     except Exception as e:
#         print("Ocurrió un error al obtener el saldo anterior: ", e)
#     finally:
#         return lista


def db_setPedidos(json_pedidos):

    #conn = connect()
    # conn.close()

    fecha = ''
    pRes = 0
    pMensaje = ''
    pIdCpte = 0

    #lista = []

    with conn.cursor() as cursor:
        for p in json_pedidos:

            fecha = p['fecha'][6:8] + '/' + \
                p['fecha'][4:6] + '/' + p['fecha'][0:4]
            sql = """
            DECLARE @pRes INT
            DECLARE @pMensaje NVARCHAR(250)
            DECLARE @pIdCpte INT
            
            set nocount on; EXEC wsSysMobileSPPedidosV_MV_CPTE '{}','{}','{}',0,@pRes OUTPUT, @pMensaje OUTPUT,@pIdCpte OUTPUT

            SELECT @pRes as pRes, @pMensaje as pMensaje, @pIdCpte pIdCpte
            """.format(p['idcliente'], p['idvendedor'], fecha)

            # print(sql)
            # return sql
            cursor.execute(sql)
            resultado = cursor.fetchall()

            pRes = resultado[0][0]
            pMensaje = resultado[0][1]
            pIdCpte = resultado[0][2]

            with conn.cursor() as cursor:
                if pRes == 11:
                    #db_insertaCpteInsumos(p['detallePedido'], pIdCpte)
                    # for pd in lista:
                    for pItem in p['detallepedido']:
                        # return pItem;
                        sql = """
                        DECLARE @pRes INT
                        DECLARE @pMensaje NVARCHAR(250)
                        DECLARE @pIdCpte INT 
                        
                        EXEC wsSysMobileSPPedidosV_MV_CPTEINSUMOS {},'{}',{},{},{},@pRes OUTPUT, @pMensaje OUTPUT,@pIdCpte OUTPUT

                        SELECT @pRes as pRes, @pMensaje as pMensaje, @pIdCpte pIdCpte
                        """.format(pIdCpte, pItem['idarticulo'], pItem['cantidad'], pItem['importeunitario'], pItem['porcdto'])

                        # print(sql)
                        cursor.execute(sql)

                else:
                    # print(sql)
                    print('Error : ' + pMensaje)
                    return False

    return True


def db_getCptes(tc, vdor, fhd, fhh, idcliente):

    fechaD = fhd[6:8] + '/' + fhd[4:6] + '/' + fhd[0:4]
    fechaH = fhh[6:8] + '/' + fhh[4:6] + '/' + fhh[0:4]
    lista = []

    try:
        # Obtener cptes de V_MV_CPTE
        with conn.cursor() as cursor:
            if idcliente != '-1':
                if tc == 'CB':
                    cursor.execute("SELECT top 30 tc,idcomprobante,nombre,importe,fecha,cuenta FROM V_MV_CPTE WHERE (TC='CB' OR TC='CBFP') AND ltrim(idvendedor)='{}' AND cuenta='{}' and (fecha>='{}' and fecha<='{}') order by fecha desc".format(
                        vdor, idcliente, fechaD, fechaH))
                else:
                    cursor.execute("SELECT top 30 tc,idcomprobante,nombre,importe,fecha,cuenta FROM V_MV_CPTE WHERE TC='{}' AND ltrim(idvendedor)='{}' AND cuenta='{}' and (fecha>='{}' and fecha<='{}') order by fecha desc".format(
                        tc, vdor, idcliente, fechaD, fechaH))
            else:
                if tc == 'CB':
                    cursor.execute("SELECT top 30 tc,idcomprobante,nombre,importe,fecha,cuenta FROM V_MV_CPTE WHERE (TC='CB' OR TC='CBFP') AND ltrim(idvendedor)='{}' AND (fecha>='{}' and fecha<='{}') order by fecha desc".format(
                        vdor, fechaD, fechaH))
                else:
                    cursor.execute("SELECT top 30 tc,idcomprobante,nombre,importe,fecha,cuenta FROM V_MV_CPTE WHERE TC='{}' AND ltrim(idvendedor)='{}' AND (fecha>='{}' and fecha<='{}') order by fecha desc".format(
                        tc, vdor, fechaD, fechaH))
            res = cursor.fetchall()
            cursor.commit()

            for row in res:
                lista.append(
                    {
                        'tc': row.tc,
                        'idcomprobante': row.idcomprobante.strip(),
                        'razon': row.nombre.strip(),
                        'cuenta': row.cuenta.strip(),
                        'totalFinal': float(row.importe),
                        'fecha': row.fecha,
                        'id': 0
                    }
                )
    except Exception as e:
        print("Ocurrió un error al obtener los clientes: ", e)
    finally:
        return lista


def db_getPedidoDetalle(tc, idcpte):
    lista = []

    try:
        with conn.cursor() as cursor:

            # cursor.execute("SELECT tc,idcomprobante,idarticulo,descripcion,cantidad,importe,total FROM V_MV_CPTEinsumos WHERE tc='{}' AND idcomprobante='{}'".format(tc,idcpte))
            cursor.execute("SELECT a.importe as totalcpte,a.fecha,a.cuenta,a.nombre,a.tc,a.idcomprobante,b.idarticulo,b.descripcion,b.cantidad,b.importe,b.total FROM V_MV_CPTE a "
                           + "LEFT JOIN V_MV_CPTEINSUMOS b on a.tc = b.tc and a.idcomprobante = b.idcomprobante WHERE a.tc='{}' and a.idcomprobante='{}'".format(tc, idcpte))

            res = cursor.fetchall()
            cursor.commit()

            for row in res:
                lista.append(
                    {
                        'tc': row.tc,
                        'idcomprobante': row.idcomprobante.strip(),
                        'fecha': row.fecha,
                        'cuenta': row.cuenta,
                        'nombre': row.nombre.strip(),
                        'idarticulo': row.idarticulo.strip(),
                        'descripcion': row.descripcion.strip(),
                        'cantidad': float(row.cantidad),
                        'importe': float(row.importe),
                        'total': float(row.total),
                        'totalcpte': float(row.totalcpte)
                    }
                )
    except Exception as e:
        print("Ocurrió un error al obtener los clientes: ", e)
    finally:
        return lista


def db_getStock(id):

    lista = []

    real = _getStock(id, 1)
    lista.append(
        {'idDeposito': '@REAL', 'stock': real}
    )

    comprometido = _getStock(id, 2)
    lista.append(
        {'idDeposito': '@COMPROMETIDO', 'stock': comprometido}
    )

    lista.append(
        {'idDeposito': '@SUGERIDO', 'stock': (int(real) - int(comprometido))}
    )

    deposito = ''
    stock = 0

    with conn.cursor() as cursor:
        # cursor.execute("select a.idArticulo,a.idDeposito,a.stock,b.descripcion from wsSysMobileStockArticulosDepositos a left join wsSysMobileDepositos b "
        # + " on a.idDeposito = b.idDeposito where a.idarticulo='{}'".format(id))

        cursor.execute("SELECT ltrim(a.idArticulo) as idArticulo, isnull(ltrim(a.DEPOSITO),'') as idDeposito, ISNULL(sum(a.stock),0) as stock, isnull(b.descripcion,'') as descripcion "
                       + " FROM Stk_Ma_Articulos a LEFT JOIN V_TA_DEPOSITO b ON a.DEPOSITO = b.idDeposito WHERE LTRIM(a.idarticulo)='{}' group by a.IDARTICULO,a.DEPOSITO, b.Descripcion".format(id))

        res = cursor.fetchall()
        cursor.commit()

        for row in res:
            if row.stock is None:
                stock = 0
                deposito = ''
            else:
                stock = float(row.stock)
                deposito = row.idDeposito + ' ' + row.descripcion

            lista.append({'idDeposito': deposito, 'stock': stock})

    return lista


def _getStock(id, opcion):

    with conn.cursor() as cursor:
        if opcion == 1:
            # REAL
            # cursor.execute("SELECT isnull(stock,0) as stock FROM wsSysMobileStockArticulos WHERE IDARTICULO = '{}'".format(id))
            cursor.execute(
                "SELECT isnull(sum(STOCK),0) as stock FROM STK_MA_ARTICULOS WHERE ltrim(IDARTICULO) = '{}' group by idarticulo".format(id))
        elif opcion == 2:
            # COMPROMETIDO
            cursor.execute(
                "SELECT isnull(stock,0) as stock FROM wsSysMobileStockComprometidoArticulos WHERE IDARTICULO = '{}'".format(id))

        res = cursor.fetchall()

        cursor.commit()

        for row in res:
            if row.stock is None:
                return 0
            else:
                return float(row.stock)
        else:
            return 0


def db_printPriceList():

    lista = []

    try:
        with conn.cursor() as cursor:

            cursor.execute("select ltrim(a.idarticulo) as idarticulo, ltrim(a.descripcion) as descripcion, a.PRECIO1,a.PRECIO2,a.PRECIO3,a.PRECIO4,a.PRECIO5,a.PRECIO6,a.PRECIO7,a.PRECIO8,ltrim(b.IdRubro) as idrubro,ltrim(b.Descripcion) as rubro, ltrim(c.idfamilia) as idfamilia, ltrim(c.Descripcion) as familia "
                           + " from V_MA_ARTICULOS a LEFT JOIN V_TA_Rubros b on a.IDRUBRO = b.IdRubro LEFT JOIN V_TA_FAMILIAS c ON a.IdFamilia = c.IdFamilia "
                           + " WHERE a.SUSPENDIDO = 0 and a.SuspendidoV = 0 and b.IdRubro<>'' ORDER BY b.IdRubro,c.IdFamilia,a.IDARTICULO ")

            res = cursor.fetchall()
            cursor.commit()

            for row in res:
                lista.append(
                    {
                        'idarticulo': row.idarticulo,
                        'descripcion': row.descripcion,
                        'precio1': float(row.PRECIO1),
                        'precio2': float(row.PRECIO2),
                        'precio3': float(row.PRECIO3),
                        'precio4': float(row.PRECIO4),
                        'precio5': float(row.PRECIO5),
                        'precio6': float(row.PRECIO6),
                        'precio7': float(row.PRECIO7),
                        'precio8': float(row.PRECIO8),
                        'idrubro': row.idrubro,
                        'rubro': row.rubro,
                        'idfamilia': row.idfamilia,
                        'familia': row.familia
                    }
                )
    except Exception as e:
        print("Ocurrió un error al obtener la lista de precios: ", e)
    finally:
        return lista


def db_save_cobranza(json_cobranzas):

    for datos in json_cobranzas:
        tc = datos['tc']
        fecha = datos['fecha'][8:10] + '/' + \
            datos['fecha'][5:7] + '/' + datos['fecha'][0:4]
        cuenta = datos['cuenta']
        mp = datos['mp']
        importe = datos['importe']
        obs = datos['observacion']
        idvendedor = datos['idvendedor']

        lista = []
        with conn.cursor() as cursorcb:

            sql = """
            DECLARE @pRes INT
            DECLARE @pMensaje NVARCHAR(250)
            
            ALTER TABLE MV_ASIENTOS DISABLE TRIGGER TRG_ValidaFPEF
            set nocount on; EXEC wsSysMobileSetCobranzas '{}','{}','{}','{}',{},'{}','{}',@pRes OUTPUT, @pMensaje OUTPUT
            ALTER TABLE MV_ASIENTOS ENABLE TRIGGER TRG_ValidaFPEF
            SELECT @pRes as pRes, @pMensaje as pMensaje
            """.format(tc, cuenta, idvendedor, fecha, importe, obs, mp)

            cursorcb.execute(sql)
            resultado = cursorcb.fetchall()

            pRes = resultado[0][0]
            pMensaje = resultado[0][1]

            if pRes == 11:
                pass
            else:
                lista.append({'status': 'error', 'message': pMensaje})
                return lista

    lista.append({'status': 'ok', 'message': pMensaje})
    return lista


# def db_getMediosPago():

#     lista = []

#     try:
#         with conn.cursor() as cursor:

#             cursor.execute(
#                 "SELECT b.codigo,b.descripcion FROM TA_CONFIGURACION a LEFT JOIN MA_CUENTAS b on a.valor = b.codigoopcional WHERE a.CLAVE LIKE '%MPMASUTILIZADOS%' AND a.valor<>''")

#             res = cursor.fetchall()
#             cursor.commit()

#             for row in res:
#                 lista.append(
#                     {
#                         'codigo': row.codigo,
#                         'descripcion': row.descripcion,
#                     }
#                 )

#             if lista.__len__() == 0:
#                 lista = db_getCuentaEfectivo()
#     except Exception as e:
#         print("Ocurrió un error al obtener los medios de pago: ", e)
#     finally:
#         return lista


# def db_getCuentaEfectivo():
#     lista = []

#     try:
#         with conn.cursor() as cursor:

#             cursor.execute(
#                 "SELECT b.codigo,b.descripcion FROM TA_CONFIGURACION a LEFT JOIN MA_CUENTAS b on a.valor = b.codigo WHERE a.CLAVE ='CUENTA_CAJA'")

#             res = cursor.fetchall()
#             cursor.commit()

#             for row in res:
#                 lista.append(
#                     {
#                         'codigo': row.codigo,
#                         'descripcion': row.descripcion,
#                     }
#                 )
#     except Exception as e:
#         print("Ocurrió un error al obtener la cuenta efectivo: ", e)
#     finally:
#         return lista


# def db_get_visitas_vendedor(idvdor, dia):
#     lista = []
#     sql = ""

#     dia = (1 if dia == 0 else dia)

#     try:
#         with conn.cursor() as cursor:

#             sql = f"""
#                 SELECT a.cliente, a.observaciones, b.razon_social as nombre,b.calle,b.numero,b.localidad, SUBSTRING(frecuencia,1,1) as lunes,SUBSTRING(frecuencia,2,1) as martes,SUBSTRING(frecuencia,3,1) as miercoles,
#                 SUBSTRING(frecuencia,4,1) as jueves, SUBSTRING(frecuencia,5,1) as viernes,SUBSTRING(frecuencia,6,1) as sabado,SUBSTRING(frecuencia,7,1) as domingo
#                 FROM V_TA_FRECUENCIA_VDOR a LEFT JOIN Vt_Clientes b on a.Cliente = b.CODIGO
#                 WHERE SUBSTRING(frecuencia,{dia},1)=1 and ltrim(a.idVendedor)='{idvdor}'
#             """

#             cursor.execute(sql)

#             res = cursor.fetchall()
#             cursor.commit()

#             for row in res:
#                 lista.append(
#                     {
#                         'cliente': row.cliente,
#                         'nombre': row.nombre,
#                         'observaciones': row.observaciones,
#                         'lunes': row.lunes,
#                         'martes': row.martes,
#                         'miercoles': row.miercoles,
#                         'jueves': row.jueves,
#                         'viernes': row.viernes,
#                         'sabado': row.sabado,
#                         'domingo': row.domingo
#                     }
#                 )
#     except Exception as e:
#         print("Ocurrió un error al obtener las visitas del vendedor: ", e)
#     finally:
#         return lista


# def db_getServicios():
#     lista = []

#     try:
#         with conn.cursor() as cursor:

#             cursor.execute(
#                 "SELECT idtarea as codigo,descripcion from V_TA_TAREAS")

#             res = cursor.fetchall()
#             cursor.commit()

#             for row in res:
#                 lista.append(
#                     {
#                         'codigo': row.codigo,
#                         'descripcion': row.descripcion,
#                     }
#                 )

#             if lista.__len__() == 0:
#                 lista = db_getCuentaEfectivo()
#     except Exception as e:
#         print("Ocurrió un error al obtener los servicios: ", e)
#     finally:
#         return lista


def db_save_tarea(json_tareas):

    for datos in json_tareas:
        fecha = datos['fecha'][8:10] + '/' + \
            datos['fecha'][5:7] + '/' + datos['fecha'][0:4]

        cuenta = datos['cuenta']
        obs = datos['observacion']
        idvendedor = datos['idvendedor']
        firma = datos['firma']
        idtarea = datos['idtarea']

        # Convierto la firma a imagen
        numero = random.randint(0, 1000)
        firma = firma.replace(" ", "+")
        firma_codificada = firma.partition(",")[2]
        imgdata = base64.b64decode(firma_codificada.strip())

        with open(f"imagenFirma{str(numero)}.png", "wb") as fh:
            fh.write(imgdata)

        lista = []
        with conn.cursor() as cursor:

            sql = """
            DECLARE @pRes INT
            DECLARE @pMensaje NVARCHAR(250)
            
            set nocount on; EXEC wsSysMobileSetTareas '{}','{}','{}',{},'{}',@pRes OUTPUT, @pMensaje OUTPUT

            SELECT @pRes as pRes, @pMensaje as pMensaje
            """.format(cuenta, idvendedor, fecha, obs, idtarea)
            cursor.execute(sql)
            resultado = cursor.fetchall()

            pRes = resultado[0][0]
            pMensaje = resultado[0][1]

            if pRes == 11:
                pass
            else:
                lista.append({'status': 'error', 'message': pMensaje})
                return lista

    lista.append({'status': 'ok', 'message': pMensaje})
    return lista
