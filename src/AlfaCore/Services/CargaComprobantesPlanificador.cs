using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Plan explícito del primer circuito comercial: un NP/RM, artículos simples y cuenta corriente.</summary>
public sealed class CargaComprobantesPlanificador(CargaComprobantesRepository repository)
{
    public async Task<CargaComprobantesPlanGrabacion> CrearAsync(SqlConnection cn, CargaComprobantesContexto contexto,
        int origenId, int talonario, IReadOnlyList<CargaComprobantesCantidad> cantidades, DateTime fecha,
        CancellationToken ct, SqlTransaction? tx = null, CargaComprobantesDirecta? directa = null)
    {
        if (directa is null)
        {
            var ids = CargaComprobantesConsolidacion.Identidades(origenId, cantidades);
            if (ids.Length > 1)
            {
                if(await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                    SELECT CASE WHEN OBJECT_ID('dbo.ALFACORE_VENTAS_CARGA_ORIGEN_LINEA','U') IS NOT NULL
                        AND CHARINDEX('ALFACORE_VENTAS_CARGA_ORIGEN_LINEA',UPPER(OBJECT_DEFINITION(OBJECT_ID('dbo.VT_V_MV_INSUMOS_APLICACION'))))>0
                        THEN 1 ELSE 0 END;
                    """,transaction:tx,cancellationToken:ct))!=1)
                    throw new CargaComprobantesReglaException("Ejecutá las actualizaciones de base para habilitar varios orígenes con pendientes correctos por renglón.");
                var cabeceras = await cn.QueryAsync<string>(new CommandDefinition("""
                    SELECT DISTINCT CONVERT(nvarchar(10),ISNULL(CLASEPRECIO,0))+'|'+ISNULL(LTRIM(RTRIM(IdVendedor)),'')+'|'+
                        CONVERT(nvarchar(10),ISNULL(SucursalCuenta,0)) FROM dbo.V_MV_CPTE WHERE ID IN @Ids;
                    """, new { Ids=ids },tx,cancellationToken:ct));
                if(cabeceras.Count()!=1)
                    throw new CargaComprobantesReglaException("Los orígenes deben compartir clase de precio, vendedor y sucursal del cliente.");
                var partes = new List<CargaComprobantesPlanGrabacion>();
                foreach (var id in ids.OrderBy(x => x == origenId ? 0 : 1).ThenBy(x => x))
                {
                    var seleccion = cantidades.Where(x => (x.OrigenId == 0 ? origenId : x.OrigenId) == id)
                        .Select(x => new CargaComprobantesCantidad { LineaId = x.LineaId, Cantidad = x.Cantidad,
                            PrecioUnitarioNeto = x.PrecioUnitarioNeto, Descuento = x.Descuento }).ToList();
                    partes.Add(await CrearAsync(cn, contexto, id, talonario, seleccion, fecha, ct, tx));
                }
                contexto = await repository.GetContextoAsync(cn, contexto.Usuario, ct, tx, incluirCatalogos: false);
                return CargaComprobantesConsolidacion.UnirPlanes(partes, contexto, cantidades);
            }
        }
        // Releer también configuración/alcance en la transacción final; no conservar defaults anteriores al CAE.
        contexto = await repository.GetContextoAsync(cn, contexto.Usuario, ct, tx, incluirCatalogos: false);
        var carga = directa is null ? default : await new CargaComprobantesDirectaPreparador().PrepararAsync(cn, contexto, directa, ct, tx);
        var origen = directa is null ? await repository.GetOrigenAsync(cn, contexto, origenId, ct, tx) : carga.Origen;
        var tcDestino = directa?.Tc ?? "FC";
        var electronico = CargaComprobantesTipos.EsElectronico(tcDestino);
        var generaAsiento = CargaComprobantesTipos.GeneraAsiento(tcDestino, contexto.ProformaSinRegistracion);
        var asociado = directa?.FacturaAsociadaId is null ? default : await new CargaComprobantesAsociadoPreparador()
            .PrepararAsync(cn, directa, origen, fecha, ct, tx);
        if (directa is not null)
            cantidades = origen.Lineas.Select(x => new CargaComprobantesCantidad { LineaId = x.Id, Cantidad = x.CantidadOriginal }).ToList();
        else if (cantidades.Any(x => x.PrecioUnitarioNeto.HasValue || x.Descuento.HasValue))
        {
            var politica = await new CargaComprobantesEdicionPrecios().LeerAsync(cn, contexto.Usuario, origen.Cuenta, "FC", ct, tx, origenId);
            foreach (var seleccion in cantidades.Where(x => x.PrecioUnitarioNeto.HasValue || x.Descuento.HasValue))
            {
                var linea = origen.Lineas.SingleOrDefault(x => x.Id == seleccion.LineaId)
                    ?? throw new CargaComprobantesReglaException("El precio editado no pertenece al origen seleccionado.");
                var referencia = linea.PrecioSinIva;
                var tasa = linea.Exento || origen.ExentoIvaArticulos ? 0 : linea.AlicuotaIva == 0 ? contexto.AlicuotaGeneral : linea.AlicuotaIva;
                var (neto, final, dto) = CargaComprobantesEdicionPrecios.Aplicar(new()
                { Articulo = linea.Articulo, Cantidad = seleccion.Cantidad, PrecioUnitarioNeto = seleccion.PrecioUnitarioNeto,
                    Descuento = seleccion.Descuento }, "FC", politica, referencia, linea.Precio, tasa, contexto.Decimales, linea.Descuento);
                linea.PrecioReferenciaNeto = referencia; linea.PrecioSinIva = neto; linea.Precio = final; linea.Descuento = dto;
            }
        }
        // Validación temprana del cálculo; la liquidación definitiva se rehace al conocer la letra (C no discrimina IVA).
        var liquidacion = CargaComprobantesCalculador.Calcular(origen, contexto, cantidades);
        if (origen.TieneDevolucion || (origen.Tc == "RM" && contexto.DescargaStockRemitos != origen.TieneStock))
            throw new CargaComprobantesReglaException("El circuito de stock del remito necesita revisión antes de facturarlo.");
        if (!contexto.EntregaParcial && origen.Lineas.Where(x => x.Pendiente > 0)
            .Any(x => cantidades.SingleOrDefault(c => c.LineaId == x.Id)?.Cantidad != x.Pendiente))
            throw new CargaComprobantesReglaException("Esta base no permite entrega parcial: seleccioná todos los pendientes.");
        var cfg = (await cn.QueryAsync<Config>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))) Clave,
                COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''),CONVERT(nvarchar(max),VALORAUX),'') Valor
            FROM dbo.TA_CONFIGURACION WHERE CLAVE IN
                ('EQU_FC','EQU_NC','EQU_ND','EQU_FP','EQU_NCFP','MOTIVOSTOCKDEFAULT','PERIODO','FECHAOPERDESDE','FECHAOPERHASTA',
                 'DescargaStockxReparto','NoFacturaVencidos','SS_BloqueoActivo');
            """, transaction: tx, cancellationToken: ct))).ToDictionary(x => x.Clave, x => x.Valor, StringComparer.OrdinalIgnoreCase);
        if (cfg.GetValueOrDefault("DESCARGASTOCKXREPARTO") == "SI" || cfg.GetValueOrDefault("NOFACTURAVENCIDOS") == "SI" ||
            cfg.GetValueOrDefault("SS_BLOQUEOACTIVO") == "SI")
            throw new CargaComprobantesReglaException("La grabación de este circuito requiere incorporar sus controles de reparto, deuda vencida o seguridad contable.");
        foreach (var clave in new[] { "FECHAOPERDESDE", "FECHAOPERHASTA" })
        {
            if (!DateTime.TryParse(cfg.GetValueOrDefault(clave), CultureInfo.GetCultureInfo("es-AR"), DateTimeStyles.None, out var limite) ||
                (clave.EndsWith("DESDE") ? fecha.Date < limite.Date : fecha.Date > limite.Date))
                throw new CargaComprobantesReglaException("La fecha de factura está fuera del rango operativo configurado.");
        }
        // La cabecera directa proviene del cliente actual; no se inserta un origen auxiliar.
        var fuenteCabecera = directa is null ? "dbo.V_MV_CPTE" : """
            (SELECT 0 ID,CODIGO CUENTA,idCond_Cpra_Vta IDCOND_CPRA_VTA,IdMotivoVta IDMOTIVOCPRAVTA,
                CONVERT(nvarchar(15),'') CodigoImputacion FROM dbo.VT_CLIENTES WHERE LTRIM(RTRIM(CODIGO))=@ClienteDirecto)
            """;
        var datos = await cn.QuerySingleAsync<Datos>(new CommandDefinition($"""
            SELECT LTRIM(RTRIM(cl.CTACODIGO)) CuentaCliente,LTRIM(RTRIM(cl.IVA)) CondicionIva,
                ISNULL(iva.DESCRIPCION,'') CondicionDescripcion,
                LTRIM(RTRIM(c.IDCOND_CPRA_VTA)) CondicionVenta,LTRIM(RTRIM(c.IDMOTIVOCPRAVTA)) Motivo,
                ISNULL(LTRIM(RTRIM(u.IDCAJA)),'') Caja,cond.CantidadDias Dias,
                ISNULL(LTRIM(RTRIM(c.CodigoImputacion)),'') Imputacion,
                ISNULL(LTRIM(RTRIM(imp.Repuestos)),'') CuentaVentas,ISNULL(LTRIM(RTRIM(imp.DFI_IVARI)),'') CuentaIva,
                ISNULL(LTRIM(RTRIM(imp.Otros)),'') CuentaOtros,
                -- ObtieneCuentaIVADF: V_TA_INPUT_VENTAS, luego TA_CUENTASIVA, luego CuentaImpuestoInterno.
                COALESCE(NULLIF(LTRIM(RTRIM(imp.V_DFI_IMPINT)),''),
                    (SELECT TOP (1) NULLIF(LTRIM(RTRIM(ci.V_DFI_IMPINT)),'') FROM dbo.TA_CUENTASIVA ci),
                    (SELECT TOP (1) NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(50),k.VALOR))),'') FROM dbo.TA_CONFIGURACION k
                        WHERE UPPER(LTRIM(RTRIM(k.CLAVE)))='CUENTAIMPUESTOINTERNO'),'') CuentaInternos,
                CONVERT(bit,ISNULL(cl.BLOQUEO,0)|ISNULL(cl.Dada_De_Baja,0)) Bloqueado,
                CONVERT(bit,ISNULL(cl.ExentoIvaArticulos,0)) Exento,ISNULL(cond.PorcRecargo,0) Recargo,
                ISNULL(cl.Limite_Credito,0) LimiteCredito
            FROM {fuenteCabecera} c
            JOIN dbo.VT_CLIENTES cl ON LTRIM(RTRIM(cl.CODIGO))=LTRIM(RTRIM(c.CUENTA))
            JOIN dbo.TA_USUARIOS u ON UPPER(LTRIM(RTRIM(u.NOMBRE)))=UPPER(@Usuario)
            LEFT JOIN dbo.TA_CONDIVA iva ON LTRIM(RTRIM(iva.CODIGO))=LTRIM(RTRIM(cl.IVA))
            JOIN dbo.V_TA_Cpra_Vta cond ON LTRIM(RTRIM(cond.IDCond_Cpra_Vta))=LTRIM(RTRIM(c.IDCOND_CPRA_VTA))
            LEFT JOIN dbo.V_TA_INPUT_VENTAS imp ON LTRIM(RTRIM(imp.IdMotivoVta))=LTRIM(RTRIM(c.IDMOTIVOCPRAVTA))
                AND LTRIM(RTRIM(imp.CondIva))=LTRIM(RTRIM(cl.IVA))
            WHERE c.ID=@Id;
            """, new { contexto.Usuario, Id = origenId, ClienteDirecto = directa?.Cliente.Trim() }, tx, cancellationToken: ct));
        // PR/NP/RM no van a cuenta corriente: no dependen de la condición de pago ni del límite de crédito.
        // Contado (condición con 0 días): FC/FP/ND se cobran en el momento con la cobranza del POS.
        var contado = generaAsiento && datos.Dias <= 0;
        if (contado && (directa is null || !CargaComprobantesCobranzaContado.Cobra(tcDestino)))
            throw new CargaComprobantesReglaException(CargaComprobantesTipos.EsCredito(tcDestino)
                ? "El cliente es de contado: la nota de crédito con devolución de dinero todavía no está incorporada."
                : "El cliente es de contado: la cobranza se incorporó para facturas, proformas y notas de débito cargadas directamente.");
        if (datos.Bloqueado || datos.Exento != origen.ExentoIvaArticulos || datos.Recargo != 0 ||
            (generaAsiento && !contado && datos.LimiteCredito > 0))
            throw new CargaComprobantesReglaException("El cliente requiere revisar bloqueo, exención, recargo de la condición de venta o límite de crédito.");
        if (!contado && directa?.Pagos.Count > 0)
            throw new CargaComprobantesReglaException("El cliente es de cuenta corriente: quitá los medios de pago (la cobranza se carga aparte).");
        if (generaAsiento && !contado && await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.V_TA_Cpra_VtaDias WHERE LTRIM(RTRIM(IDCOND_CPRA_VTA))=@Condicion;
            """, new { Condicion = datos.CondicionVenta }, tx, cancellationToken: ct)) != 0)
            throw new CargaComprobantesReglaException("La condición de venta tiene vencimientos especiales pendientes de incorporar.");
        var condicionArca = ArcaCodigosAfip.ResolverCondicionIvaReceptor(datos.CondicionDescripcion, datos.CondicionIva);
        var tal = await cn.QuerySingleOrDefaultAsync<TalonarioDatos>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(ISNULL(LETRAS,''))) Letras,A_SUC_DEFAULT PuntoA,B_SUC_DEFAULT PuntoB,C_SUC_DEFAULT PuntoC,X_SUC_DEFAULT PuntoX,
                M_SUC_DEFAULT PuntoM,R_SUC_DEFAULT PuntoR,E_SUC_DEFAULT PuntoE,
                LTRIM(RTRIM(ISNULL(DEBEHABER,''))) DebeHaber,LTRIM(RTRIM(ISNULL(ES,''))) Stock
            FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))=@TipoDestino AND Talonario=@Talonario
                AND LTRIM(RTRIM(SISTEMA))='Ventas' AND LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) IN ('',@Unidad);
            """, new { Talonario = talonario, Unidad = origen.UnidadNegocio, TipoDestino = tcDestino }, tx, cancellationToken: ct));
        // Un talonario que solo admite la letra C es de un emisor monotributista (cada unidad de negocio
        // factura con su CUIT): factura C para cualquier receptor. Si no, A/B según la condición del cliente.
        // Una nota sale con la letra de su factura (el talonario de notas suele admitir A/B/C/X).
        // Los no electrónicos (proformas, presupuestos, pedidos y remitos) usan las letras habilitadas en el talonario.
        var letraAsociado = electronico ? asociado.Fiscal?.Tipo switch { 1 => "A", 6 => "B", 11 => "C", _ => null } : null;
        var letra = !electronico ? CargaComprobantesTipos.LetraNoFiscal(tcDestino, tal?.Letras ?? "", ArcaCodigosAfip.RequiereFacturaA(condicionArca))
            : letraAsociado is not null && tal?.Letras.Contains(letraAsociado, StringComparison.OrdinalIgnoreCase) == true ? letraAsociado
            : tal?.Letras.Equals("C", StringComparison.OrdinalIgnoreCase) == true ? "C"
            : ArcaCodigosAfip.RequiereFacturaA(condicionArca) ? "A" : "B";
        if (electronico && letra is "A" or "B" && !ArcaCodigosAfip.EsCondicionValidaParaLetra(condicionArca, letra))
            throw new CargaComprobantesReglaException("La condición fiscal del cliente no admite una factura A/B en este circuito.");
        if (electronico && letra == "C" && (directa is null || directa.Lineas.Any(x => !x.EsConcepto)))
            throw new CargaComprobantesReglaException("Los comprobantes C se incorporaron para otros conceptos (abonos, servicios); los artículos en C siguen pendientes.");
        if (electronico && asociado.Fiscal is { } asociacion && (asociacion.Tipo switch { 1 => "A", 6 => "B", 11 => "C", _ => "" }) != letra)
            throw new CargaComprobantesReglaException("La letra de la nota no coincide con la factura asociada y la condición actual del cliente.");
        // Sin letras habilitadas, los no electrónicos salen con X (como V_mv_vta).
        if (tal is null || (tal.Letras.IndexOf(letra, StringComparison.OrdinalIgnoreCase) < 0 && (electronico || tal.Letras.Length > 0)))
            throw new CargaComprobantesReglaException($"El talonario elegido no admite la letra {letra} para este cliente y unidad de negocio.");
        var punto = letra switch { "A" => tal.PuntoA, "C" => tal.PuntoC, "X" => tal.PuntoX, "M" => tal.PuntoM, "R" => tal.PuntoR, "E" => tal.PuntoE, _ => tal.PuntoB };
        if (!CargaComprobantesTipos.LetraConNumeracion(letra))
            throw new CargaComprobantesReglaException($"La letra {letra} del talonario no tiene numeración en la tabla de comprobantes.");
        // Stock como V_mv_vta: PR/NP no mueven stock; RM solo con DescargaStockRemitos=SI; el resto según el talonario (E/S).
        var mueveStock = directa is null || tcDestino switch { "PR" or "NP" => false, "RM" => contexto.DescargaStockRemitos, _ => true };
        if (((generaAsiento || directa is null) && tal.DebeHaber != (CargaComprobantesTipos.EsCredito(tcDestino) ? "H" : "D")) ||
            (mueveStock && tal.Stock is not ("E" or "S")) || (directa is null && tal.Stock != "S"))
            throw new CargaComprobantesReglaException("El talonario no tiene un sentido contable/stock compatible con este circuito.");
        // Con factura electrónica por unidad (V_TA_UnidadNegocio.USAEFC) el punto es el de la unidad; si no, PV_EFACTURA.
        var puntoElectronico = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(ISNULL(PDV,''))) FROM dbo.V_TA_UnidadNegocio WHERE LTRIM(RTRIM(Codigo))=@Unidad AND ISNULL(USAEFC,0)<>0;
            """, new { Unidad = origen.UnidadNegocio }, tx, cancellationToken: ct)) is { Length: > 0 } pdvUnidad && int.TryParse(pdvUnidad, out var pdv)
            ? pdv : contexto.PuntoVentaElectronico;
        if (!electronico && punto is null or <= 0 or > 9999)
            throw new CargaComprobantesReglaException($"El talonario no tiene un punto de venta válido para la letra {letra}. Revisalo en la tabla de comprobantes.");
        if ((electronico && punto != puntoElectronico) || punto is null or <= 0 or > 9999)
            throw new CargaComprobantesReglaException($"Elegí el talonario electrónico del punto {puntoElectronico} para la unidad del origen.");
        liquidacion = CargaComprobantesCalculador.Calcular(origen, contexto, cantidades, letra == "C");
        // Una nota de crédito no puede acreditar más que el saldo de su factura (total menos notas anteriores).
        if (CargaComprobantesTipos.EsCredito(tcDestino) && asociado.Fiscal is not null && liquidacion.Total > asociado.Saldo + 0.01m)
            throw new CargaComprobantesReglaException($"La nota de crédito ({liquidacion.Total:N2}) supera el saldo acreditable de la factura ({asociado.Saldo:N2}).");
        var totalFiscal = CargaComprobantesDesgloseFiscal.Crear(liquidacion).Total;
        var cobranza = contado ? await CargaComprobantesCobranzaContado.ValidarAsync(cn, tx, directa!.Pagos, totalFiscal, ct) : null;
        // ObtieneEQCpteCont: NCFP usa su propio código si no hay EQU_NCFP. Sin asiento no hay comprobante contable.
        var tc = !generaAsiento ? "" : cfg.GetValueOrDefault("EQU_" + tcDestino, "") is { Length: > 0 } equ ? equ.Trim()
            : tcDestino == "NCFP" ? "NCFP" : "";
        if (generaAsiento && string.IsNullOrWhiteSpace(tc)) throw new CargaComprobantesReglaException("Falta la equivalencia contable del comprobante.");
        var subdiario = !generaAsiento ? "" : await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(m.Subdiario)) FROM dbo.MA_SUBDIARIOSCOMPROBANTES m
            JOIN dbo.MA_SUBDIARIOS s ON LTRIM(RTRIM(s.CODIGO))=LTRIM(RTRIM(m.Subdiario))
            WHERE LTRIM(RTRIM(m.TC))=@Tc;
            """, new { Tc = tc }, tx, cancellationToken: ct));
        // Alfa Gestión imputa al ejercicio abierto que contiene la fecha (ej. FC de 2026 en PERIODO 20 con la
        // clave PERIODO=19 sin actualizar). Si no hay uno único, se conserva la clave como antes.
        var periodosFecha = !generaAsiento ? new List<string>() : (await cn.QueryAsync<string>(new CommandDefinition("""
            SELECT CONVERT(nvarchar(6),PERIODO) FROM dbo.MV_EJERCICIOS
            WHERE ISNULL(CERRADO,0)=0 AND @Fecha BETWEEN [FECHA DESDE] AND [FECHA HASTA];
            """, new { Fecha = fecha.Date }, tx, cancellationToken: ct))).ToList();
        var periodo = !generaAsiento ? "" : periodosFecha.Count == 1 ? periodosFecha[0].Trim() : cfg.GetValueOrDefault("PERIODO", "");
        // ObtieneTipoRegCont retorna cuatro blancos cuando no hay asociación.
        // VB6 compara con "" sin Trim y conserva ese valor; las FC existentes de esta base lo usan.
        subdiario ??= string.Empty;
        if (generaAsiento && await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.MV_EJERCICIOS WHERE CONVERT(nvarchar(6),PERIODO)=@Periodo
                AND ISNULL(CERRADO,0)=0 AND @Fecha BETWEEN [FECHA DESDE] AND [FECHA HASTA];
            """, new { Periodo = periodo, Fecha = fecha.Date }, tx, cancellationToken: ct)) != 1)
            throw new CargaComprobantesReglaException("Revisá la equivalencia contable, el subdiario y el ejercicio abierto para esta fecha.");
        var cuentaVentas = datos.Imputacion.Length > 0 ? datos.Imputacion : datos.CuentaVentas;
        var hayConceptos = origen.Lineas.Any(x => x.EsConcepto);
        var hayArticulos = origen.Lineas.Any(x => !x.EsConcepto);
        var cuentas = new List<string> { datos.CuentaCliente };
        if (hayArticulos) cuentas.Add(cuentaVentas);
        if (hayConceptos) cuentas.Add(datos.CuentaOtros);
        if (liquidacion.Iva != 0) cuentas.Add(datos.CuentaIva);
        if (generaAsiento && liquidacion.ImpuestosInternos != 0)
        {
            if (datos.CuentaInternos.Length == 0)
                throw new CargaComprobantesReglaException("No se informó la cuenta para imputar los impuestos internos (V_DFI_IMPINT en imputaciones de ventas o CuentaImpuestoInterno).");
            cuentas.Add(datos.CuentaInternos);
        }
        // Sin asiento (PR/NP/RM) no se exigen cuentas contables.
        foreach (var cuenta in generaAsiento ? cuentas.Distinct() : Enumerable.Empty<string>())
            if (cuenta.Length == 0 || await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(CODIGO))=@Cuenta
                    AND TITULO=0 AND BLOQUEO=0 AND Dada_De_Baja=0 AND ISNULL(usaCCostos,0)=0;
                """, new { Cuenta = cuenta }, tx, cancellationToken: ct)) != 1)
                throw new CargaComprobantesReglaException("Falta una cuenta contable habilitada o requiere centros de costos, excluidos de esta etapa.");
        var especiales = directa is not null ? 0 : await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.V_MV_CPTEINSUMOS i
            JOIN dbo.V_MA_ARTICULOS a ON LTRIM(RTRIM(a.IDARTICULO))=LTRIM(RTRIM(i.IDARTICULO))
            WHERE i.TC=@Tc AND i.IDCOMPROBANTE=@Comprobante AND i.IDCOMPLEMENTO=0 AND i.ID IN @Ids
                AND (ISNULL(a.USASERIE,0)<>0 OR ISNULL(a.USALOTE,0)<>0 OR ISNULL(LTRIM(RTRIM(a.CuentaContable)),'')<>''
                    OR ISNULL(LTRIM(RTRIM(i.NRO_SERIE)),'')<>'' OR ISNULL(LTRIM(RTRIM(i.NRO_LOTE)),'')<>''
                    OR ISNULL(i.CANTIDADUD,1)<>1 OR ISNULL(LTRIM(RTRIM(i.IDUNIDAD)),'')=''
                    OR ISNULL(LTRIM(RTRIM(i.IDUNIDADBASE)),'')=''
                    OR LTRIM(RTRIM(i.IDUNIDAD))<>LTRIM(RTRIM(i.IDUNIDADBASE)));
            """, new { origen.Tc, origen.Comprobante, Ids = cantidades.Where(x => x.Cantidad > 0).Select(x => x.LineaId).ToArray() }, tx, cancellationToken: ct));
        if (especiales != 0) throw new CargaComprobantesReglaException("Los artículos seleccionados requieren lotes, series, equivalencias o imputaciones contables particulares pendientes de incorporar.");
        var plan = new CargaComprobantesPlanGrabacion
        {
            Directa = directa, ClasePrecio = directa is null ? 0 : carga.Precios.ClasePrecio,
            ArtExentoANoGrav = liquidacion.NetoNoGravado != 0 && contexto.ArtExentoANoGrav,
            SentidoContable = directa is null ? null : tal.DebeHaber, SignoStock = directa is null ? 0 : tal.Stock == "E" ? 1 : -1,
            Asociado = asociado.Fiscal, AsociadoComprobante = asociado.Comprobante, AsociadoHuella = asociado.Huella,
            OrigenId = origenId, Talonario = talonario, Origen = origen, Liquidacion = liquidacion,
            Cantidades = cantidades.OrderBy(x => x.LineaId).ToList(), Usuario = contexto.Usuario, UnidadNegocio = origen.UnidadNegocio,
            Fecha = fecha.Date, Vencimiento = fecha.Date.AddDays(datos.Dias), CondicionIva = datos.CondicionIva,
            CondicionVenta = datos.CondicionVenta, Motivo = datos.Motivo, Caja = datos.Caja, TcContable = tc,
            Periodo = periodo, Subdiario = subdiario, Letra = letra, PuntoVenta = punto.Value,
            CuentaCliente = datos.CuentaCliente, CuentaVentas = cuentaVentas, CuentaIva = datos.CuentaIva,
            CuentaOtros = hayConceptos ? datos.CuentaOtros : null,
            CuentaInternos = liquidacion.ImpuestosInternos != 0 ? datos.CuentaInternos : null,
            Cobranza = cobranza,
            DescargaStock = directa is not null ? mueveStock : origen.Tc == "NP" || !contexto.DescargaStockRemitos,
            MotivoStock = cfg.GetValueOrDefault("MOTIVOSTOCKDEFAULT", "")
        };
        // Snapshot completo de cabecera/detalle y datos de cliente usados por la grabación.
        // Se guarda solo su hash junto al plan; no se registra el JSON en logs.
        var material = directa is not null ? await cn.QuerySingleAsync<string>(new CommandDefinition("""
            SELECT (SELECT cl.*,
                JSON_QUERY((SELECT a.* FROM dbo.V_MA_ARTICULOS a WHERE LTRIM(RTRIM(a.IDARTICULO)) IN @Codigos
                    ORDER BY a.IDARTICULO FOR JSON PATH,INCLUDE_NULL_VALUES)) Articulos
                FROM dbo.VT_CLIENTES cl WHERE LTRIM(RTRIM(cl.CODIGO))=@Cliente FOR JSON PATH,INCLUDE_NULL_VALUES);
            """, new { Cliente = directa.Cliente.Trim(), Codigos = directa.Lineas.Select(x => x.Articulo.Trim()).ToArray() }, tx, cancellationToken: ct))
            : await cn.QuerySingleAsync<string>(new CommandDefinition("""
            SELECT (SELECT c.*, JSON_QUERY((SELECT i.* FROM dbo.V_MV_CPTEINSUMOS i WHERE i.TC=c.TC
                AND i.IDCOMPROBANTE=c.IDCOMPROBANTE AND i.IDCOMPLEMENTO=0 ORDER BY i.SECUENCIA,i.ID
                FOR JSON PATH,INCLUDE_NULL_VALUES)) Detalle,
                JSON_QUERY((SELECT cl.* FROM dbo.VT_CLIENTES cl WHERE LTRIM(RTRIM(cl.CODIGO))=LTRIM(RTRIM(c.CUENTA))
                    FOR JSON PATH,INCLUDE_NULL_VALUES)) Cliente
                FROM dbo.V_MV_CPTE c WHERE c.ID=@Id FOR JSON PATH,INCLUDE_NULL_VALUES);
            """, new { Id = origenId }, tx, cancellationToken: ct));
        plan.Huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material + JsonSerializer.Serialize(plan))));
        return plan;
    }

    private sealed class Config { public string Clave { get; set; } = ""; public string Valor { get; set; } = ""; }
    private sealed class TalonarioDatos
    {
        public string Letras { get; set; } = "";
        public int? PuntoA { get; set; }
        public int? PuntoB { get; set; }
        public int? PuntoC { get; set; }
        public int? PuntoX { get; set; }
        public int? PuntoM { get; set; }
        public int? PuntoR { get; set; }
        public int? PuntoE { get; set; }
        public string DebeHaber { get; set; } = "";
        public string Stock { get; set; } = "";
    }
    private sealed class Datos
    {
        public string CuentaCliente { get; set; } = "";
        public string CondicionIva { get; set; } = "";
        public string CondicionDescripcion { get; set; } = "";
        public string CondicionVenta { get; set; } = "";
        public string Motivo { get; set; } = "";
        public string Caja { get; set; } = "";
        public string Imputacion { get; set; } = "";
        public string CuentaVentas { get; set; } = "";
        public string CuentaIva { get; set; } = "";
        public string CuentaOtros { get; set; } = "";
        public string CuentaInternos { get; set; } = "";
        public int Dias { get; set; }
        public bool Bloqueado { get; set; }
        public bool Exento { get; set; }
        public decimal Recargo { get; set; }
        public decimal LimiteCredito { get; set; }
    }
}
