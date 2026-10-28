using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ReporteComercialService
{
    public Task<SaldosVentasResultDto> GetSaldosVentasAsync(FiltrosReporteCompras f, bool controlCredito, CancellationToken ct=default)
        => EjecutarAsync(true,controlCredito?"ControlCredito":"Saldos",
            cn=>LeerSaldosVentasAsync(cn,f,controlCredito,ct),ct,f);

    public Task<SaldosVentasResultDto> GetSaldosComprasAsync(FiltrosReporteCompras f,CancellationToken ct=default)
        => EjecutarAsync(false,"Saldos",cn=>LeerSaldosVentasAsync(cn,f,false,ct,ventas:false),ct);

    // Transacción opcional para pruebas con rollback de límites de crédito. La ruta pública
    // conserva las guardas de sesión/permisos y el registro de errores centralizado.
    internal static async Task<SaldosVentasResultDto> LeerSaldosVentasAsync(SqlConnection cn, FiltrosReporteCompras f,
        bool controlCredito, CancellationToken ct=default, SqlTransaction? transaction=null,bool ventas=true)
    {
        var corte=new FiltrosReporteCompras { FechaDesde=null,FechaHasta=f.FechaHasta??DateTime.Today,
            CuentaExacta=f.CuentaExacta,Provincia=f.Provincia,IncluirProforma=f.IncluirProforma,
            Proveedor=f.Proveedor,Vendedor=ventas?f.Vendedor:null,SinVendedor=ventas&&f.SinVendedor,Zona=ventas?f.Zona:null,SinZona=ventas&&f.SinZona,
            UnidadesNegocio=[..f.UnidadesNegocio],Pagina=f.Pagina,TamanioPagina=f.TamanioPagina };
        var parametros=new DynamicParameters(Parametros(corte));
        parametros.Add("IncluirCero",f.IncluirSaldosCero);parametros.Add("ControlCredito",controlCredito);
        var sql=$"""
            -- Materializar la selección antes de agregar: con lista vacía Dapper expande IN
            -- a una subconsulta, que SQL Server no permite dentro de SUM(CASE ...).
            SELECT LTRIM(RTRIM(a.CUENTA)) Cuenta,
                CONVERT(decimal(19,4),CASE WHEN a.[DEBE-HABER]='{(ventas ? "D" : "H")}' THEN a.IMPORTE ELSE -a.IMPORTE END) Signado,
                CASE WHEN @SinFiltroUnidad=1 OR LTRIM(RTRIM(ISNULL(a.UNEGOCIO,''))) IN @Unidades THEN 1 ELSE 0 END Seleccionado
            INTO #BaseSaldo FROM dbo.MV_ASIENTOS a
            {FiltroCarteraComercial(ventas,"a.CUENTA","a.UNEGOCIO",filtrarUnidad:false)}
            {FiltroProformas(ventas,"a.TC")}
            AND a.EsResumen=0 AND a.FECHA<DATEADD(day,1,@FechaHasta);
            SELECT Cuenta,SUM(Signado) SaldoGlobal,SUM(CASE WHEN Seleccionado=1 THEN Signado ELSE 0 END) Saldo
            INTO #SaldoMovimientos FROM #BaseSaldo GROUP BY Cuenta;

            SELECT p.Cuenta,p.RazonSocial,ISNULL(m.Saldo,0) Saldo,ISNULL(m.SaldoGlobal,0) SaldoGlobal,
                p.LimiteCredito,
                CASE WHEN p.LimiteCredito=0 THEN CONVERT(decimal(38,4),0)
                    ELSE p.LimiteCredito-ISNULL(m.SaldoGlobal,0) END CreditoDisponible,
                CASE WHEN p.LimiteCredito>0 AND ISNULL(m.SaldoGlobal,0)>p.LimiteCredito
                    THEN ISNULL(m.SaldoGlobal,0)-p.LimiteCredito ELSE CONVERT(decimal(38,4),0) END Exceso
            INTO #SaldosVentas FROM (
                SELECT LTRIM(RTRIM(CTACODIGO)) Cuenta,MAX(RAZON_SOCIAL) RazonSocial,
                    {(ventas ? "MAX(CONVERT(decimal(19,4),ISNULL(Limite_Credito,0)))" : "CONVERT(decimal(19,4),0)")} LimiteCredito
                FROM {Terceros(ventas)} p
                WHERE (@CuentaExacta IS NULL OR LTRIM(RTRIM(p.CTACODIGO))=@CuentaExacta)
                    {(ventas ? "AND (@Provincia IS NULL OR LTRIM(RTRIM(p.PROVINCIA))=@Provincia)" : "")}
                    AND (@Vendedor IS NULL OR LTRIM(RTRIM(p.IdVendedor))=@Vendedor)
                    AND (@SinVendedor=0 OR NULLIF(LTRIM(RTRIM(p.IdVendedor)),'') IS NULL)
                    AND (@Zona IS NULL OR LTRIM(RTRIM(p.ZONA))=@Zona)
                    AND (@SinZona=0 OR NULLIF(LTRIM(RTRIM(p.ZONA)),'') IS NULL)
                GROUP BY LTRIM(RTRIM(CTACODIGO))) p
            LEFT JOIN #SaldoMovimientos m ON m.Cuenta=p.Cuenta
            WHERE (@Proveedor IS NULL OR p.Cuenta LIKE @Busqueda ESCAPE '\' OR p.RazonSocial LIKE @Busqueda ESCAPE '\')
                AND (@IncluirCero=1 OR ISNULL(m.Saldo,0)<>0 OR (@ControlCredito=1 AND p.LimiteCredito<>0));

            SELECT COUNT(*) TotalRegistros,ISNULL(SUM(Saldo),0) SaldoTotal,ISNULL(SUM(SaldoGlobal),0) SaldoGlobalTotal,
                ISNULL(SUM(LimiteCredito),0) LimiteTotal,ISNULL(SUM(CreditoDisponible),0) DisponibleTotal,
                ISNULL(SUM(Exceso),0) ExcesoTotal FROM #SaldosVentas;
            WITH Pagina AS (SELECT *,ROW_NUMBER() OVER(ORDER BY Cuenta) Fila FROM #SaldosVentas)
            SELECT * FROM Pagina WHERE Fila>@Skip AND Fila<=CONVERT(bigint,@Skip)+@PageSize ORDER BY Fila;
            DROP TABLE #SaldosVentas; DROP TABLE #SaldoMovimientos; DROP TABLE #BaseSaldo;
            """;
        using var r=await cn.QueryMultipleAsync(new CommandDefinition(sql,parametros,transaction,60,cancellationToken:ct));
        var t=await r.ReadSingleAsync<SaldosVentasResultDto>();
        return new SaldosVentasResultDto { Items=(await r.ReadAsync<SaldoVentasFilaDto>()).ToList(),
            TotalRegistros=t.TotalRegistros,SaldoTotal=t.SaldoTotal,SaldoGlobalTotal=t.SaldoGlobalTotal,
            LimiteTotal=t.LimiteTotal,DisponibleTotal=t.DisponibleTotal,ExcesoTotal=t.ExcesoTotal,
            PaginaActual=f.Pagina,TamanioPagina=f.TamanioPagina };
    }
}
