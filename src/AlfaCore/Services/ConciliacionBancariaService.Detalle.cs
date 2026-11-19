using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Detalle de operaciones del procesador de cobro (NAVE, ...): aporta bruto, comisión, IVA y medio de pago
// que el extracto bancario no trae. El emparejamiento es determinístico (código de operación) y no genera asientos.
public sealed partial class ConciliacionBancariaService
{
    private const string FuenteConfigSql="SELECT COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''),CONVERT(nvarchar(max),ValorAux)) FROM dbo.TA_CONFIGURACION WHERE CLAVE=@clave";

    /// <summary>Cantidad de créditos del procesador detectados en una importación (para ofrecer el detalle).</summary>
    public Task<int> CreditosProcesadorAsync(long? importacion,CancellationToken ct=default)
        => Ejecutar("DetalleDetectar",async(cn,token)=>
        {
            var texto=await ConfigAsync(cn,"CONCILIACION-NAVE-TEXTO","NAVE",token);
            return await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM dbo.BK_ExtractoMovimiento WHERE DescripcionOriginal LIKE '%'+@texto+'%'
                  AND (@importacion IS NULL OR ImportacionId=@importacion);
                """,new{texto,importacion},cancellationToken:token));
        },ct);

    public Task<DetalleEmparejamiento> ImportarDetalleAsync(ConciliacionArchivo archivo,long? importacion,CancellationToken ct=default)
        => Ejecutar("DetalleImportar",async(cn,token)=>
        {
            if(archivo.Contenido.Length is 0 or >20*1024*1024) throw new InvalidOperationException("Seleccioná un Excel válido de hasta 20 MB.");
            var user=users.GetCurrentUserName();
            var lectura=ConciliacionDetalleProcesador.Leer(archivo.Contenido);
            var hash=Convert.ToHexString(SHA256.HashData(archivo.Contenido));
            using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
            CommandDefinition Cmd(string sql,object? args=null)=>new(sql,args,tx,commandTimeout:120,cancellationToken:token);
            var id=await cn.ExecuteScalarAsync<long?>(Cmd("SELECT TOP(1) DetalleId FROM dbo.BK_DetalleProcesador WITH(UPDLOCK,HOLDLOCK) WHERE Procesador=@p AND HashArchivo=@hash",
                new{p=lectura.Procesador,hash}));
            if(id is null)
            {
                id=await cn.ExecuteScalarAsync<long>(Cmd("""
                    INSERT INTO dbo.BK_DetalleProcesador(Procesador,ArchivoNombre,HashArchivo,FechaDesde,FechaHasta,CantidadOperaciones,Bruto,Comision,IvaComision,Neto,UsuarioImportacion)
                    OUTPUT INSERTED.DetalleId VALUES(@p,@nombre,@hash,@desde,@hasta,@cant,@bruto,@com,@iva,@neto,@user);
                    """,new{p=lectura.Procesador,nombre=Path.GetFileName(archivo.Nombre),hash,desde=lectura.Desde,hasta=lectura.Hasta,cant=lectura.Operaciones.Count,
                        bruto=lectura.Operaciones.Sum(o=>o.Bruto),com=lectura.Operaciones.Sum(o=>o.Comision),iva=lectura.Operaciones.Sum(o=>o.IvaComision),
                        neto=lectura.Operaciones.Sum(o=>o.Neto),user}));
                foreach(var o in lectura.Operaciones)
                    await cn.ExecuteAsync(Cmd("""
                        INSERT INTO dbo.BK_DetalleProcesadorOperacion(DetalleId,Codigo,FechaOperacion,FechaAcreditacion,MedioPago,Estado,Bruto,Comision,IvaComision,Neto)
                        VALUES(@id,@Codigo,@FechaOperacion,@FechaAcreditacion,@MedioPago,@Estado,@Bruto,@Comision,@IvaComision,@Neto);
                        """,new{id,o.Codigo,o.FechaOperacion,o.FechaAcreditacion,o.MedioPago,o.Estado,o.Bruto,o.Comision,o.IvaComision,o.Neto}));
            }
            tx.Commit();
            return await EmparejarDetalleAsync(cn,id.Value,importacion,token);
        },ct);

    public Task<DetalleEmparejamiento> EmparejarDetalleAsync(long detalle,long? importacion,CancellationToken ct=default)
        => Ejecutar("DetalleEmparejar",(cn,token)=>EmparejarDetalleAsync(cn,detalle,importacion,token),ct);

    private static async Task<string> ConfigAsync(SqlConnection cn,string clave,string defecto,CancellationToken ct)
    {
        var v=await cn.ExecuteScalarAsync<string?>(new CommandDefinition(FuenteConfigSql,new{clave},cancellationToken:ct));
        return string.IsNullOrWhiteSpace(v)?defecto:v.Trim();
    }

    private sealed record CabFila(string Procesador);
    private sealed record OpFila(string Codigo,string MedioPago,decimal Bruto,decimal Comision,decimal IvaComision,decimal Neto);
    private sealed record MovFila(string Referencia,decimal Importe);

    private static async Task<DetalleEmparejamiento> EmparejarDetalleAsync(SqlConnection cn,long detalle,long? importacion,CancellationToken ct)
    {
        var cab=await cn.QuerySingleOrDefaultAsync<CabFila>(new CommandDefinition(
            "SELECT Procesador FROM dbo.BK_DetalleProcesador WHERE DetalleId=@detalle",new{detalle},cancellationToken:ct))
            ??throw new InvalidOperationException("El detalle ya no existe.");
        var texto=await ConfigAsync(cn,"CONCILIACION-NAVE-TEXTO","NAVE",ct);
        var prefijoTexto=await ConfigAsync(cn,"CONCILIACION-NAVE-PREFIJO","10",ct);
        if(!int.TryParse(prefijoTexto,NumberStyles.None,CultureInfo.InvariantCulture,out var prefijo)||prefijo is < 4 or > 40)
            throw new InvalidOperationException("Revisá CONCILIACION-NAVE-PREFIJO: debe ser un entero entre 4 y 40.");
        var ops=(await cn.QueryAsync<OpFila>(new CommandDefinition(
            "SELECT Codigo,MedioPago,Bruto,Comision,IvaComision,Neto FROM dbo.BK_DetalleProcesadorOperacion WHERE DetalleId=@detalle",new{detalle},cancellationToken:ct))).ToList();
        var movs=(await cn.QueryAsync<MovFila>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(ReferenciaExterna)) Referencia,Importe FROM dbo.BK_ExtractoMovimiento
            WHERE DescripcionOriginal LIKE '%'+@texto+'%' AND ISNULL(LTRIM(RTRIM(ReferenciaExterna)),'')<>''
              AND (@importacion IS NULL OR ImportacionId=@importacion);
            """,new{texto,importacion},commandTimeout:120,cancellationToken:ct))).ToList();

        string Clave(string codigo)=>codigo.Length<=prefijo?codigo:codigo[..prefijo];
        var porClave=ops.GroupBy(o=>Clave(o.Codigo),StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.ToList(),StringComparer.OrdinalIgnoreCase);
        var grupos=movs.GroupBy(m=>m.Referencia,StringComparer.OrdinalIgnoreCase).ToList();
        int emp=0,dif=0,huerfanos=0,anulados=0;
        var usadas=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var g in grupos)
        {
            var suma=g.Sum(m=>m.Importe);
            if(porClave.TryGetValue(g.Key,out var candidatas))
            {
                // Se compara la suma por código: el pago y su devolución comparten código y se netean.
                usadas.Add(g.Key);
                if(suma==candidatas.Sum(o=>o.Neto)) emp++; else dif++;
            }
            else if(suma==0) anulados++; // pago y devolución en el mismo extracto
            else huerfanos+=g.Count();
        }
        var sinCredito=porClave.Where(k=>!usadas.Contains(k.Key)).Sum(k=>k.Value.Count);
        var avisos=new List<string>();
        if(anulados>0) avisos.Add($"{anulados} operación(es) con pago y devolución en el extracto se anulan entre sí y no figuran en el detalle.");
        if(sinCredito>0) avisos.Add($"{sinCredito} operación(es) del detalle no tienen crédito en el extracto: suele ser porque el extracto termina antes que el reporte.");
        if(huerfanos>0) avisos.Add($"{huerfanos} crédito(s) del extracto no figuran en el detalle: revisá que el reporte cubra el mismo período.");
        var medios=ops.GroupBy(o=>string.IsNullOrWhiteSpace(o.MedioPago)?"(sin medio)":o.MedioPago).OrderByDescending(g=>g.Sum(o=>o.Bruto))
            .Select(g=>new DetalleMedio(g.Key,g.Count(),g.Sum(o=>o.Bruto),g.Sum(o=>o.Comision),g.Sum(o=>o.IvaComision),g.Sum(o=>o.Neto))).ToList();
        return new(detalle,cab.Procesador,ops.Count,emp,dif,sinCredito,huerfanos,ops.Sum(o=>o.Bruto),ops.Sum(o=>o.Comision),ops.Sum(o=>o.IvaComision),ops.Sum(o=>o.Neto),medios,avisos);
    }
}
