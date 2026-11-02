using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class ConciliacionTarjetasService
{
    public Task<TarjetasCriteriosAnalisis> CriteriosIaAsync(CancellationToken ct=default)
        =>Ejecutar("CriteriosAnalisis",CargarCriteriosIa,ct);
    private static async Task<TarjetasCriteriosAnalisis> CargarCriteriosIa(SqlConnection c,CancellationToken ct)
    {
        var criterios=new TarjetasCriteriosAnalisis
        {
            Automatico=!string.Equals((await Config(c,"TARJETAS-BUSQUEDA-AUTOMATICA",ct)).Trim(),"NO",StringComparison.OrdinalIgnoreCase),
            MaxDiasGrupo=EnteroIa(await Config(c,"TARJETAS-AUTO-MAX-DIAS",ct),3,1,7),
            MaxIntentosAutomaticos=EnteroIa(await Config(c,"TARJETAS-AUTO-MAX-INTENTOS",ct),1000000,100,1000000),
            Dias=EnteroIa(await Config(c,"TARJETAS-IA-DIAS",ct),3,0,31),
            MaxFilasIa=EnteroIa(await Config(c,"TARJETAS-IA-MAX-FILAS",ct),2000,50,2000),
            MaxMovimientosGrupo=EnteroIa(await Config(c,"TARJETAS-AGRUPA-MAX-MOV",ct),200,1,500),
            MaxLotesCombinados=EnteroIa(await Config(c,"TARJETAS-AGRUPA-MAX-LOTES",ct),4,1,6),
            MaxIntentos=EnteroIa(await Config(c,"TARJETAS-AGRUPA-MAX-INTENTOS",ct),100000,100,1000000)
        };
        criterios.Validar();return criterios;
    }

    public Task<TarjetasIaResultado> AnalizarIaAsync(TarjetasFiltro filtro,CancellationToken ct=default,
        Func<int,int,Task>? progreso=null,TarjetasCriteriosAnalisis? criterios=null,Func<TarjetasProgresoAnalisis,Task>? avance=null,bool guardar=true)=>Ejecutar("AsistirIA",async(c,t)=>
    {
        if(avance is not null)await avance(new(1,"Consultando movimientos autorizados y criterios de análisis…"));
        var session=sessions.GetActiveSession()!.Id;var autor=users.GetCurrentUserName();var sistema=users.CurrentUser?.SystemCode;
        if(guardar&&await TieneAnalisis(c,t)==0)throw new InvalidOperationException("Ejecutá las actualizaciones de base: falta habilitar el guardado del análisis de tarjetas.");
        var opciones=criterios??await CargarCriteriosIa(c,t);opciones.Validar();
        var datos=await Comparar(c,filtro with{Estado="Pendientes"},t);
        if(!datos.Importacion.DetalleCompleto)throw new InvalidOperationException("Revisá el detalle del PDF: no coincide con el bruto. No se puede proponer una conciliación.");
        if(avance is not null)await avance(new(2,$"Buscando agrupaciones entre {datos.Detalles.Count} registros de liquidación y {datos.Movimientos.Count} movimientos de Alfa…"));
        var local=await BuscarConProgreso(datos,opciones,avance,t);AntesDeGrabar(session,t);
        if(opciones.Automatico&&datos.Detalles.Any(d=>!local.Propuestas.Any(p=>p.Detalles.Any(x=>x.Id==d.Id))))
        {
            var todos=await Comparar(c,filtro with{Estado="Todos"},t);
            var yaConciliados=TarjetasBusquedaAutomatica.DetectarYaConciliados(todos,opciones,local.Propuestas.SelectMany(p=>p.Detalles).Select(d=>d.Id).ToHashSet());
            var idsBloqueados=yaConciliados.SelectMany(d=>d.Detalles).ToHashSet();
            local=local with{Diagnosticos=local.Diagnosticos.Where(d=>!d.Detalles.Any(idsBloqueados.Contains)).Concat(yaConciliados).ToList()};
        }
        var protegidos=local.Diagnosticos.Where(d=>d.Tipo is "Ambiguo" or "YaConciliado").SelectMany(d=>d.Detalles).ToHashSet();
        var ds=local.Propuestas.SelectMany(p=>p.Detalles).Select(d=>d.Id).ToHashSet();
        var ms=local.Propuestas.SelectMany(p=>p.Movimientos).Select(m=>m.Clave).ToHashSet(StringComparer.Ordinal);
        var restantes=datos with{Detalles=datos.Detalles.Where(d=>!ds.Contains(d.Id)&&!protegidos.Contains(d.Id)).ToList(),Movimientos=datos.Movimientos.Where(m=>!ms.Contains(m.Clave)).ToList()};
        var ia=new TarjetasIaResultado("Motor de agrupaciones",0,0,0,opciones.Dias,[]){LotesAnalizados=0};
        var aviso="";var planes=0;var modeloPlan="";
        if(avance is not null)await avance(new(3,!opciones.ConsultarIa?"Consulta al modelo desactivada para este análisis.":restantes.Detalles.Count>0&&restantes.Movimientos.Count>0?"Preparando las consultas de IA para los casos restantes…":"No se requieren consultas al modelo para este análisis."));
        if(opciones.ConsultarIa&&restantes.Detalles.Count>0&&restantes.Movimientos.Count>0)
        {
            try
            {
                if(opciones.Automatico&&iaModelo is not null&&restantes.Detalles.Any(d=>d.Nivel=="Lote"))
                {
                    var plan=TarjetasPlanIa.Preparar(restantes,filtro,opciones);
                    if(plan.Objetivos.Count>0&&plan.Alcances.Count>0)
                    {
                        if(avance is not null)await avance(new(3,"La IA está eligiendo fechas y sucursales para los lotes restantes. El plan se verificará antes de proponer conciliaciones."));
                        planes++;var respuestaPlan=await iaModelo.AnalizarAsync(plan.Contexto,"",t);modeloPlan=respuestaPlan.Modelo;AntesDeGrabar(session,t);
                        var seleccionPlan=TarjetasPlanIa.Validar(plan,respuestaPlan.Json,opciones);
                        if(avance is not null)await avance(new(3,"Verificando cantidades, importes y alternativas en los alcances elegidos por la IA…"));
                        var verificado=await Task.Run(()=>TarjetasBusquedaAutomatica.Buscar(restantes,opciones,t,plan:seleccionPlan),t);
                        var nuevos=verificado.Propuestas.SelectMany(p=>p.Detalles).Select(d=>d.Id).ToHashSet();
                        local=local with{Propuestas=local.Propuestas.Concat(verificado.Propuestas).ToList(),Intentos=local.Intentos+verificado.Intentos,
                            Diagnosticos=local.Diagnosticos.Where(d=>!d.Detalles.Any(nuevos.Contains)).Concat(verificado.Diagnosticos).ToList()};
                        var ambiguosPlan=verificado.Diagnosticos.Where(d=>d.Tipo=="Ambiguo").SelectMany(d=>d.Detalles).ToHashSet();
                        var usados=verificado.Propuestas.SelectMany(p=>p.Movimientos).Select(m=>m.Clave).ToHashSet(StringComparer.Ordinal);
                        restantes=restantes with{Detalles=restantes.Detalles.Where(d=>!nuevos.Contains(d.Id)&&!ambiguosPlan.Contains(d.Id)).ToList(),Movimientos=restantes.Movimientos.Where(m=>!usados.Contains(m.Clave)).ToList()};
                    }
                }
                var lotes=TarjetasIaLotes.Preparar(restantes,opciones.Dias,opciones.MaxFilasIa);
                var analizables=lotes.Where(l=>l.Detalles.Count>0&&l.Movimientos.Count>0).ToList();
                if(analizables.Count>0&&iaModelo is null)throw new InvalidOperationException("La conexión de IA no está disponible en este servidor.");
                var resultados=new List<TarjetasIaResultado>();
                for(var i=0;i<analizables.Count;i++)
                {
                    AntesDeGrabar(session,t);if(progreso is not null)await progreso(i+1,analizables.Count);
                    if(avance is not null)await avance(new(3,"Esperando respuesta de IA. La duración de esta consulta puede variar.",LoteActual:i+1,TotalLotes:analizables.Count,LotesCompletados:i));
                    var contexto=TarjetasIaPropuestas.Contexto(analizables[i],filtro,opciones.Dias,opciones.MaxFilasIa);
                    var respuesta=await iaModelo!.AnalizarAsync(contexto,"",t);AntesDeGrabar(session,t);
                    var validado=TarjetasIaPropuestas.Validar(analizables[i],contexto,respuesta);
                    if(opciones.Automatico)
                    {
                        var ambiguas=validado.Propuestas.Where(p=>TarjetasBusquedaAutomatica.HayAlternativaIndistinguible(p,analizables[i],opciones)).ToList();
                        local=local with{Diagnosticos=local.Diagnosticos.Concat(ambiguas.Select(p=>new TarjetasDiagnostico("Ambiguo","Cupón sin identidad verificable",p.Detalles.Select(d=>d.Id).ToList(),"Hay movimientos alternativos compatibles en fecha e importe y el cupón de Alfa no identifica cuál corresponde. No se eligió uno por la respuesta del modelo.",[]))).ToList()};
                        var validas=validado.Propuestas.Where(p=>TarjetasBusquedaAutomatica.CumpleLimites(p,opciones)&&!ambiguas.Contains(p)).ToList();
                        validado=validado with{Propuestas=validas,Descartadas=validado.Descartadas+validado.Propuestas.Count-validas.Count};
                    }
                    resultados.Add(validado);
                    if(avance is not null)await avance(new(3,"Respuesta recibida y validada.",LoteActual:i+1,TotalLotes:analizables.Count,LotesCompletados:i+1));
                }
                ia=TarjetasIaLotes.Unir(lotes,resultados,restantes,opciones.Dias);
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex) when(local.Propuestas.Count>0)
            {
                AntesDeGrabar(session,t);
                var codigo=await events.LogErrorAsync("ConciliacionTarjetas","AsistirIAParcial",ex,"Se conservaron las agrupaciones locales; la consulta a IA no pudo completarse.",ct:t);
                aviso=$" La consulta a IA no se completó (código {codigo}); se conservan las propuestas ya verificadas, sin conciliaciones automáticas.";
            }
        }
        if(avance is not null)await avance(new(4,"Reuniendo propuestas y verificando los resultados antes de mostrarlos…"));
        var resumen=$"{(opciones.Automatico?"Búsqueda automática":"Búsqueda local")}: {local.Intentos:N0} intentos; {local.Propuestas.Count} agrupaciones propuestas; {local.Ambiguas} candidatos descartados por competir. IA: {ia.LotesAnalizados} consultas de movimientos; {planes} consultas de planificación, {ia.DetallesAnalizados} filas de liquidación y {ia.MovimientosAnalizados} movimientos enviados.";
        if(local.Limitada)resumen+=" Se alcanzó un límite de exploración: no se agotaron las combinaciones. Se indican los alcances no agotados; las propuestas conservadas se verificaron en su fecha y sucursal.";
        if(local.DiasExcedidos>0)resumen+=$" {local.DiasExcedidos} grupos diarios exceden el máximo de movimientos y no se exploraron localmente: filtrá por sucursal o ampliá el máximo.";
        if(!opciones.ConsultarIa)resumen+=" Consulta al modelo desactivada para esta prueba.";
        AntesDeGrabar(session,t);
        var resultado=ia with{Modelo=(opciones.Automatico?"Búsqueda automática":"Agrupación local")+(planes>0?" + plan "+modeloPlan:"")+(ia.LotesAnalizados>0?" + "+ia.Modelo:""),
            DetallesAnalizados=datos.Detalles.Count,MovimientosAnalizados=datos.Movimientos.Count,
            Propuestas=local.Propuestas.Concat(ia.Propuestas).ToList(),ResumenBusqueda=resumen+aviso,Diagnosticos=local.Diagnosticos,LotesAnalizados=ia.LotesAnalizados+planes};
        if(guardar)
        {
            if(avance is not null)await avance(new(4,"Guardando el análisis para recuperarlo al volver a abrir la liquidación…"));
            resultado=resultado with{AnalisisId=await GuardarAnalisis(c,datos,filtro,opciones,resultado,autor,sistema,t)};
        }
        return resultado;
    },ct);

    private static int EnteroIa(string valor,int inicial,int minimo,int maximo)=>string.IsNullOrWhiteSpace(valor)?inicial:
        int.TryParse(valor,out var n)&&n>=minimo&&n<=maximo?n:throw new InvalidOperationException("Revisá los parámetros de análisis de tarjetas: hay un valor fuera de los límites permitidos.");
}
