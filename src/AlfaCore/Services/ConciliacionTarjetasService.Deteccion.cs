using System.Globalization;
using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionTarjetasService
{
    private const string CuentasTarjetasSql="SELECT LTRIM(RTRIM(CODIGO)) Codigo,DESCRIPCION Descripcion,LTRIM(RTRIM(MONEDA)) Moneda FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(MEDIODEPAGO))='TJ' AND ISNULL(TITULO,0)=0 AND ISNULL(Dada_De_Baja,0)=0 ORDER BY DESCRIPCION";

    private async Task DetectarCuenta(SqlConnection c,TarjetasLectura lectura,CancellationToken ct)
    {
        var cuentas=(await c.QueryAsync<ConciliacionCuenta>(Cmd(CuentasTarjetasSql,null,ct))).Where(c=>c.Moneda=="1").ToList();
        var reglas=JsonSerializer.Deserialize<List<Asociacion>>(await Config(c,"TARJETAS-ASOCIACIONES",ct))??[];
        var asociadas=reglas.Where(x=>x.Banco==lectura.Banco&&x.Comercio==lectura.Comercio&&x.Tarjeta==lectura.Tarjeta).ToList();
        var regla=asociadas.Count==1?asociadas[0]:null;
        if(asociadas.Count>1)lectura.Avisos.Add("Hay más de una asociación para este comercio y tarjeta. Se requiere revisar la cuenta.");
        var dias=EnteroIa(await Config(c,"TARJETAS-IA-DIAS",ct),3,0,31);
        if(!decimal.TryParse(await Config(c,"TARJETAS-TOLERANCIA",ct),NumberStyles.Number,CultureInfo.InvariantCulture,out var tolerancia)||tolerancia<0||tolerancia>100)
            throw new InvalidOperationException("Revisá TARJETAS-TOLERANCIA (0 a 100, con punto decimal).");
        var desde=lectura.Detalles.Min(d=>d.Fecha).Date;var hasta=lectura.Detalles.Max(d=>d.Fecha).Date;
        var movimientos=(await c.QueryAsync<TarjetasMovimiento>(Cmd(MovimientosDeteccionSql,
            new{desde=desde.AddDays(-dias),hasta=hasta.AddDays(dias),sucursal="",usuario=users.GetCurrentUserName()},ct))).AsList();
        var completo=movimientos.Count<=10000;
        var candidatas=completo?TarjetasDeteccionCuentas.Evaluar(lectura,cuentas,movimientos,dias,tolerancia,ct):[];
        TarjetasDeteccionCuentas.Sugerir(lectura,cuentas,candidatas,regla?.Cuenta??"",regla?.Sucursal??"",desde,hasta,dias,completo);
    }
}
