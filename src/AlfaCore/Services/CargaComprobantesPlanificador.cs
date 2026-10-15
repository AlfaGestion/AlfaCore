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
        // Releer también configuración/alcance en la transacción final; no conservar defaults anteriores al CAE.
        contexto = await repository.GetContextoAsync(cn, contexto.Usuario, ct, tx, incluirCatalogos: false);
        var carga = directa is null ? default : await new CargaComprobantesDirectaPreparador().PrepararAsync(cn, contexto, directa, ct, tx);
        var origen = directa is null ? await repository.GetOrigenAsync(cn, contexto, origenId, ct, tx) : carga.Origen;
        var tcDestino = directa?.Tc ?? "FC";
        var asociado = directa?.FacturaAsociadaId is null ? default : await new CargaComprobantesAsociadoPreparador()
            .PrepararAsync(cn, directa, origen, fecha, ct, tx);
        if (directa is not null)
            cantidades = origen.Lineas.Select(x => new CargaComprobantesCantidad { LineaId = x.Id, Cantidad = x.CantidadOriginal }).ToList();
        var liquidacion = CargaComprobantesCalculador.Calcular(origen, contexto, cantidades);
        if ((tcDestino != "FP" && (liquidacion.NetoNoGravado != 0 || liquidacion.Alicuotas.Count != 1)) ||
            liquidacion.ImpuestosInternos != 0 || liquidacion.Alicuotas.Count > 1)
            throw new CargaComprobantesReglaException("La grabación inicial admite artículos gravados con una alícuota y sin impuestos internos.");
        if (origen.TieneDevolucion || (origen.Tc == "RM" && contexto.DescargaStockRemitos != origen.TieneStock))
            throw new CargaComprobantesReglaException("El circuito de stock del remito necesita revisión antes de facturarlo.");
        if (!contexto.EntregaParcial && origen.Lineas.Where(x => x.Pendiente > 0)
            .Any(x => cantidades.SingleOrDefault(c => c.LineaId == x.Id)?.Cantidad != x.Pendiente))
            throw new CargaComprobantesReglaException("Esta base no permite entrega parcial: seleccioná todos los pendientes.");
        var cfg = (await cn.QueryAsync<Config>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))) Clave,
                COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''),CONVERT(nvarchar(max),VALORAUX),'') Valor
            FROM dbo.TA_CONFIGURACION WHERE CLAVE IN
                ('EQU_FC','EQU_NC','EQU_ND','EQU_FP','MOTIVOSTOCKDEFAULT','PERIODO','FECHAOPERDESDE','FECHAOPERHASTA',
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
        if (datos.Bloqueado || datos.Exento != origen.ExentoIvaArticulos || datos.Dias <= 0 || datos.Recargo != 0 || datos.LimiteCredito > 0)
            throw new CargaComprobantesReglaException("El cliente requiere revisar bloqueo, exención, cobranza de contado, recargo o límite de crédito. La primera grabación admite cuenta corriente simple.");
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.V_TA_Cpra_VtaDias WHERE LTRIM(RTRIM(IDCOND_CPRA_VTA))=@Condicion;
            """, new { Condicion = datos.CondicionVenta }, tx, cancellationToken: ct)) != 0)
            throw new CargaComprobantesReglaException("La condición de venta tiene vencimientos especiales pendientes de incorporar.");
        var condicionArca = ArcaCodigosAfip.ResolverCondicionIvaReceptor(datos.CondicionDescripcion, datos.CondicionIva);
        var letra = tcDestino == "FP" ? "X" : ArcaCodigosAfip.RequiereFacturaA(condicionArca) ? "A" : "B";
        if (tcDestino != "FP" && !ArcaCodigosAfip.EsCondicionValidaParaLetra(condicionArca, letra))
            throw new CargaComprobantesReglaException("La condición fiscal del cliente no admite una factura A/B en este circuito.");
        if (asociado.Fiscal is { } asociacion && (asociacion.Tipo == 1 ? "A" : "B") != letra)
            throw new CargaComprobantesReglaException("La letra de la nota no coincide con la factura asociada y la condición actual del cliente.");
        var tal = await cn.QuerySingleOrDefaultAsync<TalonarioDatos>(new CommandDefinition("""
            SELECT CASE WHEN @Letra='A' THEN A_SUC_DEFAULT WHEN @Letra='X' THEN X_SUC_DEFAULT ELSE B_SUC_DEFAULT END Punto,
                LTRIM(RTRIM(DEBEHABER)) DebeHaber,LTRIM(RTRIM(ES)) Stock
            FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))=@TipoDestino AND Talonario=@Talonario
                AND LTRIM(RTRIM(SISTEMA))='Ventas'
                AND CHARINDEX(@Letra,LETRAS)>0 AND LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) IN ('',@Unidad);
            """, new { Letra = letra, Talonario = talonario, Unidad = origen.UnidadNegocio, TipoDestino = tcDestino }, tx, cancellationToken: ct));
        var punto = tal?.Punto;
        if (tal is null || tal.DebeHaber != (tcDestino == "NC" ? "H" : "D") || tal.Stock is not ("E" or "S") || (directa is null && tal.Stock != "S"))
            throw new CargaComprobantesReglaException("El talonario no tiene un sentido contable/stock compatible con este circuito.");
        if ((tcDestino != "FP" && punto != contexto.PuntoVentaElectronico) || punto is null or <= 0 or > 9999)
            throw new CargaComprobantesReglaException($"Elegí el talonario electrónico del punto {contexto.PuntoVentaElectronico} para la unidad del origen.");
        var tc = cfg.GetValueOrDefault("EQU_" + tcDestino, "");
        if (string.IsNullOrWhiteSpace(tc)) throw new CargaComprobantesReglaException("Falta la equivalencia contable del comprobante.");
        var subdiario = await cn.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(m.Subdiario)) FROM dbo.MA_SUBDIARIOSCOMPROBANTES m
            JOIN dbo.MA_SUBDIARIOS s ON LTRIM(RTRIM(s.CODIGO))=LTRIM(RTRIM(m.Subdiario))
            WHERE LTRIM(RTRIM(m.TC))=@Tc;
            """, new { Tc = tc }, tx, cancellationToken: ct));
        var periodo = cfg.GetValueOrDefault("PERIODO", "");
        // ObtieneTipoRegCont retorna cuatro blancos cuando no hay asociación.
        // VB6 compara con "" sin Trim y conserva ese valor; las FC existentes de esta base lo usan.
        subdiario ??= string.Empty;
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.MV_EJERCICIOS WHERE CONVERT(nvarchar(6),PERIODO)=@Periodo
                AND ISNULL(CERRADO,0)=0 AND @Fecha BETWEEN [FECHA DESDE] AND [FECHA HASTA];
            """, new { Periodo = periodo, Fecha = fecha.Date }, tx, cancellationToken: ct)) != 1)
            throw new CargaComprobantesReglaException("Revisá la equivalencia contable, el subdiario y el ejercicio abierto para esta fecha.");
        var cuentaVentas = datos.Imputacion.Length > 0 ? datos.Imputacion : datos.CuentaVentas;
        foreach (var cuenta in (liquidacion.Iva == 0 ? new[] { datos.CuentaCliente, cuentaVentas } : new[] { datos.CuentaCliente, cuentaVentas, datos.CuentaIva }).Distinct())
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
            SentidoContable = directa is null ? null : tal.DebeHaber, SignoStock = directa is null ? 0 : tal.Stock == "E" ? 1 : -1,
            Asociado = asociado.Fiscal, AsociadoComprobante = asociado.Comprobante, AsociadoHuella = asociado.Huella,
            OrigenId = origenId, Talonario = talonario, Origen = origen, Liquidacion = liquidacion,
            Cantidades = cantidades.OrderBy(x => x.LineaId).ToList(), Usuario = contexto.Usuario, UnidadNegocio = origen.UnidadNegocio,
            Fecha = fecha.Date, Vencimiento = fecha.Date.AddDays(datos.Dias), CondicionIva = datos.CondicionIva,
            CondicionVenta = datos.CondicionVenta, Motivo = datos.Motivo, Caja = datos.Caja, TcContable = tc,
            Periodo = periodo, Subdiario = subdiario, Letra = letra, PuntoVenta = punto.Value,
            CuentaCliente = datos.CuentaCliente, CuentaVentas = cuentaVentas, CuentaIva = datos.CuentaIva,
            DescargaStock = directa is not null || origen.Tc == "NP" || !contexto.DescargaStockRemitos,
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
        public int? Punto { get; set; }
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
        public int Dias { get; set; }
        public bool Bloqueado { get; set; }
        public bool Exento { get; set; }
        public decimal Recargo { get; set; }
        public decimal LimiteCredito { get; set; }
    }
}
