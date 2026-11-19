
from pyafipws.wsaa import WSAA
from pyafipws.wsfev1 import WSFEv1
import datetime
from rich import print

class Afip:

    URL_WSAA = "https://wsaahomo.afip.gov.ar/ws/services/LoginCms?wsdl"
    URL_WSFEv1 = "https://wswhomo.afip.gov.ar/wsfev1/service.asmx?WSDL"

    CERT = None
    PRIVATEKEY = None
    CUIT = None
    production = False

    CACHE = "./cache"

    wsaa = None
    wsfev1 = None

    def __init__(self,crt:str,key:str,cuit:str, production: bool = False):
        self.CUIT = cuit
        self.CERT = crt
        self.PRIVATEKEY = key
        self.production = production

        self.wsaa = WSAA()
        self.wsfev1 = WSFEv1()
        
        if self.production:
            self.URL_WSAA = "https://wsaa.afip.gov.ar/ws/services/LoginCms"
            self.URL_WSFEv1 = "https://servicios1.afip.gov.ar/wsfev1/service.asmx"
        else:
            self.URL_WSAA = "https://wsaahomo.afip.gov.ar/ws/services/LoginCms?wsdl"
            self.URL_WSFEv1 = "https://wswhomo.afip.gov.ar/wsfev1/service.asmx?WSDL"

    def get_next_number(self,tipo_cbte:str,pto_vta:str)->int:
        self.__conect_to_wsfev1()

        last = self.wsfev1.CompUltimoAutorizado(tipo_cbte, pto_vta)
        return int(last) + 1

    def __conect_to_wsfev1(self)->None:
        # obtener ticket de acceso (token y sign):
        ta = self.wsaa.Autenticar("wsfe", self.CERT, self.PRIVATEKEY, wsdl=self.URL_WSAA, cache=self.CACHE, debug=True)
        self.wsfev1.Cuit = self.CUIT
        self.wsfev1.SetTicketAcceso(ta)

        self.wsfev1.Conectar(self.CACHE, self.URL_WSFEv1)

    def generate_electronic_invoice(self,tipo_cbte, registros, cpte_asociado={}):
        """Rutina para emitir facturas electrónicas en PDF c/CAE AFIP Argentina"""
        self.__conect_to_wsfev1()

        # recorrer los registros a facturar, solicitar CAE y generar el PDF:
        for reg in registros:
            hoy = datetime.date.today().strftime("%Y%m%d")
            cbte = Comprobante(tipo_cbte=tipo_cbte, punto_vta=reg["pto_vta"], fecha_cbte=hoy,
                            cbte_nro=reg.get("nro"),
                            tipo_doc=reg.get("tipo_documento", 96), nro_doc=reg["documento"],
                            nombre_cliente=reg["nombre"],
                            domicilio_cliente=reg["domicilio"],
                            fecha_serv_desde=reg.get("periodo_desde"),
                            fecha_serv_hasta=reg.get("periodo_hasta"),
                            fecha_venc_pago=reg.get("venc_pago", None),
                            )
            # SI es NC o ND debe informar comprobante asociado
            if tipo_cbte == 2 or tipo_cbte == 3 or tipo_cbte == 7 or tipo_cbte == 8:
                if cpte_asociado:
                    cbte.cmp_asocs = [cpte_asociado]
            #     cbte.cmp_asocs = {
            #         "tipo": 1,
            #         "pto_vta": 2,
            #         "nro": 3
            #     }

            for item in reg["items"]:
                cbte.agregar_item(ds=item["name"], qty=item.get("qty", 1), precio=float(item.get(
                    "price", 0)), tasa_iva=item.get("iva", 21.), codigo=item.get('code'), umed=item.get('umed', 7))

            ok = cbte.autorizar(self.wsfev1)
            # nro = cbte.encabezado["cbte_nro"]

            if "homo" in self.URL_WSFEv1:
                cbte.encabezado["motivos_obs"] = "Ejemplo Sin validez fiscal"

            return cbte.encabezado


