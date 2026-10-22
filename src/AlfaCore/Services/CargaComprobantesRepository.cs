using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class CargaComprobantesRepository
{
    public async Task<CargaComprobantesContexto> GetContextoAsync(SqlConnection cn, string usuario, CancellationToken ct,
        SqlTransaction? transaction = null, bool incluirCatalogos = true)
    {
        var contexto = await cn.QuerySingleOrDefaultAsync<CargaComprobantesContexto>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(NOMBRE)) Usuario, LTRIM(RTRIM(ISNULL(UNegocio,''))) UnidadNegocio,
                CONVERT(bit, ISNULL(AdminCentral,0)) AdminCentral,
                CONVERT(bit,ISNULL(V_ModificaPrecio,1)) PuedeEditarPrecio,CONVERT(bit,ISNULL(V_Descuentos,1)) PuedeEditarDescuento
            FROM dbo.TA_USUARIOS WHERE UPPER(LTRIM(RTRIM(NOMBRE))) = UPPER(@Usuario);
            """, new { Usuario = usuario }, transaction, cancellationToken: ct))
            ?? throw new CargaComprobantesReglaException("El usuario actual no está definido en la base activa.");
        var valores = (await cn.QueryAsync<ConfigRow>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))) Clave,
                COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''), CONVERT(nvarchar(max),VALORAUX),'') Valor
            FROM dbo.TA_CONFIGURACION
            WHERE LTRIM(RTRIM(CLAVE)) IN ('EntregaParcialRemito','DescargaStockRemitos','CantidadDecimales','CantidadDecimalesVentas',
                'PreciosFinales','PreciosFinalesNeto','RevalorizaRemitos','PIVA','AjustaRedondeoTotal','PV_EFACTURA',
                'UsaFacturaProforma','RobaIvaProforma','UsaFacturaProformaSinRegistracion','ArtExentoaNoGrav');
            """, transaction: transaction, cancellationToken: ct))).ToDictionary(x => x.Clave, x => x.Valor, StringComparer.OrdinalIgnoreCase);
        contexto.EntregaParcial = valores.GetValueOrDefault("ENTREGAPARCIALREMITO") == "SI";
        contexto.ArtExentoANoGrav = valores.GetValueOrDefault("ARTEXENTOANOGRAV") == "SI";
        contexto.DescargaStockRemitos = valores.GetValueOrDefault("DESCARGASTOCKREMITOS") == "SI";
        contexto.PreciosFinales = valores.GetValueOrDefault("PRECIOSFINALES") == "SI";
        contexto.UsaFacturaProforma = valores.GetValueOrDefault("USAFACTURAPROFORMA") == "SI";
        contexto.RobaIvaProforma = valores.GetValueOrDefault("ROBAIVAPROFORMA") == "SI";
        contexto.ProformaSinRegistracion = valores.GetValueOrDefault("USAFACTURAPROFORMASINREGISTRACION") == "SI";
        if (int.TryParse(valores.GetValueOrDefault("PV_EFACTURA"), out var puntoVenta)) contexto.PuntoVentaElectronico = puntoVenta;
        contexto.PreciosFinalesNeto = valores.GetValueOrDefault("PRECIOSFINALESNETO") == "SI";
        contexto.RevalorizaRemitos = valores.GetValueOrDefault("REVALORIZAREMITOS") == "SI";
        // VtaTot: activo incluso si la clave falta o está vacía; únicamente NO lo desactiva.
        contexto.AjustaRedondeoTotal = !string.Equals(valores.GetValueOrDefault("AJUSTAREDONDEOTOTAL"), "NO", StringComparison.OrdinalIgnoreCase);
        var tasaTexto = valores.GetValueOrDefault("PIVA", "").Replace(',', '.');
        if (decimal.TryParse(tasaTexto, System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out var tasa)) contexto.AlicuotaGeneral = tasa;
        if (!int.TryParse(valores.GetValueOrDefault("CANTIDADDECIMALESVENTAS"), out var decimales) || decimales <= 0)
            int.TryParse(valores.GetValueOrDefault("CANTIDADDECIMALES"), out decimales);
        contexto.Decimales = decimales > 0 ? Math.Clamp(decimales, 1, 4) : 2;
        if (!contexto.AdminCentral && contexto.UnidadNegocio.Length == 0)
            throw new CargaComprobantesReglaException("El usuario debe tener una unidad de negocio para consultar documentos.");
        contexto.Talonarios = (await cn.QueryAsync<CargaComprobantesTalonario>(new CommandDefinition("""
            SELECT Talonario, LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) UnidadNegocio,
                ISNULL(LETRAS,'') Letras, LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion
            FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO))='FC'
                AND (@AdminCentral=1 OR LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) IN ('',@UnidadNegocio))
            ORDER BY Talonario;
            """, new { contexto.AdminCentral, contexto.UnidadNegocio }, transaction, cancellationToken: ct))).ToList();
        if (!incluirCatalogos) return contexto;
        contexto.TalonariosDirecta = (await cn.QueryAsync<CargaComprobantesTalonario>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(CODIGO)) Tc,Talonario,LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) UnidadNegocio,
                ISNULL(LETRAS,'') Letras,LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion
            FROM dbo.V_TA_CPTE WHERE LTRIM(RTRIM(CODIGO)) IN ('FC','NC','ND','FP') AND LTRIM(RTRIM(SISTEMA))='Ventas'
                AND (@AdminCentral=1 OR LTRIM(RTRIM(ISNULL(UNEGOCIO,''))) IN ('',@UnidadNegocio))
            ORDER BY CODIGO,Talonario;
            """, new { contexto.AdminCentral, contexto.UnidadNegocio }, transaction, cancellationToken: ct))).ToList();
        using var filtros = await cn.QueryMultipleAsync(new CommandDefinition("""
            SELECT LTRIM(RTRIM(IdVendedor)) Codigo,LTRIM(RTRIM(Nombre)) Descripcion
            FROM dbo.V_TA_VENDEDORES WHERE LTRIM(RTRIM(IdVendedor))<>'' ORDER BY Nombre,IdVendedor;
            SELECT DISTINCT LTRIM(RTRIM(ZONA)) Codigo,LTRIM(RTRIM(ZONA)) Descripcion
            FROM dbo.VT_CLIENTES WHERE ISNULL(LTRIM(RTRIM(ZONA)),'')<>'' ORDER BY Codigo;
            SELECT LTRIM(RTRIM(Codigo)) Codigo,LTRIM(RTRIM(Descripcion)) Descripcion
            FROM dbo.V_TA_UnidadNegocio WHERE @AdminCentral=1 OR LTRIM(RTRIM(Codigo))=@UnidadNegocio ORDER BY Codigo;
            SELECT LTRIM(RTRIM(IdDeposito)) Codigo,LTRIM(RTRIM(Descripcion)) Descripcion,
                ISNULL(LTRIM(RTRIM(UnidadNegocio)),'') Grupo
            FROM dbo.V_TA_Deposito WHERE @AdminCentral=1 OR ISNULL(LTRIM(RTRIM(UnidadNegocio)),'') IN ('',@UnidadNegocio)
            ORDER BY IdDeposito;
            """, new { contexto.AdminCentral, contexto.UnidadNegocio }, transaction, cancellationToken: ct));
        contexto.Vendedores = (await filtros.ReadAsync<CargaComprobantesOpcionFiltro>()).ToList();
        contexto.Zonas = (await filtros.ReadAsync<CargaComprobantesOpcionFiltro>()).ToList();
        contexto.UnidadesNegocio = (await filtros.ReadAsync<CargaComprobantesOpcionFiltro>()).ToList();
        contexto.Depositos = (await filtros.ReadAsync<CargaComprobantesOpcionFiltro>()).ToList();
        await new CargaComprobantesSeleccionPrecios().CargarCatalogosAsync(cn, contexto, ct, transaction);
        return contexto;
    }

    private const string ListColumns = """
        c.ID Id, LTRIM(RTRIM(c.TC)) Tc, LTRIM(RTRIM(c.IDCOMPROBANTE)) Comprobante,
        c.FECHA Fecha, LTRIM(RTRIM(ISNULL(c.CUENTA,''))) Cuenta, ISNULL(c.NOMBRE,'') Cliente,
        LTRIM(RTRIM(ISNULL(c.UNEGOCIO,''))) UnidadNegocio,
        LTRIM(RTRIM(ISNULL(c.IdDeposito,''))) Deposito, LTRIM(RTRIM(ISNULL(c.IdLista,''))) Lista,
        LTRIM(RTRIM(ISNULL(c.Moneda,''))) Moneda, ISNULL(c.IMPORTE,0) Importe
        """;

    // Los efectos relacionados se consultan solo al abrir un origen, no para
    // cada documento del listado antes de paginar.
    private const string HeaderColumns = ListColumns + "," + """
        CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.V_MV_RESERVAS r WHERE LTRIM(RTRIM(r.TC))=LTRIM(RTRIM(c.TC)) AND LTRIM(RTRIM(r.IDCOMPROBANTE))=LTRIM(RTRIM(c.IDCOMPROBANTE))) THEN 1 ELSE 0 END) TieneReservas,
        CONVERT(bit,CASE WHEN c.FechaPreparacion IS NOT NULL THEN 1 ELSE 0 END) TienePreparacion,
        CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.V_MV_STOCK s WHERE LTRIM(RTRIM(s.TC))=LTRIM(RTRIM(c.TC)) AND LTRIM(RTRIM(s.IDCOMPROBANTE))=LTRIM(RTRIM(c.IDCOMPROBANTE)) AND ISNULL(s.Anulado,0)=0) THEN 1 ELSE 0 END) TieneStock,
        CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.MV_APLICACION a
            JOIN dbo.MV_APLICACION n ON LTRIM(RTRIM(n.TCO_ORIGEN))=LTRIM(RTRIM(a.TC)) AND LTRIM(RTRIM(n.IDComprobante_Origen))=LTRIM(RTRIM(a.IDComprobante))
            WHERE LTRIM(RTRIM(a.TCO_ORIGEN))=LTRIM(RTRIM(c.TC)) AND LTRIM(RTRIM(a.IDComprobante_Origen))=LTRIM(RTRIM(c.IDCOMPROBANTE)) AND LTRIM(RTRIM(n.TC)) IN ('NC','NCFP')) THEN 1 ELSE 0 END) TieneDevolucion,
        CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.V_MV_CPTETAREAS t WHERE LTRIM(RTRIM(t.TC))=LTRIM(RTRIM(c.TC)) AND LTRIM(RTRIM(t.IDCOMPROBANTE))=LTRIM(RTRIM(c.IDCOMPROBANTE)))
            OR EXISTS(SELECT 1 FROM dbo.V_MV_CPTE_OBSERV o WHERE LTRIM(RTRIM(o.TC))=LTRIM(RTRIM(c.TC)) AND LTRIM(RTRIM(o.IDCOMPROBANTE))=LTRIM(RTRIM(c.IDCOMPROBANTE)) AND ISNULL(o.Importe,0)<>0) THEN 1 ELSE 0 END) TieneOtrosDetalles,
        ISNULL(CONVERT(nvarchar(max),c.OBSERVACIONES),'') Observaciones,
        CONVERT(bit,1) DatosCalculoDisponibles, CONVERT(bit,ISNULL(c.ExentoIVAArticulos,0)) ExentoIvaArticulos,
        CONVERT(bit,CASE WHEN ISNULL(c.PorcDescuento1,0)<>0 OR ISNULL(c.PorcDescuento2,0)<>0 OR ISNULL(c.PorcDescuento3,0)<>0
            OR ISNULL(c.PorcDescuento4,0)<>0 OR ISNULL(c.ImpDescuento1,0)<>0 OR ISNULL(c.ImpDescuento2,0)<>0
            OR ISNULL(c.ImpDescuento3,0)<>0 OR ISNULL(c.ImpDescuento4,0)<>0 OR ISNULL(c.PorcRecargo,0)<>0
            OR ISNULL(c.ImpAnticipo,0)<>0 THEN 1 ELSE 0 END) TieneAjustesGlobales,
        CONVERT(bit,CASE WHEN ISNULL(c.RETIBR_Importe,0)<>0 OR ISNULL(c.RETIVA_Importe,0)<>0 OR ISNULL(c.RETGAN_Importe,0)<>0
            OR ISNULL(LTRIM(RTRIM(c.RETIBR_IdRetencion)),'')<>'' OR ISNULL(LTRIM(RTRIM(c.RETIVA_IdRetencion)),'')<>''
            OR ISNULL(LTRIM(RTRIM(c.RETGAN_IdRetencion)),'')<>'' OR EXISTS(SELECT 1 FROM dbo.VT_CLIENTES cl
                WHERE LTRIM(RTRIM(cl.CODIGO))=LTRIM(RTRIM(c.CUENTA)) AND (
                    ISNULL(LTRIM(RTRIM(cl.RETIBR_IdRetencion)),'')<>'' OR ISNULL(LTRIM(RTRIM(cl.RETIBR_IdRetencionOnLine)),'')<>''
                    OR ISNULL(LTRIM(RTRIM(cl.RETIVA_IdRetencion)),'')<>'' OR ISNULL(LTRIM(RTRIM(cl.RETGAN_IdRetencion)),'')<>''))
            THEN 1 ELSE 0 END) TienePercepciones
        """;

    private const string Eligible = """
        c.IDCOMPLEMENTO=0 AND LTRIM(RTRIM(c.TC)) IN ('NP','RM') AND c.ANULADA=0 AND c.FINALIZADA=0
        AND ISNULL(c.BLOQUEADA,0)=0 AND ISNULL(c.CERRADA,0)=0 AND ISNULL(c.EnComodato,0)=0
        AND (@AdminCentral=1 OR LTRIM(RTRIM(c.UNEGOCIO))=@UnidadNegocio)
        """;

    public async Task<PagedResult<CargaComprobantesOrigen>> BuscarAsync(SqlConnection cn,
        CargaComprobantesContexto contexto, CargaComprobantesFiltro filtro, CancellationToken ct)
    {
        if (filtro.FechaDesde?.Date > filtro.FechaHasta?.Date)
            throw new CargaComprobantesReglaException("La fecha desde no puede ser posterior a la fecha hasta.");
        if (filtro.FechaDesde?.Date < new DateTime(1753, 1, 1) || filtro.FechaHasta?.Date < new DateTime(1753, 1, 1))
            throw new CargaComprobantesReglaException("Las fechas deben ser posteriores o iguales al 01/01/1753.");
        if (filtro.FechaHasta?.Date == DateTime.MaxValue.Date)
            throw new CargaComprobantesReglaException("Elegí una fecha hasta anterior al último día del calendario.");
        var page = Math.Max(filtro.PageNumber, 1);
        var size = Math.Clamp(filtro.PageSize, 1, 100);
        var where = Eligible + """
             AND LTRIM(RTRIM(c.TC))=@Tc AND (@Busqueda='' OR c.IDCOMPROBANTE LIKE @Patron ESCAPE '~'
                 OR c.NOMBRE LIKE @Patron ESCAPE '~' OR c.CUENTA LIKE @Patron ESCAPE '~')
             AND (@Desde IS NULL OR c.FECHA>=@Desde) AND (@HastaExclusiva IS NULL OR c.FECHA<@HastaExclusiva)
             AND (@Vendedor='' OR LTRIM(RTRIM(c.IdVendedor))=@Vendedor)
             AND (@UnidadFiltro='' OR LTRIM(RTRIM(c.UNEGOCIO))=@UnidadFiltro)
             AND (@Deposito='' OR LTRIM(RTRIM(c.IdDeposito))=@Deposito)
             AND (@Zona='' OR EXISTS(SELECT 1 FROM dbo.VT_CLIENTES cliente
                WHERE LTRIM(RTRIM(cliente.CODIGO))=LTRIM(RTRIM(c.CUENTA)) AND LTRIM(RTRIM(cliente.ZONA))=@Zona))
            """;
        var search = filtro.Busqueda.Trim();
        var pattern = "%" + search.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
        using var result = await cn.QueryMultipleAsync(new CommandDefinition(
            $"""
            -- Evaluar una sola vez la vista oficial, en lugar de correlacionarla por cada cabecera
            -- y repetir todo el cálculo para contar y paginar.
            SELECT DISTINCT LTRIM(RTRIM(TC)) Tc,LTRIM(RTRIM(IDCOMPROBANTE)) Comprobante
            INTO #CC_Pendientes FROM dbo.VT_V_MV_INSUMOS_SALDOSRM
            WHERE LTRIM(RTRIM(TC))=@Tc AND TC IN (@Tc,@TcUno,@TcDos) AND SALDO>0;
            CREATE UNIQUE CLUSTERED INDEX IX_CC_Pendientes ON #CC_Pendientes(Tc,Comprobante);
            SELECT c.ID INTO #CC_Ids FROM dbo.V_MV_CPTE c JOIN #CC_Pendientes p
                ON p.Tc=LTRIM(RTRIM(c.TC)) AND p.Comprobante=LTRIM(RTRIM(c.IDCOMPROBANTE)) WHERE {where};
            SELECT {ListColumns} FROM dbo.V_MV_CPTE c JOIN #CC_Ids p ON p.ID=c.ID
                ORDER BY c.FECHA DESC,c.ID DESC OFFSET @Skip ROWS FETCH NEXT @Size ROWS ONLY;
            SELECT COUNT(*) FROM #CC_Ids;
            DROP TABLE #CC_Ids; DROP TABLE #CC_Pendientes;
            """,
            new { contexto.AdminCentral, contexto.UnidadNegocio, Tc = filtro.TipoOrigen, TcUno = " " + filtro.TipoOrigen, TcDos = "  " + filtro.TipoOrigen, Busqueda = search,
                Patron = pattern, Desde = filtro.FechaDesde?.Date, HastaExclusiva = filtro.FechaHasta?.Date.AddDays(1),
                Vendedor = filtro.Vendedor.Trim(), Zona = filtro.Zona.Trim(), UnidadFiltro = filtro.UnidadNegocio.Trim(),
                Deposito = filtro.Deposito.Trim(), Skip = (long)(page - 1) * size, Size = size }, commandTimeout: 30, cancellationToken: ct));
        var items = (await result.ReadAsync<CargaComprobantesOrigen>()).ToList();
        return new PagedResult<CargaComprobantesOrigen> { Items = items, Total = await result.ReadSingleAsync<int>(), PageNumber = page, PageSize = size };
    }

    public async Task<CargaComprobantesOrigen> GetOrigenAsync(SqlConnection cn,
        CargaComprobantesContexto contexto, int id, CancellationToken ct, SqlTransaction? transaction = null)
    {
        // Buscar la cabecera por PK antes de consultar el saldo. Correlacionar toda la
        // vista de saldos con HeaderColumns amplía innecesariamente el plan de cabecera.
        // La vista oficial sigue definiendo el pendiente, ahora con TC/número concretos.
        var origen = await cn.QuerySingleOrDefaultAsync<CargaComprobantesOrigen>(new CommandDefinition(
            $"""
            SELECT {HeaderColumns} FROM dbo.V_MV_CPTE c WHERE c.ID=@Id
                AND c.IDCOMPLEMENTO=0 AND LTRIM(RTRIM(c.TC)) IN ('NP','RM') AND c.ANULADA=0 AND c.FINALIZADA=0
                AND ISNULL(c.BLOQUEADA,0)=0 AND ISNULL(c.CERRADA,0)=0 AND ISNULL(c.EnComodato,0)=0
                AND (@AdminCentral=1 OR LTRIM(RTRIM(c.UNEGOCIO))=@UnidadNegocio);
            """,
            new { contexto.AdminCentral, contexto.UnidadNegocio, Id = id }, transaction, cancellationToken: ct))
            ?? throw new CargaComprobantesReglaException("El origen ya no está disponible o pertenece a otra unidad de negocio.");
        // TC tiene ancho 4 y aquí solo puede ser NP/RM: el IN cubre también sus variantes
        // con blancos iniciales, permite usar índices y conserva la comparación normalizada.
        var filtroNumero = origen.Comprobante.Length == 13 ? " AND IDCOMPROBANTE=@Comprobante" : "";
        var saldoParametros = new { origen.Tc, origen.Comprobante, TcUno = " " + origen.Tc, TcDos = "  " + origen.Tc };
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition($"""
            SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.VT_V_MV_INSUMOS_SALDOSRM
                WHERE LTRIM(RTRIM(TC))=@Tc AND TC IN (@Tc,@TcUno,@TcDos)
                    AND LTRIM(RTRIM(IDCOMPROBANTE))=@Comprobante {filtroNumero} AND SALDO>0) THEN 1 ELSE 0 END;
            """, saldoParametros, transaction, cancellationToken: ct)) == 0)
            throw new CargaComprobantesReglaException("El origen ya no está disponible o pertenece a otra unidad de negocio.");
        origen.Lineas = (await cn.QueryAsync<CargaComprobantesLineaOrigen>(new CommandDefinition($"""
            SELECT LTRIM(RTRIM(IDARTICULO)) Articulo,ISNULL(LTRIM(RTRIM(IdDeposito)),'') Deposito,SUM(SALDO) Saldo
            INTO #CC_SaldosDetalle FROM dbo.VT_V_MV_INSUMOS_SALDOSRM
            WHERE LTRIM(RTRIM(TC))=@Tc AND TC IN (@Tc,@TcUno,@TcDos)
                AND LTRIM(RTRIM(IDCOMPROBANTE))=@Comprobante {filtroNumero}
            GROUP BY LTRIM(RTRIM(IDARTICULO)),ISNULL(LTRIM(RTRIM(IdDeposito)),'');
            SELECT i.ID Id, i.SECUENCIA Secuencia, LTRIM(RTRIM(i.IDARTICULO)) Articulo,
                ISNULL(i.DESCRIPCION,'') Descripcion, LTRIM(RTRIM(ISNULL(i.IDUNIDAD,''))) Unidad,
                LTRIM(RTRIM(COALESCE(i.IdDeposito,@Deposito,''))) Deposito,
                CONVERT(decimal(28,8),ISNULL(i.CANTIDAD,0)) CantidadOriginal,
                CONVERT(decimal(28,8),ISNULL(i.CANTIDADUD,0)) CoeficienteUnidad,
                CONVERT(decimal(28,8),CASE WHEN ISNULL(i.CANTIDAD,0)<=0 OR COUNT(*) OVER(PARTITION BY i.IDARTICULO)>1 THEN 0
                    WHEN ISNULL(s.SALDO,0)>ISNULL(i.CANTIDAD,0) THEN ISNULL(i.CANTIDAD,0)
                    ELSE ISNULL(s.SALDO,0) END) Pendiente,
                ISNULL(i.IMPORTE,0) Precio, ISNULL(i.PorcDto,0) Descuento,
                ISNULL(i.IMPORTE_S_IVA,0) PrecioSinIva, ISNULL(i.AlicIVA,0) AlicuotaIva,
                ISNULL(i.AlicIvaRec,0) AlicuotaIvaRecargo, ISNULL(i.IMPUESTOS,0) ImpuestoInternoUnitario,
                ISNULL(i.ITC,0) ItcUnitario, CONVERT(bit,ISNULL(i.EXENTO,0)) Exento,
                ISNULL(i.TotalFinal,0) TotalOriginal,
                COUNT(*) OVER(PARTITION BY i.IDARTICULO) RenglonesArticulo
            FROM dbo.V_MV_CPTEINSUMOS i
            LEFT JOIN #CC_SaldosDetalle s ON s.Articulo=LTRIM(RTRIM(i.IDARTICULO))
                AND s.Deposito=ISNULL(LTRIM(RTRIM(COALESCE(i.IdDeposito,@Deposito))),'')
            WHERE LTRIM(RTRIM(i.TC))=@Tc AND LTRIM(RTRIM(i.IDCOMPROBANTE))=@Comprobante AND i.IDCOMPLEMENTO=0
            ORDER BY i.SECUENCIA,i.ID;
            DROP TABLE #CC_SaldosDetalle;
            """, new { origen.Tc, origen.Comprobante, origen.Deposito, TcUno = " " + origen.Tc, TcDos = "  " + origen.Tc }, transaction, commandTimeout: 30, cancellationToken: ct))).ToList();
        return origen;
    }

    private sealed class ConfigRow
    {
        public string Clave { get; set; } = string.Empty;
        public string Valor { get; set; } = string.Empty;
    }
}
