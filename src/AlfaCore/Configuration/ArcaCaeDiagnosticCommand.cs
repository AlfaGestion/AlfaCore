using AlfaCore.Models;
using AlfaCore.Services;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Configuration;

/// <summary>
/// Modo one-shot <c>--test-arca-cae</c>: prueba manual de la Fase 1 de facturación electrónica AFIP/
/// ARCA (WSAA + WSFEv1) contra una base real, SIN pasar por PuntoVentaService. Nunca escribe en
/// V_MV_Cpte/V_MV_CPTE_ELECTRONICOS -- solo autentica, consulta el último autorizado y, si se pide
/// explícitamente con --solicitar-cae, pide un CAE de prueba (comprobante de monto chico, Consumidor
/// Final) para verificar el circuito completo antes de integrarlo al POS.
/// </summary>
internal static class ArcaCaeDiagnosticCommand
{
    public const string Verb = "--test-arca-cae";

    public static bool IsRequested(IReadOnlyList<string> args)
        => args.Any(a => string.Equals(a, Verb, StringComparison.OrdinalIgnoreCase));

    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        var connectionString = ReadOption(args, "--connection-string");
        var uNegocio = ReadOption(args, "--unegocio");
        var cbteTipoArg = ReadOption(args, "--cbte-tipo");
        var solicitarCae = args.Any(a => string.Equals(a, "--solicitar-cae", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Uso: AlfaCore --test-arca-cae --connection-string \"<cadena de conexión>\" [--unegocio <codigo>] [--solicitar-cae] [--cbte-tipo <n>]");
            return 1;
        }

        var cbteTipo = int.TryParse(cbteTipoArg, out var parsedTipo) && parsedTipo > 0 ? parsedTipo : 6; // 6 = Factura B

