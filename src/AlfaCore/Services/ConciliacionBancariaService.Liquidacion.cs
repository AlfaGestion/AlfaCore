using System.Globalization;
using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed record LiquidacionLinea(string Medio,string Cuenta,int Operaciones,decimal Bruto,decimal Comision,decimal IvaComision,decimal Neto);
public sealed record LiquidacionDia(DateTime Fecha,IReadOnlyList<LiquidacionLinea> Lineas)
{
    /// <summary>Créditos del extracto que liquida este día (para conciliarlos contra el renglón del banco).</summary>
    public IReadOnlyList<long> Movimientos { get; init; }=[];
    public int Operaciones=>Lineas.Sum(l=>l.Operaciones);
    public decimal Bruto=>Lineas.Sum(l=>l.Bruto);
    public decimal Comision=>Lineas.Sum(l=>l.Comision);
    public decimal IvaComision=>Lineas.Sum(l=>l.IvaComision);
    public decimal Neto=>Lineas.Sum(l=>l.Neto);
}
public sealed record LiquidacionCuenta(string Codigo,string Descripcion,string Relacionada);
public sealed record LiquidacionPropuesta(long DetalleId,string CuentaBanco,string CuentaComision,string CuentaIva,IReadOnlyList<LiquidacionDia> Dias,
    IReadOnlyDictionary<string,string> CuentasPorMedio,IReadOnlyList<LiquidacionCuenta> CuentasTarjeta,IReadOnlyList<string> Errores,IReadOnlyList<string> Avisos,
    IReadOnlyList<LiquidacionCuenta> CuentasGasto)
{
    public decimal Bruto=>Dias.Sum(d=>d.Bruto);
    public decimal Comision=>Dias.Sum(d=>d.Comision);
    public decimal IvaComision=>Dias.Sum(d=>d.IvaComision);
    public decimal Neto=>Dias.Sum(d=>d.Neto);
    public int Operaciones=>Dias.Sum(d=>d.Operaciones);
}

// Propuesta (solo lectura) del asiento de liquidación del procesador de cobro, por día de acreditación y medio de pago:
//   Debe  Banco ............................ neto acreditado
//   Debe  Gasto por comisión ............... comisión (neto)
//   Debe  IVA crédito fiscal ............... IVA de la comisión (va al Libro de IVA Compras)
//   Haber Cuenta del medio de pago ......... bruto cobrado (la que se debitó al registrar la cobranza)
// No escribe contabilidad: el grabado del asiento es un paso posterior.
public sealed partial class ConciliacionBancariaService
{
    private const string ClaveCuentasMedio="CONCILIACION-NAVE-CUENTAS",ClaveCuentaComision="CONCILIACION-NAVE-CUENTA-COMISION",ClaveCuentaIva="CONCILIACION-NAVE-CUENTA-IVA";

    private sealed record MovCruce(long Id,string Referencia,DateTime Fecha,decimal Importe);
    private sealed record CuentaFila(string Codigo,string Descripcion,string? Relacionada);

    public Task<LiquidacionPropuesta> PrepararLiquidacionAsync(long detalle,long? importacion,CancellationToken ct=default)
        => Ejecutar("LiquidacionPreparar",(cn,token)=>PrepararLiquidacionEnConexionAsync(cn,detalle,importacion,token),ct);

    public Task GuardarCuentaMedioAsync(string medio,string cuenta,CancellationToken ct=default)
        => Ejecutar("LiquidacionCuentaMedio",async(cn,token)=>
        {
            var tarjetas=await CuentasTarjetaAsync(cn,token);
            cuenta=cuenta.Trim();
            if(cuenta.Length>0&&!tarjetas.Any(c=>c.Codigo==cuenta)) throw new InvalidOperationException("Elegí una cuenta de tarjeta activa e imputable del plan de cuentas.");
            var mapa=await LeerMapaMediosAsync(cn,token);
            var clave=ConciliacionGastosBancarios.Normalizar(medio);
            if(clave.Length==0) throw new InvalidOperationException("Medio de pago no válido.");
            if(cuenta.Length==0) mapa.Remove(clave); else mapa[clave]=cuenta;
            await GuardarConfigJsonAsync(cn,ClaveCuentasMedio,mapa,"Cuenta del plan de cuentas de cada medio de pago del procesador (se acredita por el bruto al liquidar)",token);
            return 0;
        },ct);

