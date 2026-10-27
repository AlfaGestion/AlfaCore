using AlfaCore.Components.Pages;
using AlfaCore.Models;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Bug reportado: imágenes rotas en Tickets que, al hacer click, terminaban en una página de
/// login/"Sin base activa". Auditado contra main (76fb1d97):
///
/// 1) "Sin base activa" ya estaba resuelto por el hardening de adjuntos (00b30fb1) -- Tickets.razor ya
///    arma las URLs con idBase+token (GetAttachmentUrl/GetAttachmentDownloadUrl), idéntico al patrón de
///    Conversaciones.razor, así que RestoreUserSessionFromToken siempre tiene con qué restaurar la
///    sesión en el request del adjunto -- no queda ambiente sin sesión. Verificado por código (no se
///    modificó nada de auth/tenant en este commit), sólo se confirma con un test estructural.
///
/// 2) Bug real que SÍ seguía en main: TicketsService leía CONV_ADJUNTOS con una query propia que no
///    traía ArchivoDisponible/PuedeRecuperarse/EstadoAlmacenamiento (a diferencia de
///    ConversacionesService.GetConversationAttachmentsInternalAsync, que sí los calcula). Tickets.razor
///    entonces renderizaba TODA fila de CONV_ADJUNTOS como si el contenido existiera -- incluyendo
///    adjuntos sin contenido (placeholder NO_DISPONIBLE_META, pendientes de recuperación, etc.) -- con
///    un &lt;img&gt; e &lt;a href&gt; que apuntaban al mismo endpoint que iba a devolver 404. El fix
///    reusa IConversacionesService.GetMessageAttachmentsAsync (mismo camino ya endurecido y probado que
///    usa Conversaciones.razor) para poblar esos tres campos, y la UI ahora sólo renderiza el
///    &lt;a href&gt;/&lt;img&gt; navegable cuando ArchivoDisponible es true -- si no, muestra el mismo
///    texto por estado que Conversaciones (reusado, no duplicado).
/// </summary>
public sealed class TicketsAttachmentsSessionTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string TicketsSource = File.ReadAllText(
        Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "Tickets.razor"));
    private static readonly string TicketsServiceSource = File.ReadAllText(
        Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "TicketsService.cs"));

    // --- "Sin base activa": confirmar que el patrón ya endurecido sigue en Tickets -----------

    [Fact]
    public void GetAttachmentUrls_IncludeIdBaseAndSessionToken_SameAsConversaciones()
    {
        Assert.Contains(
            "\"/api/conversaciones/adjuntos/{idAdjunto}?idBase={GetAttachmentBaseId()}{GetAttachmentUserTokenQuery()}\"",
            TicketsSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"/api/conversaciones/adjuntos/{idAdjunto}?download=1&idBase={GetAttachmentBaseId()}{GetAttachmentUserTokenQuery()}\"",
            TicketsSource,
            StringComparison.Ordinal);
        Assert.Contains("AppUserSession.CurrentToken", TicketsSource, StringComparison.Ordinal);
    }

    // --- Imagen rota: ya no navega a una URL que sabemos que va a fallar ----------------------

    [Fact]
    public void ImageAttachment_OnlyRendersClickableLink_WhenArchivoDisponible()
    {
        var block = BlockBetween(TicketsSource, "@if (IsImageAttachment(adj))", "private ");

        var imageBranchStart = block.IndexOf("@if (IsImageAttachment(adj))", StringComparison.Ordinal);
        var archivoDisponibleIndex = block.IndexOf("@if (adj.ArchivoDisponible)", imageBranchStart, StringComparison.Ordinal);
        var anchorIndex = block.IndexOf("<a href=\"@GetAttachmentUrl(adj.IdAdjunto)\" target=\"_blank\"", imageBranchStart, StringComparison.Ordinal);
        var unavailableIndex = block.IndexOf("tickets-attachment--unavailable", imageBranchStart, StringComparison.Ordinal);

        Assert.True(archivoDisponibleIndex > imageBranchStart, "No se encontró el if (adj.ArchivoDisponible) dentro de la rama de imagen.");
        Assert.True(anchorIndex > archivoDisponibleIndex, "El <a>/<img> navegable de imagen debe quedar dentro del if (ArchivoDisponible), no incondicional.");
        Assert.True(unavailableIndex > anchorIndex, "Debe existir un fallback tickets-attachment--unavailable después del <a> navegable.");
    }

    [Fact]
    public void DocumentAttachment_OnlyRendersDownloadLink_WhenArchivoDisponible()
    {
        var block = BlockBetween(TicketsSource, "@if (IsImageAttachment(adj))", "private ");

        var documentBranchStart = block.IndexOf("<a href=\"@GetAttachmentDownloadUrl(adj.IdAdjunto)\"", StringComparison.Ordinal);
        var unavailableIndex = block.IndexOf("tickets-attachment--unavailable", documentBranchStart, StringComparison.Ordinal);

        Assert.True(documentBranchStart >= 0, "No se encontró el <a> de descarga de documento.");
        Assert.True(unavailableIndex > documentBranchStart, "Debe existir un fallback tickets-attachment--unavailable después del <a> de descarga, en la misma rama de documento.");
    }

    // --- Persistencia: ya no arma su propia query de CONV_ADJUNTOS ----------------------------

    [Fact]
    public void TicketsService_ReusesConversacionesServiceForAttachments_DoesNotDuplicateAvailabilityLogic()
    {
        Assert.Contains("conversacionesService.GetMessageAttachmentsAsync(group.Key, messageIds, ct)", TicketsServiceSource, StringComparison.Ordinal);
        Assert.Contains("IConversacionesService conversacionesService", TicketsServiceSource, StringComparison.Ordinal);

        // No debe volver a mano-armar un SELECT propio sobre CONV_ADJUNTOS con columnas de
        // disponibilidad -- eso es justamente lo que quedaba desactualizado.
        Assert.DoesNotContain("INNER JOIN dbo.CONV_ADJUNTOS a ON a.IdMensaje = tm.IdMensaje", TicketsServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void TicketAdjuntoOrigenDto_ExposesAvailabilitySignals()
    {
        var modelsSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Models", "TicketsModels.cs"));
        var block = BlockBetween(modelsSource, "public sealed class TicketAdjuntoOrigenDto", "public sealed class TicketCreateRequest");

        Assert.Contains("public bool ArchivoDisponible { get; set; }", block, StringComparison.Ordinal);
        Assert.Contains("public bool PuedeRecuperarse { get; set; }", block, StringComparison.Ordinal);
        Assert.Contains("public string EstadoAlmacenamiento { get; set; }", block, StringComparison.Ordinal);
    }

    // --- Texto de fallback: mismo resolver que Conversaciones, sin duplicarlo -----------------

    [Fact]
    public void UnavailableText_ContenidoDisponible_NuncaSeUsa_PeroResuelveVacioSiSeLlamaraPorError()
    {
        var adjunto = new TicketAdjuntoOrigenDto { ArchivoDisponible = true };

        Assert.Equal(string.Empty, Tickets.GetAttachmentUnavailableText(adjunto, "imagen"));
    }

    [Fact]
    public void UnavailableText_NoDisponibleMeta_EsDefinitivoYMencionaWhatsApp()
    {
        var adjunto = new TicketAdjuntoOrigenDto
        {
            ArchivoDisponible = false,
            PuedeRecuperarse = false,
            EstadoAlmacenamiento = "NO_DISPONIBLE_META"
        };

        var text = Tickets.GetAttachmentUnavailableText(adjunto, "imagen");

        Assert.Equal("Esta imagen ya no está disponible en WhatsApp.", text);
    }

    [Fact]
    public void UnavailableText_PuedeRecuperarse_NuncaDiceNoDisponible()
    {
        var adjunto = new TicketAdjuntoOrigenDto
        {
            ArchivoDisponible = false,
            PuedeRecuperarse = true,
            EstadoAlmacenamiento = string.Empty
        };

        var text = Tickets.GetAttachmentUnavailableText(adjunto, "documento");

        Assert.Equal("Archivo histórico pendiente de recuperación.", text);
        Assert.DoesNotContain("no disponible", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnavailableText_RecuperacionFallidaLegacy_NoEsDefinitivo()
    {
        var adjunto = new TicketAdjuntoOrigenDto
        {
            ArchivoDisponible = false,
            PuedeRecuperarse = false,
            EstadoAlmacenamiento = "RECUPERACION_FALLIDA"
        };

        var text = Tickets.GetAttachmentUnavailableText(adjunto, "documento");

        Assert.Equal("No se pudo recuperar el archivo por ahora.", text);
        Assert.DoesNotContain("WhatsApp", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnavailableText_SinReferencia_NoLeAtribuyeLaPerdidaAWhatsApp()
    {
        var adjunto = new TicketAdjuntoOrigenDto
        {
            ArchivoDisponible = false,
            PuedeRecuperarse = false,
            EstadoAlmacenamiento = string.Empty
        };

        var text = Tickets.GetAttachmentUnavailableText(adjunto, "documento");

        Assert.Equal("No se pudo recuperar este archivo histórico.", text);
    }

    // --- Tenant: no se introdujo ningún idBase/conexión nuevos --------------------------------

    [Fact]
    public void Fix_DoesNotIntroduceNewConnectionOrArbitraryIdBase()
    {
        Assert.DoesNotContain("SqlConnectionStringBuilder", TicketsServiceSource, StringComparison.Ordinal);
        Assert.Contains("ISessionService sessionService", TicketsServiceSource, StringComparison.Ordinal);
    }

    private static string BlockBetween(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"No se encontro inicio: {start}");
        var endIndex = string.IsNullOrEmpty(end)
            ? source.Length
            : source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"No se encontro fin: {end}");
        return source[startIndex..endIndex];
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}
