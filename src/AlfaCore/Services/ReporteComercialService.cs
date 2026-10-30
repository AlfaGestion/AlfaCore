using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Lectura común de libros de compras/ventas y movimientos de sus cuentas comerciales.</summary>
public sealed partial class ReporteComercialService(ISessionService sessions, IAppUserSessionService users,
    IPermissionService permissions, IAppEventService events)
{
    private sealed class ReglaReporteException(string message) : InvalidOperationException(message);
    public const string MenuVentas = "D60RPTWEB";
    private static string Fuente(bool ventas) => ventas ? "dbo.Libro_VentasConFP" : "dbo.Libro_ComprasConFP";
    private static string Terceros(bool ventas) => ventas ? "dbo.VT_CLIENTES" : "dbo.VT_PROVEEDORES";
    private const string Filtro = """
        WHERE (@FechaDesde IS NULL OR l.FECHA >= @FechaDesde)
          AND (@FechaHasta IS NULL OR l.FECHA < DATEADD(day,1,@FechaHasta))
          AND (@Proveedor IS NULL OR l.Cuenta LIKE @Busqueda ESCAPE '\' OR l.RazonSocial LIKE @Busqueda ESCAPE '\')
          AND (@TipoComprobante IS NULL OR LTRIM(RTRIM(l.TC))=@TipoComprobante)
        """;
    private static string Libro(bool ventas) => $"""
        WITH Libro AS (
            SELECT l.FECHA,LTRIM(RTRIM(l.TC)) TC,l.IdComprobante,LTRIM(RTRIM(l.CUENTA)) Cuenta,
                COALESCE(NULLIF(LTRIM(RTRIM(l.CABNOMBRE)),''),LTRIM(RTRIM(l.CUENTA))) RazonSocial,
                l.DESC_MOTIVO,l.USUARIO_LOGEADO,l.IMPORTE,
                CASE WHEN LTRIM(RTRIM(l.TC)) IN ('FP','NCFP','FPC','NCCP')
                    THEN COALESCE(l.IMPORTE_SINIVA,ISNULL(l.LIVA_ImpNetoGrav,0)+ISNULL(l.LIVA_ImpNetoNGrav,0)+ISNULL(l.LIVA_EXENTO,0))
                    ELSE ISNULL(l.LIVA_ImpNetoGrav,0)+ISNULL(l.LIVA_ImpNetoNGrav,0)+ISNULL(l.LIVA_EXENTO,0) END NetoSignado,
                ISNULL(l.LIVA_ImpIVA,0)+ISNULL(l.LIVA_ImpIVARec,0)+ISNULL(l.LIVA_ImpIva2,0)+ISNULL(l.LIVA_ImpIVA3,0)+ISNULL(l.LIVA_ImpIVA4,0) IvaSignado
            FROM {Fuente(ventas)} l
            {FiltroCarteraComercial(ventas,"l.CUENTA", "l.UNEGOCIO", requiereCliente:false)}
            {FiltroProformas(ventas,"l.TC")}
        )
        """;
    private static object Parametros(FiltrosReporteCompras f)
    {
        Validar(f);
        var texto=string.IsNullOrWhiteSpace(f.Proveedor)?null:f.Proveedor.Trim();
        var literal=texto?.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_").Replace("[","\\[");
        return new { FechaDesde=f.FechaDesde?.Date, FechaHasta=f.FechaHasta?.Date, Proveedor=texto,
            Busqueda="%"+literal+"%", TipoComprobante=string.IsNullOrWhiteSpace(f.TipoComprobante)?null:f.TipoComprobante.Trim(),
            CuentaExacta=Normalizar(f.CuentaExacta), Provincia=Normalizar(f.Provincia), f.IncluirProforma,
            Vendedor=string.IsNullOrWhiteSpace(f.Vendedor)?null:f.Vendedor.Trim(), f.SinVendedor,
            Zona=string.IsNullOrWhiteSpace(f.Zona)?null:f.Zona.Trim(), f.SinZona,
            Unidades=f.UnidadesNegocio.Select(x=>x.Trim()).Distinct().ToArray(), SinFiltroUnidad=f.UnidadesNegocio.Length==0,
            Skip=(f.Pagina-1)*f.TamanioPagina, PageSize=f.TamanioPagina };
    }
    private static string? Normalizar(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    internal static bool EsProforma(string tc) => tc.Trim().ToUpperInvariant() is "FP" or "NCFP" or "CBFP";
    private static string FiltroProformas(bool ventas,string tc) => ventas
        ? $"AND (@IncluirProforma=1 OR UPPER(LTRIM(RTRIM(ISNULL({tc},'')))) NOT IN ('FP','NCFP','CBFP'))" : "";

    // Se consulta nuevamente al generar/exportar: ocultar el checkbox no es una autorización.
    private async Task<bool> PuedeIncluirProformaAsync(SqlConnection cn,CancellationToken ct)
    {
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN COL_LENGTH('dbo.TA_USUARIOS','VerProforma') IS NULL THEN 0 ELSE 1 END",cancellationToken:ct)) == 0)
            return false;
        return await cn.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT CONVERT(bit,CASE WHEN EXISTS (
                SELECT 1 FROM dbo.TA_CONFIGURACION
                WHERE UPPER(LTRIM(RTRIM(CLAVE)))='USAFACTURAPROFORMA'
                  AND UPPER(LTRIM(RTRIM(COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''),CONVERT(nvarchar(max),VALORAUX),''))))='SI')
                AND EXISTS (SELECT 1 FROM dbo.TA_USUARIOS
                    WHERE UPPER(LTRIM(RTRIM(NOMBRE)))=UPPER(@Usuario)
                      AND (@Sistema='' OR UPPER(LTRIM(RTRIM(SISTEMA)))=UPPER(@Sistema))
                      AND VerProforma=1)
                THEN 1 ELSE 0 END);
            """,new { Usuario=users.GetCurrentUserName().Trim(),Sistema=users.CurrentUser?.SystemCode?.Trim()??"" },cancellationToken:ct));
    }

    public static void Validar(FiltrosReporteCompras f)
    {
        if(f.UnidadesNegocio.Length>200 || (f.SinVendedor && !string.IsNullOrWhiteSpace(f.Vendedor)) ||
            (f.SinZona && !string.IsNullOrWhiteSpace(f.Zona)) ||
            f.FechaDesde>f.FechaHasta || f.FechaHasta?.Date==DateTime.MaxValue.Date || f.Pagina<1 || f.TamanioPagina is <1 or >500 ||
            (long)(f.Pagina-1)*f.TamanioPagina>int.MaxValue)
            throw new ReglaReporteException("Revisá el período y la paginación del reporte.");
    }
    public Task<ReporteComprasOptionsDto> GetOptionsAsync(bool ventas,CancellationToken ct=default)
        => EjecutarAsync(ventas,"Opciones",async cn=>
        {
            var tipos=(await cn.QueryAsync<string>(new CommandDefinition(
                $"SELECT DISTINCT LTRIM(RTRIM(TC)) FROM {Fuente(ventas)} WHERE ISNULL(TC,'')<>'' ORDER BY 1",cancellationToken:ct))).ToList();
            if(!ventas) return new ReporteComprasOptionsDto { TiposComprobante=tipos,
                UnidadesNegocio=(await cn.QueryAsync<ReporteFiltroOpcion>(new CommandDefinition("SELECT LTRIM(RTRIM(Codigo)) Codigo, MAX(Descripcion) Nombre FROM dbo.V_TA_UnidadNegocio GROUP BY LTRIM(RTRIM(Codigo)) ORDER BY Codigo",cancellationToken:ct))).ToList() };
            var puedeProforma=await PuedeIncluirProformaAsync(cn,ct);
            if (!puedeProforma) tipos.RemoveAll(EsProforma);
            using var r=await cn.QueryMultipleAsync(new CommandDefinition("""
                SELECT LTRIM(RTRIM(IdVendedor)) Codigo, MAX(Nombre) Nombre FROM dbo.V_TA_VENDEDORES
                GROUP BY LTRIM(RTRIM(IdVendedor)) ORDER BY Codigo;
                SELECT LTRIM(RTRIM(Codigo)) Codigo, MAX(Descripcion) Nombre FROM dbo.V_TA_UnidadNegocio
                GROUP BY LTRIM(RTRIM(Codigo)) ORDER BY Codigo;
                SELECT DISTINCT LTRIM(RTRIM(ZONA)) FROM dbo.VT_CLIENTES WHERE NULLIF(LTRIM(RTRIM(ZONA)),'') IS NOT NULL ORDER BY 1;
                SELECT LTRIM(RTRIM(CODIGO)) Codigo, MAX(DESCRIPCION) Nombre FROM dbo.TA_ESTADOS
                GROUP BY LTRIM(RTRIM(CODIGO)) ORDER BY Nombre;
                """,cancellationToken:ct));
            return new ReporteComprasOptionsDto { TiposComprobante=tipos,
                Vendedores=(await r.ReadAsync<ReporteFiltroOpcion>()).ToList(),
                UnidadesNegocio=(await r.ReadAsync<ReporteFiltroOpcion>()).ToList(), Zonas=(await r.ReadAsync<string>()).ToList(),
                Provincias=(await r.ReadAsync<ReporteFiltroOpcion>()).ToList(),PuedeIncluirProforma=puedeProforma };
        },ct);
    public Task<IReadOnlyList<ReporteFiltroOpcion>> BuscarClientesAsync(string texto,CancellationToken ct=default)
        => EjecutarAsync<IReadOnlyList<ReporteFiltroOpcion>>(true,"BuscarClientes",async cn=>
        {
            var termino=texto.Trim();
            if (termino.Length<2) return [];
            var literal=termino.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_").Replace("[","\\[");
            return (await cn.QueryAsync<ReporteFiltroOpcion>(new CommandDefinition("""
                SELECT TOP (25) LTRIM(RTRIM(CTACODIGO)) Codigo,MAX(RAZON_SOCIAL) Nombre
                FROM dbo.VT_CLIENTES
                WHERE LTRIM(RTRIM(CTACODIGO)) LIKE @Busqueda ESCAPE '\'
                    OR RAZON_SOCIAL LIKE @Busqueda ESCAPE '\'
                GROUP BY LTRIM(RTRIM(CTACODIGO))
                ORDER BY CASE WHEN LTRIM(RTRIM(CTACODIGO))=@Texto THEN 0 ELSE 1 END,MAX(RAZON_SOCIAL),LTRIM(RTRIM(CTACODIGO))
                ;
                """,new { Texto=termino,Busqueda="%"+literal+"%" },cancellationToken:ct))).ToList();
        },ct);

    public Task<bool> ValidarFiltrosAsync(bool ventas,FiltrosReporteCompras f,CancellationToken ct=default)
        => EjecutarAsync(ventas,"ValidarFiltros",_=> { Validar(f); return Task.FromResult(true); },ct,f);

    public Task<ResumenComprasDto> GetResumenAsync(bool ventas,FiltrosReporteCompras f,CancellationToken ct=default)
        => EjecutarAsync(ventas,"Resumen",async cn=>
        {
            var filas=(await cn.QueryAsync<ResumenComprasFilaDto>(new CommandDefinition(Libro(ventas)+$"""
                SELECT l.Cuenta,l.RazonSocial,COUNT(*) CantidadComprobantes,ISNULL(SUM(NetoSignado),0) Neto,
                    ISNULL(SUM(IvaSignado),0) Iva,ISNULL(SUM(IMPORTE),0) Total FROM Libro l {Filtro}
                GROUP BY l.Cuenta,l.RazonSocial ORDER BY SUM(IMPORTE) DESC,l.Cuenta,l.RazonSocial;
                """,Parametros(f),commandTimeout:60,cancellationToken:ct))).ToList();
            var total=filas.Sum(x=>x.Total);
            return new ResumenComprasDto { TotalComprado=total,NetoTotal=filas.Sum(x=>x.Neto),IvaTotal=filas.Sum(x=>x.Iva),
                CantidadComprobantes=filas.Sum(x=>x.CantidadComprobantes),CantidadProveedores=filas.Select(x=>x.Cuenta).Distinct().Count(),
                Filas=filas.Select(x=>new ResumenComprasFilaDto { Cuenta=x.Cuenta,RazonSocial=x.RazonSocial,CantidadComprobantes=x.CantidadComprobantes,
                    Neto=x.Neto,Iva=x.Iva,Total=x.Total,Participacion=total==0?0:x.Total/total }).ToList() };
        },ct,f);
    public Task<DetalleComprasResultDto> GetDetalleAsync(bool ventas,FiltrosReporteCompras f,CancellationToken ct=default)
        => EjecutarAsync(ventas,"Detalle",async cn=>
        {
            var sql=Libro(ventas)+$"SELECT COUNT(*) FROM Libro l {Filtro};"+Libro(ventas)+$"""
                , Pagina AS (
                SELECT l.FECHA Fecha,l.TC Tc,l.IdComprobante,l.Cuenta,l.RazonSocial,ISNULL(l.DESC_MOTIVO,'') DescMotivo,
                    l.NetoSignado Neto,l.IvaSignado Iva,ISNULL(l.IMPORTE,0) Total,ISNULL(l.USUARIO_LOGEADO,'') Usuario
                    ,ROW_NUMBER() OVER (ORDER BY l.FECHA DESC,l.IdComprobante DESC,l.TC,l.Cuenta,l.NetoSignado,l.IvaSignado,l.IMPORTE) Fila
                FROM Libro l {Filtro})
                SELECT Fecha,Tc,IdComprobante,Cuenta,RazonSocial,DescMotivo,Neto,Iva,Total,Usuario
                FROM Pagina WHERE Fila>@Skip AND Fila<=CONVERT(bigint,@Skip)+@PageSize ORDER BY Fila;
                """;
            using var r=await cn.QueryMultipleAsync(new CommandDefinition(sql,Parametros(f),commandTimeout:60,cancellationToken:ct));
            return new DetalleComprasResultDto { TotalRegistros=await r.ReadSingleAsync<int>(),Items=(await r.ReadAsync<DetalleComprasFilaDto>()).ToList(),PaginaActual=f.Pagina,TamanioPagina=f.TamanioPagina };
        },ct,f);
    public Task<CuentaCorrienteResultDto> GetCuentaCorrienteAsync(bool ventas,FiltrosReporteCompras f,CancellationToken ct=default)
        => GetCuentaCorrienteDetalladaAsync(ventas,f,ct);
    private async Task<T> EjecutarAsync<T>(bool ventas,string accion,Func<SqlConnection,Task<T>> consulta,CancellationToken ct,FiltrosReporteCompras? filtros=null)
    {
        if(!TenantDataAccessGuard.TryGetAuthorizedSession(sessions,users,out var id))
            throw new ReglaReporteException("Ingresá en la base activa para consultar el reporte.");
        var usuario=users.GetCurrentUserName();
        try
        {
            await using var cn=new SqlConnection(sessions.GetConnectionString());await cn.OpenAsync(ct);
            var clave=ventas?MenuVentas:"D5005";
            if(!await permissions.HasAccessAsync(clave,ct))
            {
                var restringido=await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                    SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.TA_TAREAS WHERE UPPER(LTRIM(RTRIM(USUARIO)))=UPPER(@Usuario)
                        AND UPPER(LTRIM(RTRIM(SISTEMA)))=UPPER(@Sistema) AND ISNULL(TAREA,'')<>'')
                        OR EXISTS(SELECT 1 FROM dbo.ALFACORE_TAREAS_WEB WHERE UPPER(LTRIM(RTRIM(USUARIO)))=UPPER(@Usuario)
                        AND UPPER(LTRIM(RTRIM(SISTEMA)))=UPPER(@Sistema) AND ISNULL(Clave,'')<>'') THEN 1 ELSE 0 END
                    """,new { Usuario=usuario,Sistema=users.CurrentUser?.SystemCode??"" },cancellationToken:ct));
                if(restringido!=0)throw new ReglaReporteException("Tu usuario no tiene permiso para consultar este reporte.");
            }
            if (ventas && filtros?.IncluirProforma == true && !await PuedeIncluirProformaAsync(cn,ct))
                throw new ReglaReporteException("La configuración o tu usuario no permiten incluir proformas. Desmarcá la opción y volvé a generar el reporte.");
            var resultado=await consulta(cn);
            if(!TenantDataAccessGuard.TryGetAuthorizedSession(sessions,users,out var actual)||id!=actual||usuario!=users.GetCurrentUserName())
                throw new ReglaReporteException("Cambió la sesión. Volvé a generar el reporte en la base activa.");
            return resultado;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(ReglaReporteException){throw;}
        catch(Exception ex)
        {
            var codigo=await events.LogErrorAsync(ventas?"ReporteVentas":"ReporteCompras",accion,ex,"No se pudo generar el reporte.",ct:ct);
            throw new AppUserFacingException("No se pudo generar el reporte.",codigo,ex);
        }
    }
}