    /// <summary>Guarda las cuentas del asiento de liquidación para la comisión (gasto) y el IVA de la comisión (crédito fiscal).</summary>
    public Task GuardarCuentasComisionAsync(string comision,string iva,CancellationToken ct=default)
        => Ejecutar("LiquidacionCuentasComision",async(cn,token)=>
        {
            var tc=await LeerComprobanteAsync(cn,null,ClaveLiquidacionComprobante,"CC",token);
            var validas=(await AsientoCatalogoEnConexionAsync(cn,null,token,tc)).Cuentas.Select(c=>c.Codigo).ToHashSet();
            foreach(var (clave,valor,desc) in new[]{(ClaveCuentaComision,comision.Trim(),"Cuenta de gasto por comisión del procesador de cobro (asiento de liquidación)"),
                (ClaveCuentaIva,iva.Trim(),"Cuenta de IVA crédito fiscal del IVA de la comisión del procesador de cobro (asiento de liquidación y Libro de IVA Compras)")})
            {
                if(valor.Length>0&&!validas.Contains(valor)) throw new InvalidOperationException("Elegí una cuenta activa, contable y en pesos del plan de cuentas.");
                await cn.ExecuteAsync(new CommandDefinition("""
                    IF EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WITH(UPDLOCK,HOLDLOCK) WHERE CLAVE=@clave)
                        UPDATE dbo.TA_CONFIGURACION SET VALOR=@valor,FechaHora_Modificacion=GETDATE() WHERE CLAVE=@clave;
                    ELSE INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION) VALUES('CAJA',@clave,@valor,@desc);
                    """,new{clave,valor,desc},cancellationToken:token));
            }
            return 0;
        },ct);

