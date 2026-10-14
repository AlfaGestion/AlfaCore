using System.Text.RegularExpressions;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// CONV_MENSAJES.MessageType (2026-10-14): desde 2026-09-14 NormalizeMessageType devolvía 'ORDER'
/// (pedido de catálogo de WhatsApp) pero la restricción CK_CONV_MENSAJES_MessageType no lo aceptaba,
/// así que el webhook fallaba al guardar el pedido. Este test exige que el último script que define la
/// restricción acepte todos los tipos que el código puede guardar.
/// </summary>
public sealed class ConversacionesMessageTypeConstraintTests
{
    [Fact]
    public void LaRestriccionDeLaBase_AceptaTodosLosTiposQueGuardaElCodigo()
    {
        var entradas = new[]
        {
            null, "", "text", "image", "document", "audio", "video", "sticker", "location", "contacts",
            "contact", "button", "interactive", "reaction", "system", "order", "unsupported", "algo-nuevo"
        };
        var tiposDelCodigo = entradas.Select(ConversacionesService.NormalizeMessageType).ToHashSet(StringComparer.Ordinal);

        var permitidos = TiposPermitidosPorElUltimoScript(out var script);

        var faltantes = tiposDelCodigo.Where(t => !permitidos.Contains(t)).ToList();
        Assert.True(faltantes.Count == 0,
            $"{script} no acepta: {string.Join(", ", faltantes)}. Sumarlos a CK_CONV_MENSAJES_MessageType con un script nuevo.");
    }

    private static HashSet<string> TiposPermitidosPorElUltimoScript(out string script)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln")))
            current = current.Parent;
        Assert.NotNull(current);

        var ultimo = Directory.GetFiles(Path.Combine(current!.FullName, "src", "AlfaCore", "App_Data", "updates"), "*.sql")
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"ADD\s+CONSTRAINT\s+CK_CONV_MENSAJES_MessageType", RegexOptions.IgnoreCase))
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .Last();
        script = Path.GetFileName(ultimo);

        var sql = File.ReadAllText(ultimo);
        var definicion = sql[Regex.Match(sql, @"ADD\s+CONSTRAINT\s+CK_CONV_MENSAJES_MessageType", RegexOptions.IgnoreCase).Index..];
        var lista = Regex.Match(definicion, @"MessageType\s+IN\s*\((?<lista>[^)]*)\)", RegexOptions.IgnoreCase).Groups["lista"].Value;
        return Regex.Matches(lista, @"N?'(?<v>[A-Z_]+)'").Select(m => m.Groups["v"].Value).ToHashSet(StringComparer.Ordinal);
    }
}
