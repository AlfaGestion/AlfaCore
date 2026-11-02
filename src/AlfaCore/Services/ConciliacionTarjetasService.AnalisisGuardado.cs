using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionTarjetasService
{
    public Task<TarjetasImportacion> ObtenerImportacionAsync(long id,CancellationToken ct=default)
        =>Ejecutar("ObtenerImportacion",async(c,t)=>await c.QuerySingleOrDefaultAsync<TarjetasImportacion>(Cmd("SELECT Id,Nombre,Banco,Comercio,Tarjeta,Cuenta,Sucursal,Desde,Hasta,Bruto,Neto,DetalleCompleto,Avisos,Anulada FROM dbo.TJ_Importacion WHERE Id=@id",new{id},t))
            ??throw new InvalidOperationException("No se encontró la liquidación de esta recepción."),ct);

    private static Task<int> TieneAnalisis(SqlConnection c,CancellationToken ct)
        =>c.ExecuteScalarAsync<int>(Cmd("SELECT CASE WHEN OBJECT_ID(N'dbo.TJ_Analisis',N'U') IS NULL THEN 0 ELSE 1 END",null,ct));

    public Task<TarjetasAnalisisReferencia?> UltimoAnalisisAsync(long importacion,CancellationToken ct=default)
        =>Ejecutar<TarjetasAnalisisReferencia?>("ConsultarAnalisisGuardado",async(c,t)=>
        {
            if(await TieneAnalisis(c,t)==0)return null;
            return await c.QuerySingleOrDefaultAsync<TarjetasAnalisisReferencia>(Cmd("""
                SELECT TOP(1) Id,FechaUtc FROM dbo.TJ_Analisis
                WHERE ImportacionId=@importacion AND Usuario=@usuario ORDER BY Id DESC
                """,new{importacion,usuario=users.GetCurrentUserName().Trim()},t));
        },ct);

    private async Task<long> GuardarAnalisis(SqlConnection c,TarjetasComparacion datos,TarjetasFiltro filtro,
        TarjetasCriteriosAnalisis criterios,TarjetasIaResultado resultado,string autor,string? sistema,CancellationToken ct)
    {
        var session=sessions.GetActiveSession()!.Id;
        var contenido=JsonSerializer.Serialize(new TarjetasAnalisisGuardado(1,datos.Importacion.Cuenta.Trim(),filtro with{Estado="Pendientes"},criterios,resultado));
        using var tx=c.BeginTransaction();
        // Impide guardar como vigente un análisis si se anuló o cambió de cuenta durante la espera.
        var actual=await c.QuerySingleAsync<TarjetasImportacion>(Cmd("SELECT * FROM dbo.TJ_Importacion WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id",new{datos.Importacion.Id},ct,tx));
        if(actual.Anulada||actual.Cuenta.Trim()!=datos.Importacion.Cuenta.Trim())
            throw new InvalidOperationException("La importación cambió mientras se analizaba. Volvé a abrirla para analizar la cuenta vigente.");
        var id=await c.ExecuteScalarAsync<long>(Cmd("INSERT dbo.TJ_Analisis(ImportacionId,Usuario,Contenido) OUTPUT INSERTED.Id VALUES(@id,@usuario,@contenido)",
            new{id=datos.Importacion.Id,usuario=autor.Trim(),contenido},ct,tx));
        AntesDeGrabar(session,ct);
        if(users.GetCurrentUserName()!=autor||users.CurrentUser?.SystemCode!=sistema)throw new OperationCanceledException("El usuario del análisis cambió.");
        tx.Commit();return id;
    }

    public Task<TarjetasAnalisisRecuperado> RecuperarAnalisisAsync(long importacion,long analisis,CancellationToken ct=default)
        =>Ejecutar("RecuperarAnalisis",async(c,t)=>
        {
            if(await TieneAnalisis(c,t)==0)throw new InvalidOperationException("Ejecutá las actualizaciones de base para recuperar análisis guardados.");
            var fila=await c.QuerySingleOrDefaultAsync<FilaAnalisis>(Cmd("""
                SELECT Id,FechaUtc,Contenido FROM dbo.TJ_Analisis
                WHERE Id=@analisis AND ImportacionId=@importacion AND Usuario=@usuario
                """,new{analisis,importacion,usuario=users.GetCurrentUserName().Trim()},t))
                ??throw new InvalidOperationException("No hay un análisis guardado disponible para tu usuario en esta liquidación.");
            var guardado=JsonSerializer.Deserialize<TarjetasAnalisisGuardado>(fila.Contenido)
                ??throw new InvalidOperationException("El análisis guardado no se puede leer. Generá un nuevo análisis.");
            if(guardado.Version!=1||guardado.Filtro.Importacion!=importacion)
                throw new InvalidOperationException("El formato del análisis cambió. Generá un nuevo análisis.");
            var filtro=guardado.Filtro with{Estado="Pendientes"};
            var datos=await Comparar(c,filtro,t);
            var resultado=RevalidarAnalisis(guardado,datos,out var omitidas);
            return new TarjetasAnalisisRecuperado(new(fila.Id,fila.FechaUtc),filtro,guardado.Criterios,datos,resultado,omitidas);
        },ct);

    internal static TarjetasIaResultado RevalidarAnalisis(TarjetasAnalisisGuardado guardado,TarjetasComparacion datos,out int omitidas)
    {
        if(guardado.Cuenta.Trim()!=datos.Importacion.Cuenta.Trim()||!datos.Importacion.DetalleCompleto||datos.Importacion.Anulada)
            throw new InvalidOperationException("La cuenta o el detalle de la liquidación cambió. Generá un nuevo análisis.");
        var ds=datos.Detalles.Where(d=>d.Vinculo is null).ToDictionary(d=>d.Id);
        var ms=datos.Movimientos.Where(m=>m.Vinculo is null&&!m.LegacyLiquidado&&!m.Alterado).ToDictionary(m=>m.Clave,StringComparer.Ordinal);
        var validas=new List<TarjetasIaPropuesta>();var usadosD=new HashSet<long>();var usadosM=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in guardado.Resultado.Propuestas)
        {
            if(p.Detalles.Count==0||p.Movimientos.Count==0||p.Detalles.Select(d=>d.Id).Distinct().Count()!=p.Detalles.Count
                ||p.Movimientos.Select(m=>m.Clave).Distinct(StringComparer.Ordinal).Count()!=p.Movimientos.Count)continue;
            if(p.Detalles.Any(d=>!ds.TryGetValue(d.Id,out var actual)||!MismoDetalle(d,actual)||usadosD.Contains(d.Id))
                ||p.Movimientos.Any(m=>!ms.TryGetValue(m.Clave,out var actual)||string.IsNullOrWhiteSpace(m.Huella)||m.Huella!=actual.Huella||usadosM.Contains(m.Clave)))continue;
            var vigente=p with{Detalles=p.Detalles.Select(d=>ds[d.Id]).ToList(),Movimientos=p.Movimientos.Select(m=>ms[m.Clave]).ToList()};
            if(Math.Abs(vigente.Diferencia)>datos.Tolerancia)continue;
            validas.Add(vigente);foreach(var d in vigente.Detalles)usadosD.Add(d.Id);foreach(var m in vigente.Movimientos)usadosM.Add(m.Clave);
        }
        omitidas=guardado.Resultado.Propuestas.Count-validas.Count;
        return guardado.Resultado with{Propuestas=validas};
    }
    private static bool MismoDetalle(TarjetasDetalle a,TarjetasDetalle b)
        =>a.Fecha==b.Fecha&&a.FechaPago==b.FechaPago&&a.Importe==b.Importe&&a.Cantidad==b.Cantidad
            &&a.Liquidacion==b.Liquidacion&&a.Lote==b.Lote&&a.Cupon==b.Cupon&&a.Terminal==b.Terminal&&a.Nivel==b.Nivel;
    private sealed class FilaAnalisis
    {
        public long Id {get;set;}
        public DateTime FechaUtc {get;set;}
        public string Contenido {get;set;}="";
    }
}
