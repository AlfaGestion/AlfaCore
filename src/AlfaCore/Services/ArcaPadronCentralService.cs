using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Consulta central del padrón ARCA (CUIT y DNI) con el certificado PADRON de la base central de ALFA NET
/// (conexión AlfaGestion = ALFANET2007, CUIT 27228073259). La usan las bases sin certificado propio y el
/// VB6 por /api/vb6/padron. Un solo ticket compartido, resultados guardados <c>ArcaPadron:DiasCache</c>
/// días (7), cada consulta registrada y un límite diario de consultas a ARCA por origen.
/// </summary>
public sealed class ArcaPadronCentralService(IArcaConfigService arcaConfig, IWsaaClient wsaa,
    IHttpClientFactory httpClientFactory, IConfiguration configuration, IAppEventService appEvents)
{
    private const string ModuleName = "ArcaPadronCentral";
    private const int MaximoCuitsPorDni = 5;
    private static volatile bool _esquemaListo;
    private static readonly SemaphoreSlim EsquemaLock = new(1, 1);

    private string ConnectionString => configuration.GetConnectionString("AlfaGestion") ?? string.Empty;
    private int DiasCache => Math.Max(0, configuration.GetValue("ArcaPadron:DiasCache", 7));
    private int LimiteDiario => Math.Max(1, configuration.GetValue("ArcaPadron:LimiteDiarioPorOrigen", 1000));

    public bool Disponible => ConnectionString.Length > 0;

    public async Task<ArcaPadronPersonaDto> ConsultarCuitAsync(string cuit, string origen, string usuario, CancellationToken ct = default)
    {
        var digitos = ArcaPadronSoap.SoloDigitos(cuit);
        if (digitos.Length != 11) throw new InvalidOperationException("Ingresá un CUIT válido de 11 dígitos para consultar ARCA.");
        await using var cn = await AbrirAsync(ct);
        return await ConsultarCuitAsync(cn, digitos, Origen(origen), Usuario(usuario), ct);
    }

    /// <summary>Busca en ARCA los CUIT de un DNI (7 u 8 dígitos) y devuelve los datos de cada uno.</summary>
    public async Task<IReadOnlyList<ArcaPadronPersonaDto>> ConsultarDniAsync(string dni, string origen, string usuario, CancellationToken ct = default)
    {
        var digitos = ArcaPadronSoap.SoloDigitos(dni);
        if (digitos.Length is < 7 or > 8) throw new InvalidOperationException("Ingresá un DNI de 7 u 8 dígitos para buscarlo en ARCA.");
        origen = Origen(origen); usuario = Usuario(usuario);
        await using var cn = await AbrirAsync(ct);
        var cuits = await LeerCacheAsync<List<string>>(cn, "DNI", digitos, ct);
        if (cuits is not null) await RegistrarAsync(cn, origen, usuario, "DNI", digitos, "CACHE", null);
        else
        {
            await ControlarLimiteAsync(cn, origen, ct);
            try
            {
                var (emisor, ticket) = await TicketAsync(cn, ArcaPadronSoap.ServicioDni, ct);
                cuits = (await ArcaPadronSoap.CuitsPorDniAsync(Http, ticket, emisor, digitos, ct)).ToList();
                await GuardarCacheAsync(cn, "DNI", digitos, cuits, ct);
                await RegistrarAsync(cn, origen, usuario, "DNI", digitos, "ARCA", $"{cuits.Count} CUIT");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await RegistrarAsync(cn, origen, usuario, "DNI", digitos, "ERROR", ex.Message);
                throw await TraducirAsync(ex, ArcaPadronSoap.ServicioDni, new { Dni = digitos, Origen = origen }, ct);
            }
        }
        var personas = new List<ArcaPadronPersonaDto>();
        foreach (var c in cuits.Take(MaximoCuitsPorDni))
            personas.Add(await ConsultarCuitAsync(cn, c, origen, usuario, ct));
        return personas;
    }

    private async Task<ArcaPadronPersonaDto> ConsultarCuitAsync(SqlConnection cn, string cuit, string origen, string usuario, CancellationToken ct)
    {
        var guardada = await LeerCacheAsync<ArcaPadronPersonaDto>(cn, "CUIT", cuit, ct);
        if (guardada is not null)
        {
            await RegistrarAsync(cn, origen, usuario, "CUIT", cuit, "CACHE", null);
            return guardada;
        }
        await ControlarLimiteAsync(cn, origen, ct);
        try
        {
            var (emisor, ticket) = await TicketAsync(cn, ArcaPadronSoap.ServicioCuit, ct);
            var persona = await ArcaPadronSoap.ConsultarCuitAsync(Http, ticket, emisor, cuit, ct);
            await GuardarCacheAsync(cn, "CUIT", cuit, persona, ct);
            await RegistrarAsync(cn, origen, usuario, "CUIT", cuit, "ARCA", null);
            return persona;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RegistrarAsync(cn, origen, usuario, "CUIT", cuit, "ERROR", ex.Message);
            throw await TraducirAsync(ex, ArcaPadronSoap.ServicioCuit, new { Cuit = cuit, Origen = origen }, ct);
        }
    }

    /// <summary>Datos del certificado central para mostrar su estado (vencimiento) en administración.</summary>
    public async Task<(string Cuit, DateTime? Vence)> EstadoCertificadoAsync(CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ct);
        var emisor = await arcaConfig.ResolvePadronEmisorAsync(cn, ct)
            ?? throw new InvalidOperationException("La base central no tiene certificado PADRON.");
        return (ArcaPadronSoap.CuitRepresentada(emisor), ArcaPadronSoap.Vencimiento(emisor));
    }

    private HttpClient Http => httpClientFactory.CreateClient("Arca");

    private async Task<(ArcaEmisorConfig Emisor, WsaaTicket Ticket)> TicketAsync(SqlConnection cn, string servicio, CancellationToken ct)
    {
        var emisor = await arcaConfig.ResolvePadronEmisorAsync(cn, ct)
            ?? throw new InvalidOperationException("La consulta central de ARCA no tiene certificado configurado.");
        return (emisor, await wsaa.ObtenerTicketAsync(cn, emisor, ct, servicio));
    }

    private async Task<InvalidOperationException> TraducirAsync(Exception ex, string servicio, object datos, CancellationToken ct)
    {
        if (ex is InvalidOperationException { InnerException: null } propia && ex is not ArcaAutenticacionException)
            return propia; // mensajes de negocio ya legibles ("ARCA no pudo informar el CUIT: ...")
        await appEvents.LogErrorAsync(ModuleName, "Consultar", ex, "No se pudo consultar el padrón ARCA central.", datos, ct: ct);
        var texto = ex.ToString();
        if (texto.Contains("coe.notAuthorized", StringComparison.OrdinalIgnoreCase) || texto.Contains("no autorizado", StringComparison.OrdinalIgnoreCase))
            return new InvalidOperationException($"El certificado central no está autorizado en ARCA para {servicio}. Hay que habilitarlo en el Administrador de Relaciones.", ex);
        if (texto.Contains("cms.cert.expired", StringComparison.OrdinalIgnoreCase) || texto.Contains("expirado", StringComparison.OrdinalIgnoreCase))
            return new InvalidOperationException("El certificado central de consulta de ARCA está vencido. Hay que renovarlo en la base central.", ex);
        if (ex is ArcaAutenticacionException auth && auth.InnerException is null)
            return new InvalidOperationException(auth.Message, ex);
        return new InvalidOperationException("No se pudo consultar ARCA en este momento. Probá de nuevo en unos minutos.", ex);
    }

    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        if (!Disponible) throw new InvalidOperationException("Este servidor no tiene configurada la consulta central de ARCA.");
        var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        if (!_esquemaListo)
        {
            await EsquemaLock.WaitAsync(ct);
            try
            {
                if (!_esquemaListo)
                {
                    await cn.ExecuteAsync(new CommandDefinition(ArcaPadronCentralSchema.Script, commandTimeout: 120, cancellationToken: ct));
                    _esquemaListo = true;
                }
            }
            finally { EsquemaLock.Release(); }
        }
        return cn;
    }

    private async Task ControlarLimiteAsync(SqlConnection cn, string origen, CancellationToken ct)
    {
        var usadas = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.ARCA_PADRON_CONSULTA
            WHERE Origen = @Origen AND Resultado = 'ARCA' AND FechaUtc >= DATEADD(day, -1, GETUTCDATE());
            """, new { Origen = origen }, cancellationToken: ct));
        if (usadas >= LimiteDiario)
            throw new InvalidOperationException($"Se alcanzó el límite de {LimiteDiario} consultas a ARCA en 24 horas para esta instalación. Las consultas ya hechas siguen disponibles.");
    }

    private async Task<T?> LeerCacheAsync<T>(SqlConnection cn, string tipo, string valor, CancellationToken ct) where T : class
    {
        if (DiasCache == 0) return null;
        var json = await cn.ExecuteScalarAsync<string?>(new CommandDefinition("""
            SELECT Datos FROM dbo.ARCA_PADRON_CACHE WHERE Tipo = @Tipo AND Valor = @Valor AND FechaUtc >= DATEADD(day, -@Dias, GETUTCDATE());
            """, new { Tipo = tipo, Valor = valor, Dias = DiasCache }, cancellationToken: ct));
        try { return json is null ? null : JsonSerializer.Deserialize<T>(json); }
        catch (JsonException) { return null; }
    }

    private static Task GuardarCacheAsync<T>(SqlConnection cn, string tipo, string valor, T datos, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.ARCA_PADRON_CACHE SET Datos = @Datos, FechaUtc = GETUTCDATE() WHERE Tipo = @Tipo AND Valor = @Valor;
            IF @@ROWCOUNT = 0 INSERT INTO dbo.ARCA_PADRON_CACHE (Tipo, Valor, Datos, FechaUtc) VALUES (@Tipo, @Valor, @Datos, GETUTCDATE());
            """, new { Tipo = tipo, Valor = valor, Datos = JsonSerializer.Serialize(datos) }, cancellationToken: ct));

    private async Task RegistrarAsync(SqlConnection cn, string origen, string usuario, string tipo, string valor, string resultado, string? mensaje)
    {
        try
        {
            await cn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.ARCA_PADRON_CONSULTA (FechaUtc, Origen, Usuario, Tipo, Valor, Resultado, Mensaje)
                VALUES (GETUTCDATE(), @Origen, @Usuario, @Tipo, @Valor, @Resultado, @Mensaje);
                """, new { Origen = origen, Usuario = usuario, Tipo = tipo, Valor = valor, Resultado = resultado,
                    Mensaje = mensaje is null ? null : mensaje.Length > 400 ? mensaje[..400] : mensaje }, cancellationToken: CancellationToken.None));
        }
        catch (SqlException ex)
        {
            // El registro no debe impedir la consulta.
            await appEvents.LogErrorAsync(ModuleName, "Registrar", ex, "No se pudo registrar la consulta de padrón.", new { origen, tipo }, ct: CancellationToken.None);
        }
    }

    private static string Origen(string origen) => string.IsNullOrWhiteSpace(origen) ? "desconocido" : origen.Trim()[..Math.Min(150, origen.Trim().Length)];
    private static string Usuario(string usuario) => (usuario ?? string.Empty).Trim() is { Length: > 60 } u ? u[..60] : (usuario ?? string.Empty).Trim();
}

/// <summary>
/// Tablas de la consulta central de padrón en ALFANET2007 (no en las bases de clientes). Se aplica sola la
/// primera vez que se usa. Compatibility level 100: sin THROW. La de tickets también va en App_Data/updates
/// para las bases que consultan con certificado propio.
/// </summary>
public static class ArcaPadronCentralSchema
{
    internal const string Script = """
        IF OBJECT_ID(N'dbo.ARCA_WSAA_TICKET_SERVICIO', N'U') IS NULL
            CREATE TABLE dbo.ARCA_WSAA_TICKET_SERVICIO
            (
                Cuit nvarchar(11) NOT NULL,
                Ambiente nvarchar(20) NOT NULL,
                Servicio nvarchar(60) NOT NULL,
                Certificado nvarchar(32) NOT NULL,
                Token nvarchar(max) NOT NULL,
                Sign nvarchar(max) NOT NULL,
                FechaGeneracionUtc datetime NOT NULL,
                FechaExpiracionUtc datetime NOT NULL,
                CONSTRAINT PK_ARCA_WSAA_TICKET_SERVICIO PRIMARY KEY CLUSTERED (Cuit, Ambiente, Servicio)
            );

        IF OBJECT_ID(N'dbo.ARCA_PADRON_CACHE', N'U') IS NULL
            CREATE TABLE dbo.ARCA_PADRON_CACHE
            (
                Tipo nvarchar(4) NOT NULL,
                Valor nvarchar(11) NOT NULL,
                Datos nvarchar(max) NOT NULL,
                FechaUtc datetime NOT NULL,
                CONSTRAINT PK_ARCA_PADRON_CACHE PRIMARY KEY CLUSTERED (Tipo, Valor)
            );

        IF OBJECT_ID(N'dbo.ARCA_PADRON_CONSULTA', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ARCA_PADRON_CONSULTA
            (
                Id bigint IDENTITY(1,1) NOT NULL,
                FechaUtc datetime NOT NULL,
                Origen nvarchar(150) NOT NULL,
                Usuario nvarchar(60) NULL,
                Tipo nvarchar(4) NOT NULL,
                Valor nvarchar(11) NOT NULL,
                Resultado nvarchar(10) NOT NULL,
                Mensaje nvarchar(400) NULL,
                CONSTRAINT PK_ARCA_PADRON_CONSULTA PRIMARY KEY CLUSTERED (Id)
            );
            CREATE INDEX IX_ARCA_PADRON_CONSULTA_Origen ON dbo.ARCA_PADRON_CONSULTA (Origen, FechaUtc);
        END;
        """;
}
