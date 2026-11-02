using AlfaCore.Models;

namespace AlfaCore.Services;

public sealed class TarjetasRecepcionWorkerHostedService(IServiceProvider services,ILogger<TarjetasRecepcionWorkerHostedService> logger):BackgroundService
{
    private readonly Dictionary<int,DateTime> _reintentarDesde=[];
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10),stoppingToken);
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope=services.CreateScope();
                if(await scope.ServiceProvider.GetRequiredService<IWorkerAssignmentService>().DebeEjecutarWorkersAsync(stoppingToken))
                {
                    var bases=await scope.ServiceProvider.GetRequiredService<ICentralCompraIaService>().GetBasesHabilitadasAsync(stoppingToken);
                    foreach(var b in bases)
                    {
                        stoppingToken.ThrowIfCancellationRequested();
                        if(_reintentarDesde.TryGetValue(b.IdBase,out var proximo)&&proximo>DateTime.UtcNow)continue;
                        using var baseScope=services.CreateScope();
                        var sesiones=baseScope.ServiceProvider.GetRequiredService<ISessionService>();
                        sesiones.SetWebhookOverride(new SessionDto{BaseId=b.IdBase,Nombre=b.Nombre,Servidor=b.DbServer,BaseDatos=b.DbName,Usuario=b.DbUser,Password=b.DbPassword,TrustServerCertificate=true});
                        try{await baseScope.ServiceProvider.GetRequiredService<TarjetasRecepcionService>().ProcesarSiguienteAsync(stoppingToken);}
                        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){throw;}
                        catch(Exception ex)
                        {
                            _reintentarDesde[b.IdBase]=DateTime.UtcNow.AddMinutes(5);
                            logger.LogError(ex,"Error en la cola documental de tarjetas para la base {BaseId}.",b.IdBase);
                            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                            try{await baseScope.ServiceProvider.GetRequiredService<IAppEventService>().LogErrorAsync("TarjetasRecepcion","CicloWorker",ex,"No se pudo consultar la cola documental de tarjetas.",ct:timeout.Token);}catch(Exception logError){logger.LogWarning(logError,"No se pudo registrar el incidente del worker de tarjetas.");}
                        }
                    }
                }
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"No se pudo consultar la asignación de bases del worker de tarjetas.");}
            await Task.Delay(TimeSpan.FromSeconds(30),stoppingToken);
        }
    }
}