    private static async Task<IReadOnlyList<LiquidacionCuenta>> CuentasTarjetaAsync(SqlConnection cn,CancellationToken ct)
        => (await cn.QueryAsync<CuentaFila>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(CODIGO)) Codigo,LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion,LTRIM(RTRIM(CuentaPrincipal)) Relacionada
            FROM dbo.MA_CUENTAS WHERE LTRIM(RTRIM(ISNULL(MedioDePago,'')))='TJ' AND ISNULL(TITULO,0)=0 AND ISNULL(Dada_De_Baja,0)=0 ORDER BY CODIGO;
            """,cancellationToken:ct))).Select(c=>new LiquidacionCuenta(c.Codigo,c.Descripcion,c.Relacionada??"")).ToList();

    private static async Task<Dictionary<string,string>> LeerMapaMediosAsync(SqlConnection cn,CancellationToken ct)
    {
        var json=await cn.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT CONVERT(nvarchar(max),ValorAux) FROM dbo.TA_CONFIGURACION WHERE CLAVE=@c",new{c=ClaveCuentasMedio},cancellationToken:ct));
        if(string.IsNullOrWhiteSpace(json)) return new(StringComparer.Ordinal);
        try { return new(JsonSerializer.Deserialize<Dictionary<string,string>>(json)??[],StringComparer.Ordinal); }
        catch(JsonException) { throw new InvalidOperationException($"La configuración {ClaveCuentasMedio} no tiene un formato válido."); }
    }

    private static Task GuardarConfigJsonAsync(SqlConnection cn,string clave,object valor,string descripcion,CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            IF EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WITH(UPDLOCK,HOLDLOCK) WHERE CLAVE=@clave)
                UPDATE dbo.TA_CONFIGURACION SET VALOR='JSON',ValorAux=@json,FechaHora_Modificacion=GETDATE() WHERE CLAVE=@clave;
            ELSE INSERT dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,ValorAux,DESCRIPCION) VALUES('CAJA',@clave,'JSON',@json,@descripcion);
            """,new{clave,json=JsonSerializer.Serialize(valor),descripcion},cancellationToken:ct));

    /// <summary>Sugiere la cuenta de tarjeta cuyo nombre coincide con el medio de pago (ej. «Visa Débito» → VISA DEBITO). Solo si es única.</summary>
    internal static string? SugerirCuentaMedio(string medio,IEnumerable<LiquidacionCuenta> cuentas)
    {
        var n=ConciliacionGastosBancarios.Normalizar(medio).Replace(" INTERNACIONAL","").Trim();
        if(n.Length==0) return null;
        var m=cuentas.Where(c=>ConciliacionGastosBancarios.Normalizar(c.Descripcion)==n).ToList();
        return m.Count==1?m[0].Codigo:null;
    }

    private async Task<LiquidacionPropuesta> PrepararLiquidacionEnConexionAsync(SqlConnection cn,long detalle,long? importacion,CancellationToken ct)
    {
        if(importacion is null) throw new InvalidOperationException("Abrí una importación del extracto para preparar la liquidación.");
        var banco=await cn.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT LTRIM(RTRIM(CuentaBanco)) FROM dbo.BK_ImportacionExtracto WHERE ImportacionId=@importacion",new{importacion},cancellationToken:ct));
        if(string.IsNullOrWhiteSpace(banco)) throw new InvalidOperationException("La importación no tiene cuenta bancaria.");
        var texto=await ConfigAsync(cn,"CONCILIACION-NAVE-TEXTO","NAVE",ct);
        if(!int.TryParse(await ConfigAsync(cn,"CONCILIACION-NAVE-PREFIJO","10",ct),NumberStyles.None,CultureInfo.InvariantCulture,out var prefijo)||prefijo is < 4 or > 40)
            throw new InvalidOperationException("Revisá CONCILIACION-NAVE-PREFIJO: debe ser un entero entre 4 y 40.");
        var ops=(await cn.QueryAsync<OpFila>(new CommandDefinition(
            "SELECT Codigo,MedioPago,Bruto,Comision,IvaComision,Neto FROM dbo.BK_DetalleProcesadorOperacion WHERE DetalleId=@detalle",new{detalle},cancellationToken:ct))).ToList();
        if(ops.Count==0) throw new InvalidOperationException("El detalle ya no existe o no tiene operaciones.");
        var movs=(await cn.QueryAsync<MovCruce>(new CommandDefinition("""
            SELECT MovimientoId Id,LTRIM(RTRIM(ReferenciaExterna)) Referencia,FechaMovimiento Fecha,Importe FROM dbo.BK_ExtractoMovimiento
            WHERE DescripcionOriginal LIKE '%'+@texto+'%' AND ISNULL(LTRIM(RTRIM(ReferenciaExterna)),'')<>'' AND ImportacionId=@importacion;
            """,new{texto,importacion},commandTimeout:120,cancellationToken:ct))).ToList();

        string Clave(string codigo)=>codigo.Length<=prefijo?codigo:codigo[..prefijo];
        var porClave=ops.GroupBy(o=>Clave(o.Codigo),StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.ToList(),StringComparer.OrdinalIgnoreCase);
        var errores=new List<string>();var avisos=new List<string>();
        var tarjetas=await CuentasTarjetaAsync(cn,ct);
        var mapa=await LeerMapaMediosAsync(cn,ct);

        string CuentaDe(string medio)
        {
            var clave=ConciliacionGastosBancarios.Normalizar(medio);
            return mapa.TryGetValue(clave,out var c)?c:SugerirCuentaMedio(medio,tarjetas)??"";
        }

        // Solo se liquida lo que el banco acreditó y coincide al centavo con el neto del detalle; el pago con devolución se anula solo.
        var filas=new List<(DateTime Fecha,OpFila Op)>();
        var movsDia=new Dictionary<DateTime,List<long>>();
        var noCoinciden=0;
        foreach(var g in movs.GroupBy(m=>m.Referencia,StringComparer.OrdinalIgnoreCase))
        {
            if(!porClave.TryGetValue(g.Key,out var candidatas)) continue;
            var suma=g.Sum(m=>m.Importe);
            if(suma!=candidatas.Sum(o=>o.Neto)){noCoinciden++;continue;}
            if(suma==0) continue;
            var fecha=g.Where(m=>m.Importe>0).Select(m=>m.Fecha).DefaultIfEmpty(g.Min(m=>m.Fecha)).Min().Date;
            foreach(var o in candidatas) filas.Add((fecha,o));
            if(!movsDia.TryGetValue(fecha,out var idsDia)) movsDia[fecha]=idsDia=[];
            idsDia.AddRange(g.Select(m=>m.Id));
        }
        if(noCoinciden>0) avisos.Add($"{noCoinciden} operación(es) no coinciden con el neto del banco y no se incluyen en la liquidación.");

        var dias=filas.GroupBy(f=>f.Fecha).OrderBy(g=>g.Key).Select(d=>new LiquidacionDia(d.Key,
            d.GroupBy(f=>string.IsNullOrWhiteSpace(f.Op.MedioPago)?"(sin medio)":f.Op.MedioPago).OrderBy(m=>m.Key,StringComparer.OrdinalIgnoreCase)
             .Select(m=>new LiquidacionLinea(m.Key,CuentaDe(m.Key),m.Count(),m.Sum(f=>f.Op.Bruto),m.Sum(f=>f.Op.Comision),m.Sum(f=>f.Op.IvaComision),m.Sum(f=>f.Op.Neto))).ToList())
            {Movimientos=movsDia[d.Key].Distinct().OrderBy(x=>x).ToList()}).ToList();

        var comision=await ConfigAsync(cn,ClaveCuentaComision,"",ct);
        var cuentaIva=await ConfigAsync(cn,ClaveCuentaIva,"",ct);
        if(comision.Length==0||cuentaIva.Length==0)
        {
            // Sin clave propia, se toman las cuentas del asiento de gastos bancarios (Gastos e IVA crédito fiscal).
            var json=await cn.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT CONVERT(nvarchar(max),ValorAux) FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-GASTOS-BANCARIOS'",cancellationToken:ct));
            if(!string.IsNullOrWhiteSpace(json))
                try
                {
                    var cuentasGastos=JsonSerializer.Deserialize<ConciliacionGastosConfig>(json)?.Cuentas;
                    if(comision.Length==0) comision=cuentasGastos?.GetValueOrDefault("Gastos")?.Trim()??"";
                    if(cuentaIva.Length==0) cuentaIva=cuentasGastos?.GetValueOrDefault("IvaCredito")?.Trim()??"";
                }
                catch(JsonException) { }
        }
        // Cuentas elegibles para comisión e IVA: las mismas que acepta el grabado (activas, contables, en pesos).
        var tc=await LeerComprobanteAsync(cn,null,ClaveLiquidacionComprobante,"CC",ct);
        var catalogoCuentas=(await AsientoCatalogoEnConexionAsync(cn,null,ct,tc)).Cuentas;
        var cuentasGasto=catalogoCuentas.Select(c=>new LiquidacionCuenta(c.Codigo,c.Descripcion,"")).ToList();
        // Sin cuenta configurada se propone una buscando en el plan de cuentas; no se guarda hasta que el usuario la confirme en el panel.
        if(comision.Length==0&&SugerirCuentaComision(catalogoCuentas) is { } sc)
        {
            comision=sc;avisos.Add($"Cuenta de comisión propuesta automáticamente: {sc}. Revisala antes de generar.");
        }
        if(cuentaIva.Length==0&&SugerirCuentaIvaCredito(catalogoCuentas) is { } si)
        {
            cuentaIva=si;avisos.Add($"Cuenta de IVA crédito fiscal propuesta automáticamente: {si}. Revisala antes de generar.");
        }
        var sinCuenta=dias.SelectMany(d=>d.Lineas).Where(l=>l.Cuenta.Length==0).Select(l=>l.Medio).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if(sinCuenta.Count>0) errores.Add($"Falta la cuenta de: {string.Join(", ",sinCuenta)}. Elegila en la tabla para poder generar el asiento.");
        if(comision.Length==0) errores.Add($"Falta la cuenta de gasto para la comisión ({ClaveCuentaComision}).");
        if(cuentaIva.Length==0&&dias.Any(d=>d.IvaComision!=0)) errores.Add($"Falta la cuenta de IVA crédito fiscal para el IVA de la comisión ({ClaveCuentaIva}).");
        // El IVA de la comisión va al Libro de IVA Compras: la alícuota tiene que ser una de las vigentes para que cuadre con ARCA.
        foreach(var d in dias.Where(d=>d.Comision>0))
            if(AlicuotaLibro(d.Comision,d.IvaComision) is null)
                errores.Add($"{d.Fecha:dd/MM/yyyy}: el IVA de la comisión ({d.IvaComision:N2}) no corresponde a una alícuota válida sobre {d.Comision:N2}.");
        foreach(var l in dias.SelectMany(d=>d.Lineas).Where(l=>l.Cuenta.Length>0).Select(l=>l.Cuenta).Distinct())
        {
            var t=tarjetas.FirstOrDefault(c=>c.Codigo==l);
            if(t is not null&&t.Relacionada.Length>0&&t.Relacionada!=banco) avisos.Add($"La cuenta {t.Codigo} {t.Descripcion} tiene como cuenta relacionada {t.Relacionada}, pero el extracto es de {banco}.");
        }
        if(dias.Count==0) errores.Add("No hay créditos del extracto que coincidan con el detalle: no hay nada para liquidar.");
        return new(detalle,banco,comision,cuentaIva,dias,new Dictionary<string,string>(mapa),tarjetas,errores,avisos,cuentasGasto);
    }

    private static string Compacto(string? texto)
        => new string(ConciliacionGastosBancarios.Normalizar(texto??"").Where(char.IsLetterOrDigit).ToArray());

    private static bool EsActivo(ConciliacionAsientoCuenta c)
        => c.Codigo.TrimStart().StartsWith('1')||Compacto(c.Rubro).Contains("ACTIVO");

    /// <summary>IVA crédito fiscal: cuenta del activo cuyo nombre es «I.V.A. C.F.…» / «IVA crédito fiscal…»; no toma retenciones, percepciones ni débito fiscal. La primera por código.</summary>
    internal static string? SugerirCuentaIvaCredito(IEnumerable<ConciliacionAsientoCuenta> cuentas)
        => cuentas.Where(EsActivo).Select(c=>(c.Codigo,N:Compacto(c.Descripcion)))
            .Where(x=>x.N.Contains("IVA")&&(x.N.Contains("IVACF")||x.N.Contains("CREDITOFISCAL"))&&!x.N.Contains("RETENCION")&&!x.N.Contains("PERCEPCION"))
            .OrderBy(x=>x.Codigo,StringComparer.Ordinal).Select(x=>x.Codigo).FirstOrDefault();

    /// <summary>Gasto por comisión: la cuenta «Gastos bancarios» (o comisiones bancarias) del plan; la primera por código.</summary>
    internal static string? SugerirCuentaComision(IEnumerable<ConciliacionAsientoCuenta> cuentas)
        => cuentas.Select(c=>(c.Codigo,N:Compacto(c.Descripcion)))
            .Where(x=>x.N.Contains("GASTOSBANCARIOS")||x.N.Contains("COMISIONESBANCARIAS"))
            .OrderBy(x=>x.Codigo,StringComparer.Ordinal).Select(x=>x.Codigo).FirstOrDefault();

    private static readonly decimal[] AlicuotasIva=[21m,10.5m,27m,5m,2.5m];

    /// <summary>Alícuota de IVA que explica el IVA de la comisión (0 si no hay IVA); null si no coincide con ninguna vigente.</summary>
    internal static decimal? AlicuotaLibro(decimal neto,decimal iva)
    {
        if(iva==0) return 0m;
        if(neto<=0) return null;
        var ratio=iva/neto*100m;
        foreach(var a in AlicuotasIva) if(Math.Abs(ratio-a)<=0.5m) return a;
        return null;
    }
}
