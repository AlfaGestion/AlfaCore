using AlfaCore.Components.Pages;
using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Prioridad 7 de la auditoría UX: bug real confirmado por código (no backend, sólo atribución de
/// autor). Un mensaje reflejado de smb_message_echoes (Origen=WHATSAPP_BUSINESS_APP en InsertMessageAsync)
/// se inserta con UsuarioAutor/SistemaAutor/IdTecnicoAutor vacíos a propósito -- correcto, porque
/// AlfaCore genuinamente no sabe qué persona del negocio lo mandó desde el teléfono. Pero
/// GetMessageAuthor (Conversaciones.razor) no distinguía ese caso del de un mensaje SALIENTE normal:
/// ambos caían al mismo fallback "Equipo Alfa", atribuyendo incorrectamente al soporte de AlfaCore un
/// mensaje que en realidad mandó el propio negocio desde la app de WhatsApp Business. Además, Origen
/// se persistía (columna real, self-healing) pero nunca se leía de vuelta hacia el cliente -- la UI no
/// tenía ninguna forma de saber que un mensaje era un echo.
/// </summary>
public sealed class WhatsAppEchoAuthorAttributionTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ConversacionMensajeDto_ExposesOrigen_ToTheClient()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Models", "ConversacionesModels.cs"));
        var classStart = source.IndexOf("public sealed class ConversacionMensajeDto", StringComparison.Ordinal);
        Assert.True(classStart >= 0);
        var classBody = ExtractBody(source, classStart);
        Assert.Contains("public string Origen { get; set; } = string.Empty;", classBody, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMessagesPageAsync_SelectsAndMapsOrigen_SoTheClientActuallyReceivesIt()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        var methodStart = source.IndexOf("public Task<ConversacionMensajesPaginaDto> GetMessagesPageAsync(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodBody = ExtractBody(source, methodStart);

        Assert.Contains("ISNULL(m.Origen, '') AS Origen", methodBody, StringComparison.Ordinal);
        Assert.Contains("page.Origen", methodBody, StringComparison.Ordinal);
        Assert.Contains("Origen = GetString(rd, 17)", methodBody, StringComparison.Ordinal);
        // Si esta base tenant nunca recibió un mensaje de history/echo, la columna Origen podría no
        // existir todavía -- sin este self-heal, el SELECT que ahora la lee rompería la carga de
        // mensajes completa para esos tenants.
        Assert.Contains("await EnsureMensajeOrigenColumnAsync(cn, token);", methodBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// Auditoría de call-sites (segunda ronda): GetMessagesAsync (variante NO paginada) es un endpoint
    /// HTTP real y registrado -- GET /api/conversaciones/{id}/mensajes (Program.cs) -- no código muerto.
    /// Ningún JS de este repo lo llama (grep en wwwroot sin resultados), así que no es la ruta que usa
    /// la página Blazor de Conversaciones (ésa usa GetMessagesPageAsync vía DI directa, ya corregida),
    /// pero es una ruta real y alcanzable para cualquier consumidor externo -- por eso se corrige igual,
    /// con el mismo patrón exacto (SELECT + self-heal + mapeo), no se deja como gap documentado.
    /// </summary>
    [Fact]
    public void GetMessagesAsync_NonPagedRestEndpoint_AlsoSelectsAndMapsOrigen()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));
        var methodStart = source.IndexOf("public Task<IReadOnlyList<ConversacionMensajeDto>> GetMessagesAsync(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodBody = ExtractBody(source, methodStart);

        Assert.Contains("ISNULL(m.Origen, '')", methodBody, StringComparison.Ordinal);
        Assert.Contains("Origen = GetString(rd, 17)", methodBody, StringComparison.Ordinal);
        Assert.Contains("await EnsureMensajeOrigenColumnAsync(cn, token);", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMessagesAsync_IsARealRegisteredHttpEndpoint_NotDeadCode()
    {
        var programSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Program.cs"));
        Assert.Contains("""app.MapGet("/api/conversaciones/{id:long}/mensajes", async (""", programSource, StringComparison.Ordinal);
        Assert.Contains("svc.GetMessagesAsync(id, ct)", programSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Los otros dos call-sites de "new ConversacionMensajeDto" en ConversacionesService.cs quedan
    /// documentados y SIN tocar, con evidencia de por qué no aplican acá:
    /// - GetPendingMediaHydrationAsync: sólo ENTRANTE (WHERE Direction='ENTRANTE'), un echo es siempre
    ///   SALIENTE -- estructuralmente no puede alcanzar un mensaje de echo.
    /// - El helper de contexto para IA (histórico de conversación para el asistente): no llena
    ///   UsuarioAutor/TecnicoAutorNombre en absoluto (sólo Direction/Texto/FechaHora) y no se renderiza
    ///   nunca como "quién lo mandó" en ninguna UI -- no hay atribución de autor que corregir ahí.
    /// </summary>
    [Fact]
    public void OtherConversacionMensajeDtoCallSites_StructurallyCannotReachAnEchoAuthorBug()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));

        var hydrationStart = source.IndexOf("private async Task<List<PendingMediaHydration>> GetPendingMediaHydrationAsync(", StringComparison.Ordinal);
        Assert.True(hydrationStart >= 0, "No se encontró GetPendingMediaHydrationAsync.");
        var hydrationBody = ExtractBody(source, hydrationStart);
        Assert.Contains("UPPER(ISNULL(m.Direction, '')) = N'ENTRANTE'", hydrationBody, StringComparison.Ordinal);
    }

    // GetMessageAuthor delega en ResolveNonIncomingMessageAuthor (internal static, extraído a propósito
    // para poder testear estos casos de verdad en vez de con regex sobre el código fuente) para todo lo
    // que no sea ENTRANTE (ENTRANTE depende de CurrentConversationContactName, estado de instancia).

    [Fact]
    public void PaginatedEcho_IsAttributedToWhatsAppBusiness_NotAlfaCoreTeam()
    {
        var echo = new ConversacionMensajeDto
        {
            Direction = "SALIENTE",
            Origen = "WHATSAPP_BUSINESS_APP",
            UsuarioAutor = string.Empty,
            TecnicoAutorNombre = string.Empty
        };

        Assert.Equal("WhatsApp Business", Conversaciones.ResolveNonIncomingMessageAuthor(echo));
    }

    [Fact]
    public void HistoryImportedOutgoingMessage_IsAlsoAttributedToWhatsAppBusiness_NeverConfusedWithAnAlfaCoreMessage()
    {
        // "history no se confunde con echo": ambos representan lo mismo (el negocio mandó esto
        // directamente desde la app, no AlfaCore) y ambos deben mostrar la misma etiqueta -- confirmado
        // que esto YA NO pasaba antes de este fix (HISTORY no estaba en la lista, sólo
        // WHATSAPP_BUSINESS_APP, así que un SALIENTE de historial cai­a al "Equipo Alfa" incorrecto).
        var historical = new ConversacionMensajeDto
        {
            Direction = "SALIENTE",
            Origen = "HISTORY",
            UsuarioAutor = string.Empty,
            TecnicoAutorNombre = string.Empty
        };

        Assert.Equal("WhatsApp Business", Conversaciones.ResolveNonIncomingMessageAuthor(historical));
    }

    [Fact]
    public void NormalAlfaCoreMessage_KeepsItsRealAuthor_NeverOverriddenByTheEchoFallback()
    {
        var normal = new ConversacionMensajeDto
        {
            Direction = "SALIENTE",
            Origen = string.Empty,
            UsuarioAutor = "jperez",
            TecnicoAutorNombre = "Juan Pérez"
        };

        Assert.Equal("Juan Pérez", Conversaciones.ResolveNonIncomingMessageAuthor(normal));
    }

    [Fact]
    public void NormalAlfaCoreMessageWithNoAttachedTechnician_FallsBackToEquipoAlfa_NotWhatsAppBusiness()
    {
        var normal = new ConversacionMensajeDto
        {
            Direction = "SALIENTE",
            Origen = string.Empty,
            UsuarioAutor = string.Empty,
            TecnicoAutorNombre = string.Empty
        };

        Assert.Equal("Equipo Alfa", Conversaciones.ResolveNonIncomingMessageAuthor(normal));
    }

    [Fact]
    public void RealAuthor_StillTakesPriorityOverTheWhatsAppBusinessFallback_ForEchoOrHistory()
    {
        // Si por algún motivo un echo/history SÍ trajera un autor real (caso hoy no esperado pero
        // defensivo), no debe perderse detrás de la etiqueta genérica.
        var echoWithKnownAuthor = new ConversacionMensajeDto
        {
            Direction = "SALIENTE",
            Origen = "WHATSAPP_BUSINESS_APP",
            UsuarioAutor = "dueño-del-negocio",
            TecnicoAutorNombre = string.Empty
        };

        Assert.Equal("dueño-del-negocio", Conversaciones.ResolveNonIncomingMessageAuthor(echoWithKnownAuthor));
    }

    [Fact]
    public void NotaInterna_IsUnaffectedByOrigen_KeepsItsOwnFallback()
    {
        var nota = new ConversacionMensajeDto
        {
            Direction = "NOTA_INTERNA",
            Origen = string.Empty,
            UsuarioAutor = string.Empty,
            TecnicoAutorNombre = string.Empty
        };

        Assert.Equal("Nota interna", Conversaciones.ResolveNonIncomingMessageAuthor(nota));
    }

    private static string ExtractBody(string source, int start, char until = '}')
    {
        if (until == ';')
        {
            var semicolon = source.IndexOf(';', start);
            Assert.True(semicolon >= 0);
            return source[start..(semicolon + 1)];
        }

        var openBrace = source.IndexOf('{', start);
        Assert.True(openBrace >= 0);
        var depth = 0;
        for (var i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(i + 1)];
            }
        }
        throw new InvalidOperationException("No se pudo delimitar el cuerpo.");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AlfaCore.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se pudo ubicar la raíz del repositorio (AlfaCore.sln) desde el directorio de pruebas.");
    }
}
