using System.Reflection;
using AlfaCore.Repositories;
using AlfaCore.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AlfaCore.Tests;

internal sealed class CargaComprobantesArcaRuntime : IDisposable
{
    private readonly ServiceProvider _http;
    private readonly ILoggerFactory _loggers = LoggerFactory.Create(_ => { });
    public IAppEventService Events { get; }
    public IArcaConfigService Config { get; }
    public IWsaaClient Wsaa { get; }
    public IWsfev1Client Wsfe { get; }
    public ISessionService Sessions { get; }
    public IConfiguration Configuration { get; }

    public CargaComprobantesArcaRuntime()
    {
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:AlfaGestion"] = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL") }).Build();
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext.Items["AlfaCore.ConnectionString"] = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL");
        var env = DispatchProxy.Create<IWebHostEnvironment, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)env).Handler = (method, _) => method.Name switch
        { "get_ContentRootPath" => Path.Combine(Path.GetTempPath(), "AlfaCorePruebasArca"), "get_EnvironmentName" => "Development", _ => null };
        Events = new AppEventService(env, http, new AuxErrRepository(Configuration, http, _loggers.CreateLogger<AuxErrRepository>()),
            new AppAuditRepository(Configuration, http), new AppAuditActorAccessor { UserName = "PRUEBA_HOMOLOGACION" }, _loggers.CreateLogger<AppEventService>());
        Sessions = DispatchProxy.Create<ISessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)Sessions).Handler = (method, _) => method.Name == "GetConnectionString" ? Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL") : null;
        _http = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var factory = _http.GetRequiredService<IHttpClientFactory>();
        Config = new ArcaConfigService(Events, Sessions, Configuration);
        Wsaa = new WsaaClient(factory, Events); Wsfe = new Wsfev1Client(factory);
    }
    public void Dispose() { _http.Dispose(); _loggers.Dispose(); }
}
