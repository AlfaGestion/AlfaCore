using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>
/// Ensayo explícito contra ALFANET2007_PRUEBAS del mail de comprobantes: lee la configuración del VB6,
/// arma destinatarios/asunto/mensaje de la última factura y, sin cuenta de correo cargada, confirma que el
/// envío informa qué falta en lugar de fallar. Requiere ALFACORE_TEST_SQL y ALFACORE_FA_PRUEBAS=1.
/// </summary>
[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesMailPruebasTests(ITestOutputHelper output)
{
    [FacturacionAutomaticaPruebasFact]
    public async Task ArmaElMailDeLaUltimaFacturaConLasMarcasDelVb6()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        Assert.Equal("ALFANET2007_PRUEBAS", await cn.ExecuteScalarAsync<string>("SELECT DB_NAME()"));
        var factura = await cn.QueryFirstAsync<(string Tc, string Id)>("""
            SELECT TOP (1) LTRIM(RTRIM(TC)), LTRIM(RTRIM(IDCOMPROBANTE)) FROM V_MV_CPTE
            WHERE TC IN ('FC','FP') AND ISNULL(ANULADA,0) = 0 ORDER BY FECHAHORA_GRABACION DESC, IDCOMPROBANTE DESC;
            """);

        var repo = new CargaComprobantesRepository();
        var planner = new CargaComprobantesPlanificador(repo);
        var sender = new CargaComprobantesElectronicaEmisor(runtime.Config, runtime.Wsaa, runtime.Wsfe, runtime.Events);
        var emision = new CargaComprobantesEmisionService(planner, new(planner, new()), sender, runtime.Config, runtime.Events);
        var sesion = new SessionDto();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (method, _) => method.Name switch
        { "GetActiveSession" => sesion, "GetConnectionString" => Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"), _ => null };
        var user = await cn.QuerySingleAsync<AppUserSessionInfo>("SELECT LTRIM(RTRIM(NOMBRE)) UserName,LTRIM(RTRIM(SISTEMA)) SystemCode FROM TA_USUARIOS WHERE NOMBRE='Administrador'");
        var users = DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler = (method, _) => method.Name switch
        { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => user.UserName, "get_CurrentUser" => user, _ => null };
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(runtime.Events).AddSingleton<DocumentPdfService>().BuildServiceProvider();
        var pdfService = provider.GetRequiredService<DocumentPdfService>();
        var loggers = provider.GetRequiredService<ILoggerFactory>();
        var docs = new FacturaDocumentService(runtime.Configuration,
            new DocumentTemplateService(runtime.Configuration, runtime.Events, users, loggers.CreateLogger<DocumentTemplateService>(), runtime.Sessions),
            new DocumentRenderer(), pdfService, new ArcaQrService(), runtime.Events, loggers.CreateLogger<FacturaDocumentService>(), runtime.Sessions);
        var service = new CargaComprobantesService(runtime.Sessions, users, new PermissionService(runtime.Configuration, runtime.Sessions, users, runtime.Events),
            runtime.Events, repo, new(runtime.Config, runtime.Wsaa, runtime.Wsfe), sender, emision, docs, pdfService, new CorreoSalienteService(ConfiguracionReal()));

        var config = await service.GetMailConfigAsync();
        output.WriteLine($"Config: asunto «{config.Asunto}» · al emitir {config.EnviarAlEmitir} · prueba «{config.MailPrueba}»");
        output.WriteLine(config.Mensaje);

        var borrador = await service.PrepararMailComprobanteAsync(factura.Tc, factura.Id);
        output.WriteLine($"{borrador.Tc} {borrador.IdComprobante} · {borrador.Cliente} · para «{borrador.Para}» · último «{borrador.UltimoEnvio}»");
        output.WriteLine($"Asunto: {borrador.Asunto}");
        output.WriteLine(borrador.Mensaje);
        Assert.Equal(factura.Id, borrador.IdComprobante);
        Assert.DoesNotContain("<NOMBRE>", borrador.Asunto + borrador.Mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<PERIODO>", borrador.Asunto + borrador.Mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<DETALLEFACTURA>", borrador.Asunto + borrador.Mensaje, StringComparison.OrdinalIgnoreCase);

        // PDF opcional para revisar el encabezado (datos fiscales de la unidad): ALFACORE_MAIL_PDF=ruta.pdf
        if (Environment.GetEnvironmentVariable("ALFACORE_MAIL_PDF") is { Length: > 0 } rutaPdf)
            await File.WriteAllBytesAsync(rutaPdf, await service.GetPdfComprobanteAsync(factura.Tc, factura.Id));

        // Sin destinatario válido no intenta enviar: avisa qué corregir.
        var sinPara = await service.EnviarMailComprobanteAsync(factura.Tc, factura.Id, "no-es-un-mail", borrador.Asunto, borrador.Mensaje);
        output.WriteLine($"Sin destinatario: {sinPara.Enviado} · {sinPara.Mensaje}");
        Assert.False(sinPara.Enviado);
        Assert.False(string.IsNullOrWhiteSpace(sinPara.Mensaje));

        // Envío real opcional (cuenta de los appsettings del proyecto): ALFACORE_MAIL_ENVIAR_A=destino.
        if (Environment.GetEnvironmentVariable("ALFACORE_MAIL_ENVIAR_A") is { Length: > 0 } destino)
        {
            var envio = await service.EnviarMailComprobanteAsync(factura.Tc, factura.Id, destino, "[Prueba AlfaCore] " + borrador.Asunto, borrador.Mensaje);
            output.WriteLine($"Envío real: {envio.Enviado} · para {envio.Para} · {envio.Mensaje}");
            Assert.True(envio.Enviado, envio.Mensaje);
        }
    }

    /// <summary>Tiempos de cada paso del mail (armar, PDF, envío). Con ALFACORE_MAIL_ENVIAR_A envía de verdad.</summary>
    [FacturacionAutomaticaPruebasFact]
    public async Task MideLosTiemposDelMail()
    {
        using var runtime = new CargaComprobantesArcaRuntime();
        await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")); await cn.OpenAsync();
        var factura = await cn.QueryFirstAsync<(string Tc, string Id)>("""
            SELECT TOP (1) LTRIM(RTRIM(TC)), LTRIM(RTRIM(IDCOMPROBANTE)) FROM V_MV_CPTE
            WHERE TC IN ('FC') AND ISNULL(ANULADA,0) = 0 ORDER BY FECHAHORA_GRABACION DESC, IDCOMPROBANTE DESC;
            """);
        var repo = new CargaComprobantesRepository();
        var planner = new CargaComprobantesPlanificador(repo);
        var sender = new CargaComprobantesElectronicaEmisor(runtime.Config, runtime.Wsaa, runtime.Wsfe, runtime.Events);
        var emision = new CargaComprobantesEmisionService(planner, new(planner, new()), sender, runtime.Config, runtime.Events);
        var sesion = new SessionDto();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (method, _) => method.Name switch
        { "GetActiveSession" => sesion, "GetConnectionString" => Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"), _ => null };
        var user = await cn.QuerySingleAsync<AppUserSessionInfo>("SELECT LTRIM(RTRIM(NOMBRE)) UserName,LTRIM(RTRIM(SISTEMA)) SystemCode FROM TA_USUARIOS WHERE NOMBRE='Administrador'");
        var users = DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler = (method, _) => method.Name switch
        { "get_IsAuthenticated" => true, "IsAuthorizedForSession" => true, "GetCurrentUserName" => user.UserName, "get_CurrentUser" => user, _ => null };
        await using var provider = new ServiceCollection().AddLogging().AddSingleton(runtime.Events).AddSingleton<DocumentPdfService>().BuildServiceProvider();
        var pdfService = provider.GetRequiredService<DocumentPdfService>();
        var loggers = provider.GetRequiredService<ILoggerFactory>();
        var docs = new FacturaDocumentService(runtime.Configuration,
            new DocumentTemplateService(runtime.Configuration, runtime.Events, users, loggers.CreateLogger<DocumentTemplateService>(), runtime.Sessions),
            new DocumentRenderer(), pdfService, new ArcaQrService(), runtime.Events, loggers.CreateLogger<FacturaDocumentService>(), runtime.Sessions);
        var service = new CargaComprobantesService(runtime.Sessions, users, new PermissionService(runtime.Configuration, runtime.Sessions, users, runtime.Events),
            runtime.Events, repo, new(runtime.Config, runtime.Wsaa, runtime.Wsfe), sender, emision, docs, pdfService, new CorreoSalienteService(ConfiguracionReal()));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var borrador = await service.PrepararMailComprobanteAsync(factura.Tc, factura.Id);
        output.WriteLine($"Preparar mail (abrir diálogo): {sw.ElapsedMilliseconds} ms");
        sw.Restart(); await service.GetPdfComprobanteAsync(factura.Tc, factura.Id);
        output.WriteLine($"PDF 1ra vez: {sw.ElapsedMilliseconds} ms");
        sw.Restart(); await service.GetPdfComprobanteAsync(factura.Tc, factura.Id);
        output.WriteLine($"PDF 2da vez: {sw.ElapsedMilliseconds} ms");
        if (Environment.GetEnvironmentVariable("ALFACORE_MAIL_ENVIAR_A") is { Length: > 0 } destino)
        {
            for (var i = 1; i <= 2; i++)
            {
                sw.Restart();
                var envio = await service.EnviarMailComprobanteAsync(factura.Tc, factura.Id, destino, "[Prueba tiempos AlfaCore] " + borrador.Asunto, borrador.Mensaje);
                output.WriteLine($"Enviar mail {i} (PDF + SMTP + registro): {sw.ElapsedMilliseconds} ms · {envio.Enviado} · {envio.Mensaje}");
            }
        }
    }

    // appsettings.json + appsettings.Development.json del proyecto (la clave de la cuenta no está en git).
    private static Microsoft.Extensions.Configuration.IConfiguration ConfiguracionReal()
    {
        var dir = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(dir, "src/AlfaCore/AlfaCore.csproj"))) dir = Directory.GetParent(dir)!.FullName;
        var src = Path.Combine(dir, "src/AlfaCore");
        return new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddJsonFile(Path.Combine(src, "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(src, "appsettings.Development.json"), optional: true)
            .Build();
    }
}
