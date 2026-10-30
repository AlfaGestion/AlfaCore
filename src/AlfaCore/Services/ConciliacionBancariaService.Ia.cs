using System.Data;
using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionBancariaService
{
    private static readonly JsonSerializerOptions IaJson=new(){PropertyNameCaseInsensitive=true,MaxDepth=32};

    public Task<ConciliacionIaResultado> AnalizarIaAsync(ConciliacionCompararFiltro filtro,CancellationToken ct=default)
        =>Ejecutar("AnalizarIA",(cn,token)=>AnalizarIaEnConexionAsync(cn,null,filtro,token),ct);

    internal async Task<ConciliacionIaResultado> AnalizarIaEnConexionAsync(SqlConnection cn,SqlTransaction? tx,ConciliacionCompararFiltro filtro,CancellationToken ct)
    {
        var sesion=sessions.GetActiveSession()!.Id;
        if(!await cn.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT CONVERT(bit,CASE WHEN OBJECT_ID('dbo.BK_ConciliacionIaAnalisis','U') IS NULL THEN 0 ELSE 1 END);",transaction:tx,cancellationToken:ct)))
            throw new InvalidOperationException("Aplicá la actualización de asistencia IA de conciliación antes de analizar.");
        var f=filtro with{Estado="Pendientes",PaginaBanco=1,PaginaAlfa=1};ValidarComparacion(f);
        var maximoRaw=await cn.ExecuteScalarAsync<string>(new CommandDefinition("SELECT VALOR FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-IA-MAX-FILAS';",transaction:tx,cancellationToken:ct));
        var maximo=string.IsNullOrWhiteSpace(maximoRaw) ? 300 : int.TryParse(maximoRaw,out var n)&&n is >=50 and <=1000 ? n : throw new InvalidOperationException("Revisá el máximo de movimientos para IA: debe estar entre 50 y 1000 por lista.");
        var args=new DynamicParameters(ArgumentosComparacion(f));args.Add("limite",maximo+1);
        using var r=await cn.QueryMultipleAsync(new CommandDefinition(CompararBaseSql+"""
            SELECT TOP(@limite) * FROM #Banco WHERE ConciliacionId IS NULL AND Alterado=0 AND Importe<>0
                AND (@categoria='' OR Categoria=@categoria)
                AND (@sentido='Todos' OR (@sentido='Ingresos' AND Importe>0) OR (@sentido='Egresos' AND Importe<0))
                AND (@minimo IS NULL OR ABS(Importe)>=@minimo) AND (@maximo IS NULL OR ABS(Importe)<=@maximo)
                AND (@texto='' OR CONCAT(Detalle,' ',Referencia,' ',Importe,' ',CONVERT(char(10),Fecha,103)) LIKE @patron)
                ORDER BY Fecha,Id;
            SELECT TOP(@limite) * FROM #Alfa WHERE ConciliacionId IS NULL AND Legacy=0 AND Alterado=0 AND Moneda='1' AND Importe<>0
                AND (@sentido='Todos' OR (@sentido='Ingresos' AND Importe>0) OR (@sentido='Egresos' AND Importe<0))
                AND (@minimo IS NULL OR ABS(Importe)>=@minimo) AND (@maximo IS NULL OR ABS(Importe)<=@maximo)
                AND (@texto='' OR CONCAT(Detalle,' ',Referencia,' ',Comprobante,' ',Importe,' ',CONVERT(char(10),Fecha,103)) LIKE @patron)
                ORDER BY Fecha,Periodo,Mes,Asiento,TipoRegistro,Secuencia;
            """,args,tx,commandTimeout:120,cancellationToken:ct));
        var banco=(await r.ReadAsync<ConciliacionFilaBanco>()).AsList();var alfa=(await r.ReadAsync<ConciliacionFilaAlfa>()).AsList();
        r.Dispose();
        if(banco.Count>maximo||alfa.Count>maximo)throw new InvalidOperationException($"El análisis admite hasta {maximo} movimientos por lista. Acotá el período o los filtros; no se analizó una muestra parcial.");
        var criterios=await LeerCriterios(cn,tx,ct);
        var comparacion=await CompararEnConexionAsync(cn,tx,f,ct);
        var propuestas=new List<ConciliacionIaPropuesta>();
        foreach(var par in comparacion.Propuestas.Where(p=>banco.Any(b=>b.Id==p.Banco.Id)&&alfa.Any(a=>a.Clave==p.Alfa.Clave)))
            propuestas.Add(CrearPropuestaIa(f,[par.Banco],[par.Alfa],"Reglas","Alta","Coincidencia única recíproca según los criterios configurados; requiere revisión."));
        var usadosBanco=propuestas.SelectMany(p=>p.Banco).Select(b=>b.Id).ToHashSet();
        var usadosAlfa=propuestas.SelectMany(p=>p.Alfa).Select(a=>a.Clave).ToHashSet();
        var restantes=banco.Where(b=>!usadosBanco.Contains(b.Id)).ToList();
        var catalogo=await AsientoCatalogoEnConexionAsync(cn,tx,ct);
        var contexto=new ConciliacionIaContexto(f,criterios,restantes,alfa.Where(a=>!usadosAlfa.Contains(a.Clave)).ToList(),catalogo.Cuentas);
        var modelo="Reglas";var descartadas=0;IReadOnlyList<ConciliacionIaOrientacion> orientaciones=[];
        if(restantes.Count>0)
        {
            if(iaModelo is null)throw new InvalidOperationException("La conexión de IA no está disponible en este servidor.");
            var configurado=await cn.ExecuteScalarAsync<string>(new CommandDefinition("SELECT VALOR FROM dbo.TA_CONFIGURACION WHERE CLAVE='CONCILIACION-IA-MODELO';",transaction:tx,cancellationToken:ct)) ?? "";
            var respuesta=await iaModelo.AnalizarAsync(contexto,configurado,ct);VerificarSesion(sesion);
            modelo=respuesta.Modelo.Length>100 ? respuesta.Modelo[..100] : respuesta.Modelo;
            var validadas=ValidarRespuestaIa(contexto,respuesta.Json);
            propuestas.AddRange(validadas.Propuestas);descartadas=validadas.Descartadas;orientaciones=validadas.Orientaciones;
        }
        var resultado=new ConciliacionIaResultado(Guid.NewGuid(),modelo,banco.Count,alfa.Count,descartadas,propuestas,orientaciones){Criterios=criterios};
        VerificarSesion(sesion);ct.ThrowIfCancellationRequested();
        await cn.ExecuteAsync(new CommandDefinition("""
            INSERT dbo.BK_ConciliacionIaAnalisis(Id,Usuario,Sesion,Cuenta,Modelo,ResultadoJson)
            VALUES(@Id,@usuario,@sesion,@cuenta,@Modelo,@json);
            """,new{resultado.Id,usuario=users.GetCurrentUserName(),sesion,cuenta=f.Cuenta.Trim(),resultado.Modelo,json=JsonSerializer.Serialize(resultado)},tx,cancellationToken:ct));
        return resultado;
    }

    private static ConciliacionIaPropuesta CrearPropuestaIa(ConciliacionCompararFiltro filtro,IReadOnlyList<ConciliacionFilaBanco> banco,IReadOnlyList<ConciliacionFilaAlfa> alfa,string origen,string confianza,string motivo)
    {
        var id=Guid.NewGuid();
        return new(id,origen,confianza,motivo,banco,alfa,new(id,filtro,banco.Select(b=>new ConciliacionBancoSeleccion(b.Id,b.Huella)).ToList(),alfa.Select(a=>new ConciliacionAlfaSeleccion(a.Clave,a.Huella)).ToList()));
    }

    internal static (IReadOnlyList<ConciliacionIaPropuesta> Propuestas,IReadOnlyList<ConciliacionIaOrientacion> Orientaciones,int Descartadas) ValidarRespuestaIa(ConciliacionIaContexto c,string json)
    {
        ConciliacionIaSalida salida;
        try { salida=json.Length<=500000 ? JsonSerializer.Deserialize<ConciliacionIaSalida>(json,IaJson) ?? throw new JsonException() : throw new JsonException(); }
        catch(JsonException ex)
        {
            // Diagnóstico reproducible sin conservar el JSON, nombres, importes ni instrucciones del modelo.
            var huella=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
            var diagnostico=$"Formato IA inválido; longitud={json.Length}; ruta={ex.Path}; línea={ex.LineNumber}; posición={ex.BytePositionInLine}; SHA256={huella}";
            throw new InvalidOperationException("La IA devolvió una respuesta con un formato incompatible. No se generaron propuestas; se registró el detalle para soporte.",new FormatException(diagnostico));
        }
        if(salida.Grupos is null||salida.Orientaciones is null||salida.Grupos.Count>1000||salida.Orientaciones.Count>1000)
            throw new InvalidOperationException("La respuesta de IA excede el alcance permitido.");
        var validas=new List<ConciliacionIaPropuesta>();var descartadas=0;
        foreach(var g in salida.Grupos)
        {
            if(g is null||g.Banco is null||g.Alfa is null||g.Banco.Length is <1 or >20||g.Alfa.Length is <1 or >20
                ||g.Banco.Distinct().Count()!=g.Banco.Length||g.Alfa.Distinct().Count()!=g.Alfa.Length
                ||g.Banco.Any(i=>i<0||i>=c.Banco.Count)||g.Alfa.Any(i=>i<0||i>=c.Alfa.Count)
                ||g.Confianza is not ("Alta" or "Media" or "Baja")||string.IsNullOrWhiteSpace(g.Motivo)||g.Motivo.Length>1000)
            { descartadas++;continue; }
            var banco=g.Banco.Select(i=>c.Banco[i]).ToList();var alfa=g.Alfa.Select(i=>c.Alfa[i]).ToList();
            var signo=Math.Sign(banco[0].Importe);var diferencia=banco.Sum(b=>b.Importe)-alfa.Sum(a=>a.Importe);
            if(signo==0||banco.Any(b=>b.ConciliacionId.HasValue||b.Alterado||Math.Sign(b.Importe)!=signo)
                ||alfa.Any(a=>a.ConciliacionId.HasValue||a.Alterado||a.Legacy||a.Moneda!="1"||Math.Sign(a.Importe)!=signo)
                ||Math.Abs(diferencia)>c.Criterios.Tolerancia
                ||(c.Criterios.CompararFecha&&banco.Any(b=>alfa.Any(a=>Math.Abs((b.Fecha.Date-a.Fecha.Date).TotalDays)>c.Criterios.Dias)))
                ||(c.Criterios.CompararReferencia&&banco.Any(b=>string.IsNullOrWhiteSpace(b.Referencia)||alfa.Any(a=>a.Referencia.Trim()!=b.Referencia.Trim()))))
            { descartadas++;continue; }
            validas.Add(CrearPropuestaIa(c.Filtro,banco,alfa,"IA",g.Confianza,g.Motivo.Trim()));
        }
        // Se descartan TODOS los grupos que compiten por un movimiento, sin elegir el primero.
        var repetidosBanco=validas.SelectMany(p=>p.Banco).GroupBy(b=>b.Id).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet();
        var repetidosAlfa=validas.SelectMany(p=>p.Alfa).GroupBy(a=>a.Clave).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet();
        descartadas+=validas.RemoveAll(p=>p.Banco.Any(b=>repetidosBanco.Contains(b.Id))||p.Alfa.Any(a=>repetidosAlfa.Contains(a.Clave)));
        var usados=validas.SelectMany(p=>p.Banco).Select(b=>b.Id).ToHashSet();
        var consejos=new List<ConciliacionIaOrientacion>();
        foreach(var o in salida.Orientaciones)
        {
            if(o is null||o.Banco<0||o.Banco>=c.Banco.Count||usados.Contains(c.Banco[o.Banco].Id)||consejos.Any(x=>x.Banco.Id==c.Banco[o.Banco].Id)
                ||string.IsNullOrWhiteSpace(o.Motivo)||o.Motivo.Length>1000||o.Cuenta is null
                ||!Enum.TryParse<ConciliacionBancaria.Domain.Enums.MovementCategory>(o.Categoria,out var categoria)||!Enum.IsDefined(categoria)
                ||(o.Cuenta.Trim().Length>0&&!c.Cuentas.Any(a=>a.Codigo==o.Cuenta.Trim())))
            { descartadas++;continue; }
            consejos.Add(new(c.Banco[o.Banco],categoria.ToString(),o.Cuenta.Trim(),o.Motivo.Trim()));
        }
        return(validas,consejos,descartadas);
    }

    public Task<IReadOnlyList<long>> ConfirmarIaAsync(Guid analisis,IReadOnlyList<Guid> elegidas,bool aceptarDiferencia,string motivo,CancellationToken ct=default)
        =>Ejecutar("ConfirmarIA",async(cn,token)=>{
            using var tx=cn.BeginTransaction(IsolationLevel.Serializable);
            var ids=await ConfirmarIaEnTransaccionAsync(cn,tx,analisis,elegidas,aceptarDiferencia,motivo,token);tx.Commit();return ids;
        },ct);

    internal async Task<IReadOnlyList<long>> ConfirmarIaEnTransaccionAsync(SqlConnection cn,SqlTransaction tx,Guid analisis,IReadOnlyList<Guid> elegidas,bool aceptarDiferencia,string motivo,CancellationToken ct)
    {
        if(elegidas.Count is <1 or >100||elegidas.Distinct().Count()!=elegidas.Count)throw new InvalidOperationException("Elegí entre 1 y 100 propuestas distintas para confirmar.");
        var sesion=sessions.GetActiveSession()!.Id;
        var json=await cn.ExecuteScalarAsync<string>(new CommandDefinition("SELECT ResultadoJson FROM dbo.BK_ConciliacionIaAnalisis WHERE Id=@analisis AND Sesion=@sesion AND UPPER(LTRIM(RTRIM(Usuario)))=UPPER(@usuario);",new{analisis,sesion,usuario=users.GetCurrentUserName()},tx,cancellationToken:ct));
        if(json is null)throw new InvalidOperationException("El análisis no pertenece a esta sesión o no está disponible. Volvé a analizar.");
        var resultado=JsonSerializer.Deserialize<ConciliacionIaResultado>(json)!;
        var propuestas=resultado.Propuestas.Where(p=>elegidas.Contains(p.Id)).ToList();
        if(propuestas.Count!=elegidas.Count)throw new InvalidOperationException("Una propuesta ya no está disponible en este análisis.");
        await BloquearCuenta(cn,tx,propuestas[0].Solicitud.Filtro.Cuenta.Trim(),ct);
        if(resultado.Criterios!=await LeerCriterios(cn,tx,ct))throw new InvalidOperationException("Los criterios cambiaron después del análisis. Volvé a analizar antes de confirmar.");
        var ids=new List<long>();
        foreach(var propuesta in propuestas)
            ids.Add(await ConciliarEnTransaccionAsync(cn,tx,propuesta.Solicitud with{AceptarDiferencia=aceptarDiferencia,MotivoDiferencia=motivo},ct));
        VerificarSesion(sesion);ct.ThrowIfCancellationRequested();return ids;
    }
}