class Comprobante:

    def __init__(self, **kwargs):
        self.encabezado = dict(
            tipo_doc=99, nro_doc=0,
            tipo_cbte=6, cbte_nro=None, punto_vta=4000, fecha_cbte=None,
            imp_total=0.00, imp_tot_conc=0.00, imp_neto=0.00,
            imp_trib=0.00, imp_op_ex=0.00, imp_iva=0.00,
            moneda_id='PES', moneda_ctz=1.000,
            obs="",
            concepto=1, fecha_serv_desde=None, fecha_serv_hasta=None,
            fecha_venc_pago=None,
            nombre_cliente='', domicilio_cliente='',
            localidad='', provincia='',
            pais_dst_cmp=200, id_impositivo='Consumidor Final',
            forma_pago='1',
            obs_generales="",
            obs_comerciales="",
            motivo_obs="", cae="", resultado='', fch_venc_cae=""
        )
        self.encabezado.update(kwargs)
        if self.encabezado['fecha_serv_desde'] or self.encabezado["fecha_serv_hasta"]:
            self.encabezado["concepto"] = 3          # servicios
        self.cmp_asocs = []
        self.items = []
        self.ivas = {}

    def agregar_item(self, ds="Descripcion del producto P0001",
                     qty=1, precio=0, tasa_iva=21., umed=7, codigo="P0001"):
        """Agregar producto / servicio facturado (calculando IVA)"""
        # detalle de artículos:
        item = dict(
            u_mtx=123456, cod_mtx=1234567890123, codigo=codigo, ds=ds,
            qty=qty, umed=umed, bonif=0.00, despacho='', dato_a="",
        )
        subtotal = precio * qty
        if tasa_iva:
            iva_id = {10.5: 4, 0: 3, 21: 5, 27: 6}[tasa_iva]
            item["iva_id"] = iva_id
            # discriminar IVA si es clase A / M
            iva_liq = subtotal * tasa_iva / 100.
            self.agergar_iva(iva_id, subtotal, iva_liq)

            imp_neto = self.encabezado["imp_neto"]
            imp_iva = self.encabezado["imp_iva"]

            self.encabezado["imp_neto"] = float(f"{imp_neto +subtotal:.2f}")
            self.encabezado["imp_iva"] = float(f"{imp_iva + iva_liq:.2f}")

            if self.encabezado["tipo_cbte"] in (1, 2, 3, 4, 5, 34, 39, 51, 52, 53, 54, 60, 64):
                item["precio"] = float(f"{ precio / (1. + tasa_iva/100.):.2f}")
                item["imp_iva"] = float(f"{precio * (tasa_iva/100.):.2f}")
            else:
                # no discriminar IVA si es clase B (importe final iva incluido)
                item["precio"] = float(f"{precio * (1. + tasa_iva/100.):.2f}")
                item["imp_iva"] = None
                subtotal += float(f"{iva_liq:.2f}")
                iva_liq = 0
        else:
            iva_liq = 0

            item["precio"] = float(f"{precio:.2f}")
            item["imp_iva"] = None
            if tasa_iva is None:
                imp_tot_conc = self.encabezado["imp_tot_conc"]
                self.encabezado["imp_tot_conc"] = float(f"{imp_tot_conc +subtotal:.2f}")     # No gravado
            else:
                imp_op_ex = self.encabezado["imp_op_ex"]
                self.encabezado["imp_op_ex"] = float(f"{imp_op_ex + subtotal:.2f}")
                # Exento
        item["importe"] = float(f"{subtotal:.2f}")
        total = self.encabezado["imp_total"]

        self.encabezado["imp_total"] = float(f"{total + subtotal + iva_liq:.2f}")
        self.items.append(item)

        imp_neto = self.encabezado["imp_neto"]
        self.encabezado["imp_neto"] = float(f"{imp_neto:.2f}")

    def agergar_iva(self, iva_id, base_imp, importe):
        iva = self.ivas.setdefault(iva_id, dict(iva_id=iva_id, base_imp=0., importe=0.))

        base_imp_org = iva["base_imp"]
        importe_org = iva["importe"]
        iva["base_imp"] = float(f"{base_imp_org + base_imp:.2f}")
        iva["importe"] = float(f"{importe_org + importe:.2f}")

    def autorizar(self, wsfev1):
        "Prueba de autorización de un comprobante (obtención de CAE)"

        # datos generales del comprobante:

        if not self.encabezado["cbte_nro"]:
            # si no se especifíca nro de comprobante, autonumerar:
            ult = wsfev1.CompUltimoAutorizado(self.encabezado["tipo_cbte"], self.encabezado["punto_vta"])
            self.encabezado["cbte_nro"] = int(ult) + 1
        # print(self.encabezado)

        self.encabezado["cbt_desde"] = self.encabezado["cbte_nro"]
        self.encabezado["cbt_hasta"] = self.encabezado["cbte_nro"]
        wsfev1.CrearFactura(**self.encabezado)

        # agrego un comprobante asociado (solo notas de crédito / débito)
        for cmp_asoc in self.cmp_asocs:
            wsfev1.AgregarCmpAsoc(**cmp_asoc)

        # agrego el subtotal por tasa de IVA (iva_id 5: 21%):
        for iva in self.ivas.values():
            wsfev1.AgregarIva(**iva)

        # llamo al websevice para obtener el CAE:
        wsfev1.CAESolicitar()

        if wsfev1.ErrMsg:
            raise RuntimeError(wsfev1.ErrMsg)

        warnings_messages = []
        for obs in wsfev1.Observaciones:
            # warnings.warn(obs)
            warnings_messages.append(obs)

        # print("errores", warnings_messages)
        self.encabezado["warnings"] = warnings_messages

        # print(wsfev1.Resultado)
        # assert wsfev1.Resultado == "A"    # Aprobado!
        # assert wsfev1.CAE
        # assert wsfev1.Vencimiento
        self.encabezado["resultado"] = wsfev1.Resultado

        if wsfev1.Resultado == "A":
            self.encabezado["cae"] = wsfev1.CAE
            self.encabezado["fch_venc_cae"] = wsfev1.Vencimiento
        else:
            self.encabezado["cae"] = None
            self.encabezado["fch_venc_cae"] = None
        return True

    def generar_pdf(self, fepdf, salida="/tmp/factura.pdf"):

        fepdf.CrearFactura(**self.encabezado)

        # completo campos extra del encabezado:
        ok = fepdf.EstablecerParametro("localidad_cliente", self.encabezado["localidad"])
        ok = fepdf.EstablecerParametro("provincia_cliente", self.encabezado["provincia"])

        # imprimir leyenda "Comprobante Autorizado" (constatar con WSCDC!)
        ok = fepdf.EstablecerParametro("resultado", self.encabezado["resultado"])

        # detalle de artículos:
        for item in self.items:
            fepdf.AgregarDetalleItem(**item)

        # agrego remitos y otros comprobantes asociados:
        for cmp_asoc in self.cmp_asocs:
            fepdf.AgregarCmpAsoc(**cmp_asoc)

        # agrego el subtotal por tasa de IVA (iva_id 5: 21%):
        for iva in self.ivas.values():
            fepdf.AgregarIva(**iva)

        # armar el PDF:
        fepdf.CrearPlantilla(papel="A4", orientacion="portrait")
        fepdf.ProcesarPlantilla(num_copias=1, lineas_max=24, qty_pos='izq')
        fepdf.GenerarPDF(archivo=salida)
        return salida


# if __name__ == '__main__':
    # TODO: leer comprobantes de planilla CSV
    # Ejemplo para facturación masiva por programa:
    # IMPORTANTE: es recomendable indicar el nro de factura (y guardarlo antes)
    # para evitar generar varias facturas distintas para el mismo registro, y
    # poder recuperarlas (reproceso automático) si hay falla de comunicación
    # regs = [{"dni": i, "nombre": "Juan Perez", "domicilio": "Balcarce 50",
    #          "descripcion": "Cuota Social Enero", "precio": 300.00,
    #          "periodo_desde": "20190101", "periodo_hasta": "20190131",
    #          } for i in range(1, 10)]
    # facturar(regs)
