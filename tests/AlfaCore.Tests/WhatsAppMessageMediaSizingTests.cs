using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Evidencia visual real (capturas del usuario, validación manual en navegador): imágenes/stickers
/// históricos aparecían ocupando bloques excesivamente grandes del hilo de Conversaciones.
///
/// ROOT CAUSE auditado: había un único renderer real (<img> dentro de .message-attachment--image,
/// Conversaciones.razor ~línea 1722) usado uniformemente para mensajes en vivo E historial -- no hay un
/// renderer de historial separado. El límite de tamaño SÍ existía en CSS (tanto en app.css como en el
/// CSS aislado Conversaciones.razor.css, éste último con !important y mayor especificidad por el
/// atributo de scope que agrega Blazor -- por eso es el que efectivamente gana), pero:
/// (a) sticker e imagen normal compartían exactamente la misma clase/límite (280x260px antes de este
///     fix) -- no había ninguna variante más chica para sticker, así que un sticker se veía tan grande
///     como una foto real.
/// (b) los números vigentes no coincidían con el rango pedido (320-360 imagen / 140-180 sticker).
/// (c) existían valores VIEJOS y contradictorios en app.css (170x118 sin !important, superseded pero
///     confusos para cualquiera que audite ese archivo esperando que sean los vigentes).
///
/// Estos tests fijan por código los valores/():selectores nuevos para que una futura regresión en
/// cualquiera de los dos archivos CSS se note en el pipeline, ya que no hay bUnit en este repo para
/// medir el layout real en un navegador headless.
/// </summary>
public sealed class WhatsAppMessageMediaSizingTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string RazorSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "Conversaciones.razor"));
    private static readonly string ScopedCss = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "Conversaciones.razor.css"));
    private static readonly string AppCss = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "AlfaCore", "wwwroot", "app.css"));

    [Fact]
    public void OnlyOneRealImageRendererExists_HistoryAndLiveMessagesShareIt()
    {
        // Confirma el ROOT CAUSE: no hay un componente/markup separado para historial. Si algún día se
        // agrega uno, este test se rompe y obliga a auditar también sus límites de tamaño.
        var occurrences = CountOccurrences(RazorSource, "<img src=\"@GetAttachmentPreviewUrl(adj)\"");
        Assert.Equal(1, occurrences);

        // Confirma que ese único renderer está gateado únicamente por TieneAdjuntos/IsImageAttachment
        // -- nunca por el origen del mensaje (Origen=HISTORY vs. en vivo no participa acá).
        var gateIndex = RazorSource.IndexOf("message.TieneAdjuntos && _adjuntosPorMensaje.TryGetValue(message.IdMensaje, out var adjuntos)", StringComparison.Ordinal);
        Assert.True(gateIndex >= 0);
    }

    [Fact]
    public void StickerAttachment_GetsADistinctCssClass_SeparateFromPlainImage()
    {
        // ROOT CAUSE (a): antes IsStickerAttachment sólo controlaba el botón de "guardar favorito" --
        // el <div> contenedor usaba la MISMA clase que cualquier imagen, sin distinción de tamaño.
        Assert.Contains(
            "<div class=\"message-attachment message-attachment--image @(IsStickerAttachment(adj) ? \"message-attachment--sticker\" : string.Empty)\">",
            RazorSource,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".conversations-page--odoo .message-attachment--image img", "max-width: min(340px, 100%) !important;")]
    [InlineData(".conversations-page--odoo .message-attachment--image img", "max-height: 320px !important;")]
    [InlineData(".conversations-page--odoo .message-attachment--sticker img", "max-width: min(160px, 100%) !important;")]
    [InlineData(".conversations-page--odoo .message-attachment--sticker img", "max-height: 160px !important;")]
    public void ScopedCss_HasTheExpectedSizeConstraint(string selector, string declaration)
    {
        var block = ExtractRuleBlock(ScopedCss, selector);
        Assert.Contains(declaration, block, StringComparison.Ordinal);
    }

    [Fact]
    public void Image_UsesAutoWidthAndHeight_NeverStretchedByAFixedDimension()
    {
        var block = ExtractRuleBlock(ScopedCss, ".conversations-page--odoo .message-attachment--image img");
        Assert.Contains("width: auto !important;", block, StringComparison.Ordinal);
        Assert.Contains("height: auto !important;", block, StringComparison.Ordinal);
        Assert.Contains("object-fit: contain !important;", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Sticker_PreservesAspectRatioAndTransparency_ObjectFitContain()
    {
        var block = ExtractRuleBlock(ScopedCss, ".conversations-page--odoo .message-attachment--sticker img");
        Assert.Contains("object-fit: contain !important;", block, StringComparison.Ordinal);
        Assert.Contains("width: auto !important;", block, StringComparison.Ordinal);
        Assert.Contains("height: auto !important;", block, StringComparison.Ordinal);
    }

    [Fact]
    public void StickerRule_IsDeclaredAfterImageRule_SoItWinsTheSpecificityTie()
    {
        // Ambos selectores tienen exactamente la misma especificidad (mismo patrón: 2 clases + img +
        // [scope]) -- un empate de especificidad lo rompe el ORDEN de declaración, la última gana. Si
        // algún día la regla de sticker se mueve antes que la de imagen, una imagen normal (sin la
        // clase --sticker) seguiría bien, pero un sticker volvería a mostrarse al tamaño de imagen
        // completa.
        var imageIndex = ScopedCss.IndexOf(".conversations-page--odoo .message-attachment--image img", StringComparison.Ordinal);
        var stickerIndex = ScopedCss.IndexOf(".conversations-page--odoo .message-attachment--sticker img", StringComparison.Ordinal);
        Assert.True(imageIndex >= 0 && stickerIndex >= 0);
        Assert.True(imageIndex < stickerIndex, "La regla de sticker debe declararse DESPUÉS que la de imagen para ganar el empate de especificidad.");
    }

    [Fact]
    public void MobileBreakpoint_CapsTheBubbleWidth_SoMediaNeverOverflowsHorizontally()
    {
        // El límite real en mobile termina siendo min(Npx, 100%) del contenedor -- y el contenedor (el
        // bubble) ya está acotado a 88% del ancho del hilo bajo este breakpoint. Ningún <img> puede
        // superar el ancho disponible.
        var mediaBlock = ExtractMediaBlock(ScopedCss, "@media (max-width: 768px)");
        Assert.Contains(".message-bubble-shell", mediaBlock, StringComparison.Ordinal);
        Assert.Contains("max-width: 88% !important;", mediaBlock, StringComparison.Ordinal);

        // min(...) en vez de un ancho fijo es justamente lo que hace que en una pantalla angosta el
        // límite real termine siendo el 100% del contenedor, no los 340px/160px fijos.
        var imageBlock = ExtractRuleBlock(ScopedCss, ".conversations-page--odoo .message-attachment--image img");
        var stickerBlock = ExtractRuleBlock(ScopedCss, ".conversations-page--odoo .message-attachment--sticker img");
        Assert.Contains("min(340px, 100%)", imageBlock, StringComparison.Ordinal);
        Assert.Contains("min(160px, 100%)", stickerBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Video_NeverFallsBackToUnlimitedSize()
    {
        var containerBlock = ExtractRuleBlock(AppCss, ".message-attachment--video");
        var videoBlock = ExtractRuleBlock(AppCss, ".message-attachment--video video");

        Assert.Contains("max-width: min(340px, 100%);", containerBlock, StringComparison.Ordinal);
        Assert.Contains("max-height: 220px;", videoBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoUnavailableFallback_ShowsAnExplanation_NotABrokenPlayer()
    {
        // Ya confirmado por auditorías previas de esta rama, se re-documenta acá porque es parte del
        // mismo criterio "video: si hay media, preview; si no, mensaje" -- sin reescribirlo.
        Assert.Contains("GetAttachmentUnavailableText(adj, \"Video no disponible\")", RazorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SupersededAppCssValues_WereUpdated_NotLeftContradictingTheActiveRule()
    {
        // ROOT CAUSE (c): app.css tenía una copia vieja (170x118, sin !important) de esta regla --
        // nunca gana (menor especificidad que el CSS aislado con scope + !important), pero confundía a
        // cualquiera que la leyera pensando que esos eran los límites vigentes. Se actualizó para que
        // documente los mismos valores que la regla que realmente aplica.
        var block = ExtractRuleBlock(AppCss, ".conversations-page--odoo .message-attachment--image img");
        Assert.DoesNotContain("170px", block, StringComparison.Ordinal);
        Assert.DoesNotContain("118px", block, StringComparison.Ordinal);
        Assert.Contains("340px", block, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string ExtractRuleBlock(string css, string selector)
    {
        var selectorIndex = css.IndexOf(selector, StringComparison.Ordinal);
        Assert.True(selectorIndex >= 0, $"No se encontró el selector '{selector}'.");
        var openBrace = css.IndexOf('{', selectorIndex);
        var closeBrace = css.IndexOf('}', openBrace);
        Assert.True(openBrace >= 0 && closeBrace > openBrace);
        return css[selectorIndex..(closeBrace + 1)];
    }

    private static string ExtractMediaBlock(string css, string mediaSelector)
    {
        var mediaIndex = css.IndexOf(mediaSelector, StringComparison.Ordinal);
        Assert.True(mediaIndex >= 0, $"No se encontró el bloque '{mediaSelector}'.");
        var openBrace = css.IndexOf('{', mediaIndex);
        var depth = 0;
        for (var i = openBrace; i < css.Length; i++)
        {
            if (css[i] == '{') depth++;
            else if (css[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return css[mediaIndex..(i + 1)];
            }
        }
        throw new InvalidOperationException("No se pudo delimitar el bloque @media.");
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
