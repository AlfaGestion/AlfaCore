using AlfaCore.Services;
using Microsoft.Playwright;
using Xunit;

namespace AlfaCore.Tests;

public sealed class DocumentPdfChromiumFaltanteTests
{
    [Fact]
    public void DetectaElChromiumFaltanteAunqueVengaEnvuelto()
    {
        var playwright = new PlaywrightException("Executable doesn't exist at C:\\Users\\x\\AppData\\Local\\ms-playwright\\chromium_headless_shell-1187\\chrome-win\\headless_shell.exe");
        Assert.True(DocumentPdfService.EsChromiumFaltante(playwright));
        Assert.True(DocumentPdfService.EsChromiumFaltante(new InvalidOperationException("envoltorio", playwright)));
    }

    [Fact]
    public void OtrosErroresNoSeConfundenConChromiumFaltante()
    {
        Assert.False(DocumentPdfService.EsChromiumFaltante(new PlaywrightException("Timeout 30000ms exceeded.")));
        Assert.False(DocumentPdfService.EsChromiumFaltante(new InvalidOperationException("Executable doesn't exist")));
    }
}
