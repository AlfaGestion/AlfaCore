using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Lectura común de libros de compras/ventas y movimientos de sus cuentas comerciales.</summary>
public sealed class ReporteComercialService(ISessionService sessions, IAppUserSessionService users,
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
        )
        """;
    private static object Parametros(FiltrosReporteCompras f)
    {
        Validar(f);
        var texto=string.IsNullOrWhiteSpace(f.Proveedor)?null:f.Proveedor.Trim();
        var literal=texto?.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_").Replace("[","\\[");
        return new { FechaDesde=f.FechaDesde?.Date, FechaHasta=f.FechaHasta?.Date, Proveedor=texto,
            Busqueda="%"+literal+"%", TipoComprobante=string.IsNullOrWhiteSpace(f.TipoComprobante)?null:f.TipoComprobante.Trim(),
            Skip=(f.Pagina-1)*f.TamanioPagina, PageSize=f.TamanioPagina };
    }
    public static void Validar(FiltrosReporteCompras f)
    {
        if(f.FechaDesde>f.FechaHasta || f.FechaHasta?.Date==DateTime.MaxValue.Date || f.Pagina<1 || f.TamanioPagina is <1 or >500 ||
            (long)(f.Pagina-1)*f.TamanioPagina>int.MaxValue)
            throw new ReglaReporteException("Revisá el período y la paginación del reporte.");
    }
    public Task<ReporteComprasOptionsDto> GetOptionsAsync(bool ventas,CancellationToken ct=default)
        => EjecutarAsync(ventas,"Opciones",async cn=>new ReporteComprasOptionsDto { TiposComprobante=(await cn.QueryAsync<string>(new CommandDefinition(
            $"SELECT DISTINCT LTRIM(RTRIM(TC)) FROM {Fuente(ventas)} WHERE ISNULL(TC,'')<>'' ORDER BY 1",cancellationToken:ct))).ToList() },ct);
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
        },ct);
    public Task<DetalleComprasResultDto> GetDetalleAsync(bool ventas,FiltrosReporteCompras f,CancellationToken ct=default)
        => EjecutarAsync(ventas,"Detalle",async cn=>
        {
            var sql=Libro(ventas)+$"SELECT COUNT(*) FROM Libro l {Filtro};"+Libro(ventas)+$"""
                SELECT l.FECHA Fecha,l.TC Tc,l.IdComprobante,l.Cuenta,l.RazonSocial,ISNULL(l.DESC_MOTIVO,'') DescMotivo,
                    l.NetoSignado Neto,l.IvaSignado Iva,ISNULL(l.IMPORTE,0) Total,ISNULL(l.USUARIO_LOGEADO,'') Usuario
                FROM Libro l {Filtro} ORDER BY l.FECHA DESC,l.IdComprobante DESC,l.TC,l.Cuenta,l.NetoSignado,l.IvaSignado,l.IMPORTE
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;
                """;
            using var r=await cn.QueryMultipleAsync(new CommandDefinition(sql,Parametros(f),commandTimeout:60,cancellationToken:ct));
            return new DetalleComprasResultDto { TotalRegistros=await r.ReadSingleAsync<int>(),Items=(await r.ReadAsync<DetalleComprasFilaDto>()).ToList(),PaginaActual=f.Pagina,TamanioPagina=f.TamanioPagina };
        },ct);
    public Task<CuentaCorrienteResultDto> GetCuentaCorrienteAsync(bool ventas,FiltrosReporteCompras f,CancellationToken ct=default)
        => EjecutarAsync(ventas,"CuentaCorriente",async cn=>
        {
            // EXISTS evita multiplicar asientos si un maestro contiene más de una referencia a la cuenta.
            var origen=$"""
                WITH Movimientos AS (
                    SELECT a.FECHA,a.TC,LTRIM(RTRIM(a.CUENTA)) Cuenta,
                        COALESCE((SELECT MAX(p.RAZON_SOCIAL) FROM {Terceros(ventas)} p WHERE LTRIM(RTRIM(p.CTACODIGO))=LTRIM(RTRIM(a.CUENTA))),a.CUENTA) RazonSocial,
                        ISNULL(a.SUCURSAL,'')+ISNULL(a.NUMERO,'')+ISNULL(a.LETRA,'') IdComprobante,
                        a.[DEBE-HABER] DebeHaber,ISNULL(a.IMPORTE,0) Importe,
                        CASE WHEN a.[DEBE-HABER]='{(ventas?"D":"H")}' THEN ISNULL(a.IMPORTE,0) ELSE -ISNULL(a.IMPORTE,0) END ImporteSignado,
                        a.[NUMERO ASIENTO] Asiento,a.SECUENCIA
                    FROM dbo.MV_ASIENTOS a
                    WHERE EXISTS(SELECT 1 FROM {Terceros(ventas)} p WHERE LTRIM(RTRIM(p.CTACODIGO))=LTRIM(RTRIM(a.CUENTA)))
                )
                """;
            var sql=origen+$"SELECT COUNT(*) TotalRegistros,ISNULL(SUM(l.ImporteSignado),0) SaldoTotal FROM Movimientos l {Filtro};"+origen+$"""
                SELECT l.Cuenta,l.RazonSocial,l.FECHA Fecha,LTRIM(RTRIM(l.TC)) Tc,l.IdComprobante,l.DebeHaber,l.Importe,l.ImporteSignado
                FROM Movimientos l {Filtro} ORDER BY l.FECHA DESC,l.ASIENTO DESC,l.SECUENCIA,l.Cuenta,l.TC,l.IdComprobante
                OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;
                """;
            using var r=await cn.QueryMultipleAsync(new CommandDefinition(sql,Parametros(f),commandTimeout:60,cancellationToken:ct));
            var tot=await r.ReadSingleAsync<CuentaCorrienteResultDto>();
            return new CuentaCorrienteResultDto { TotalRegistros=tot.TotalRegistros,SaldoTotal=tot.SaldoTotal,Items=(await r.ReadAsync<CuentaCorrienteFilaDto>()).ToList(),PaginaActual=f.Pagina,TamanioPagina=f.TamanioPagina };
        },ct);
    private async Task<T> EjecutarAsync<T>(bool ventas,string accion,Func<SqlConnection,Task<T>> consulta,CancellationToken ct)
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