        var appEvents = new NullAppEventService();
        var configService = new ArcaConfigService(appEvents, new OneShotSessionService(), NullConfiguration());
        using var httpClient = new HttpClient();
        var clientFactory = new SingleClientFactory(httpClient);
        var wsaaClient = new WsaaClient(clientFactory, appEvents);
        var wsfeClient = new Wsfev1Client(clientFactory);

        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);

        output.WriteLine("== 1) Resolviendo emisor ==");
        var emisor = await configService.ResolveEmisorAsync(cn, uNegocio, ct);
        if (emisor is null)
        {
            output.WriteLine("La facturación electrónica no está habilitada o no tiene certificado/PV configurado (UsaFCElectronica, V_TA_UnidadNegocio o WSFE_CUIT/PV_EFACTURA + certificado subido en dbo.ARCA_CERTIFICADO).");
            return 1;
        }
        output.WriteLine($"CUIT={emisor.Cuit} RazonSocial={emisor.RazonSocial} Ambiente={emisor.Ambiente} PtoVta={emisor.PuntoVentaElectronico} (certificado OK, {emisor.CertificadoPem.Length} bytes de PEM)");

        output.WriteLine("== 2) Autenticando WSAA ==");
        var ticket = await wsaaClient.ObtenerTicketAsync(cn, emisor, ct);
        output.WriteLine($"Ticket obtenido. Vence (UTC): {ticket.ExpirationTimeUtc:yyyy-MM-dd HH:mm:ss}");

        output.WriteLine("== 3) Consultando último autorizado (WSFEv1) ==");
        var ultimo = await wsfeClient.ObtenerUltimoAutorizadoAsync(ticket, emisor.Cuit, emisor.PuntoVentaElectronico, cbteTipo, emisor.Ambiente, ct);
        output.WriteLine($"Último autorizado para PtoVta={emisor.PuntoVentaElectronico} CbteTipo={cbteTipo}: {ultimo}");

        if (!solicitarCae)
        {
            output.WriteLine("(No se pidió --solicitar-cae: no se solicitó ningún CAE de prueba.)");
            return 0;
        }

        output.WriteLine("== 4) Solicitando CAE de prueba (Consumidor Final, monto chico) ==");
        var proximoNumero = ultimo + 1;
        var solicitud = new ArcaCaeSolicitudDto(
            Cuit: emisor.Cuit,
            PtoVta: emisor.PuntoVentaElectronico,
            CbteTipo: cbteTipo,
            Concepto: 1,
            DocTipo: ArcaCodigosAfip.DocTipoConsumidorFinal,
            DocNro: "0",
            CbteFch: DateTime.Today,
            CbteDesde: proximoNumero,
            CbteHasta: proximoNumero,
            ImpTotal: 121.00m,
            ImpTotConc: 0m,
            ImpNeto: 100.00m,
            ImpOpEx: 0m,
            ImpTrib: 0m,
            ImpIva: 21.00m,
            CondicionIvaReceptorId: 5,
            MonId: "PES",
            MonCotiz: 1m,
            Ivas: [new ArcaIvaAlicuotaDto(ArcaCodigosAfip.ResolverIdAlicuotaIva(21m), 100.00m, 21.00m)]);

        var resultado = await wsfeClient.SolicitarCaeAsync(ticket, solicitud, emisor.Ambiente, ct);
        output.WriteLine($"ExitoTecnico={resultado.ExitoTecnico} Resultado={resultado.Resultado} Aprobado={resultado.Aprobado}");
        if (resultado.Aprobado)
        {
            output.WriteLine($"CAE={resultado.Cae} Vto={resultado.CaeVto:yyyy-MM-dd} CbteDesde={resultado.CbteDesde}");
        }
        else
        {
            output.WriteLine($"ErrorTecnico={resultado.ErrorTecnico}");
            foreach (var obs in resultado.Observaciones)
                output.WriteLine($"  Obs {obs.Codigo}: {obs.Mensaje}");
        }

        return resultado.Aprobado ? 0 : 2;
    }

    private static string? ReadOption(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class NullAppEventService : IAppEventService
    {
        public Task<string> LogErrorAsync(string module, string action, Exception exception, string userMessage, object? data = null, AppEventSeverity severity = AppEventSeverity.Error, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid().ToString());

        public Task<string> LogAuditAsync(string module, string action, string entityType, string entityId, string message, object? data = null, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid().ToString());

        public Task<Guid> WriteAuditAsync(AuditWriteRequest request, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<Guid> WriteAuditAsync(AuditWriteRequest request, SqlConnection connection, SqlTransaction transaction, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<AuditActivityPageDto> GetActivityAsync(string entityType, string recordId, int pageNumber = 1, int pageSize = 20, CancellationToken ct = default)
            => Task.FromResult(new AuditActivityPageDto());

        public Task<AuditSchemaAvailabilityDto> CheckAuditAvailabilityAsync(CancellationToken ct = default)
            => Task.FromResult(new AuditSchemaAvailabilityDto());

        public Task<AuditSchemaAvailabilityDto> CheckAuditAvailabilityAsync(SqlConnection connection, SqlTransaction? transaction = null, CancellationToken ct = default)
            => Task.FromResult(new AuditSchemaAvailabilityDto());
    }

    /// <summary>Solo para satisfacer el constructor de ArcaConfigService -- ResolveEmisorAsync recibe
    /// la conexión por parámetro y nunca usa ISessionService/IConfiguration en este flujo.</summary>
    private sealed class OneShotSessionService : ISessionService
    {
        public string GetConnectionString() => string.Empty;
        public AlfaCore.Models.SessionDto? GetActiveSession() => null;
        public void SetWebhookOverride(AlfaCore.Models.SessionDto session) { }
        public void ClearWebhookOverride() { }
        public IReadOnlyList<AlfaCore.Models.SessionDto> GetAllSessions() => [];
        public void SwitchSession(Guid id) { }
        public Guid AddSession(string nombre, string servidor, string baseDatos, string usuario, string password) => Guid.Empty;
        public void UpdateSession(Guid id, string nombre, string servidor, string baseDatos, string usuario, string password) { }
        public void DeleteSession(Guid id) { }
        public void ClearActiveSession() { }
        public event Action? SessionChanged { add { } remove { } }
    }

    private static IConfiguration NullConfiguration() => new ConfigurationBuilder().Build();
}
