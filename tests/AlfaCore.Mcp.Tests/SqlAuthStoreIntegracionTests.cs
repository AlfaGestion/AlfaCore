using AlfaCore.Mcp.Auth;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlfaCore.Mcp.Tests;

/// <summary>Solo corre con ALFACORE_MCP_TEST_AUTH_CONNECTION (base con las tablas MCP_* de 010_oauth_tablas.sql). Limpia lo que crea.</summary>
public sealed class FactAlmacenAttribute : FactAttribute
{
    public const string Variable = "ALFACORE_MCP_TEST_AUTH_CONNECTION";
    public FactAlmacenAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Integración: definir {Variable} (base con tablas MCP_* instaladas).";
    }
}

public class SqlAuthStoreIntegracionTests
{
    private static string Cs => Environment.GetEnvironmentVariable(FactAlmacenAttribute.Variable)!;
    private static SqlAuthStore Crear() => new(Cs, NullLogger<SqlAuthStore>.Instance);

    [FactAlmacen]
    public async Task Ciclo_completo_usuario_cliente_codigo_y_refresh_con_las_garantias_de_atomicidad()
    {
        var store = Crear();
        var sufijo = Guid.NewGuid().ToString("N")[..10];
        var usuario = "test_" + sufijo;
        var clientId = "mcp_test_" + sufijo;
        var ahora = DateTime.UtcNow;
        try
        {
            // Usuario
            var id = await store.CrearUsuarioAsync(usuario, "Test " + sufijo, PasswordHasher.Hash("clave-larga-123"), "base1", default);
            var u = await store.GetUsuarioAsync(usuario, default);
            Assert.NotNull(u);
            Assert.Equal(id, u!.Id);
            Assert.True(u.Activo);
            Assert.True(PasswordHasher.Verificar("clave-larga-123", u.ClaveHash));
            Assert.NotNull(await store.GetUsuarioAsync(usuario.ToUpperInvariant(), default)); // usuario insensible a mayusculas (collation CI)
            await Assert.ThrowsAnyAsync<Exception>(() => store.CrearUsuarioAsync(usuario, "dup", "x", null, default)); // unico

            await store.RegistrarLoginAsync(id, false, 2, ahora.AddMinutes(5), default);
            var bloqueado = await store.GetUsuarioPorIdAsync(id, default);
            Assert.Equal(2, bloqueado!.Intentos);
            Assert.True(bloqueado.BloqueadoHasta > ahora);

            // Cliente
            await store.AddClienteAsync(new ClienteOAuth(clientId, "Cliente de prueba", ["https://claude.ai/api/mcp/auth_callback", "http://localhost:1/callback"], ahora), default);
            var c = await store.GetClienteAsync(clientId, default);
            Assert.Equal(2, c!.RedirectUris.Count);
            Assert.True(await store.ContarClientesAsync(default) >= 1);

            // Codigo: se consume una sola vez; el segundo intento se reporta como reuso
            var hashCodigo = Secretos.Hash("codigo-" + sufijo);
            await store.AddCodigoAsync(new CodigoAuth(hashCodigo, clientId, id, "https://claude.ai/api/mcp/auth_callback", "ch", "mcp:read", "https://x/mcp", ahora.AddMinutes(1)), default);
            var (c1, reuso1) = await store.ConsumirCodigoAsync(hashCodigo, ahora, default);
            Assert.NotNull(c1); Assert.False(reuso1);
            var (c2, reuso2) = await store.ConsumirCodigoAsync(hashCodigo, ahora, default);
            Assert.NotNull(c2); Assert.True(reuso2);
            Assert.Equal((null, false), await store.ConsumirCodigoAsync("0000", ahora, default));

            // Codigo vencido
            var vencido = Secretos.Hash("vencido-" + sufijo);
            await store.AddCodigoAsync(new CodigoAuth(vencido, clientId, id, "r", "ch", "mcp:read", "x", ahora.AddSeconds(-5)), default);
            Assert.Null((await store.ConsumirCodigoAsync(vencido, ahora, default)).Codigo);

            // Codigo consumido por dos requests simultaneos: gana uno solo
            var carrera = Secretos.Hash("carrera-" + sufijo);
            await store.AddCodigoAsync(new CodigoAuth(carrera, clientId, id, "r", "ch", "mcp:read", "x", ahora.AddMinutes(1)), default);
            var resultados = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.ConsumirCodigoAsync(carrera, ahora, default)));
            Assert.Equal(1, resultados.Count(r => r.Codigo is not null && !r.Reutilizado));

            // Refresh: rotacion atomica y deteccion de reuso
            var familia = Guid.NewGuid();
            var rt1 = new RefreshAuth(Secretos.Hash("rt1-" + sufijo), clientId, id, "mcp:read", "x", ahora.AddDays(30), familia);
            var rt2 = rt1 with { Hash = Secretos.Hash("rt2-" + sufijo) };
            await store.AddRefreshAsync(rt1, default);
            Assert.True(await store.RotarRefreshAsync(rt1.Hash, rt2, ahora, default));
            Assert.False(await store.RotarRefreshAsync(rt1.Hash, rt1 with { Hash = Secretos.Hash("rt3-" + sufijo) }, ahora, default)); // ya reemplazado
            Assert.True((await store.GetRefreshAsync(rt1.Hash, default)).Invalido);
            Assert.False((await store.GetRefreshAsync(rt2.Hash, default)).Invalido);

            await store.RevocarFamiliaAsync(familia, default);
            Assert.True((await store.GetRefreshAsync(rt2.Hash, default)).Invalido);
            Assert.False(await store.RotarRefreshAsync(rt2.Hash, rt2 with { Hash = Secretos.Hash("rt4-" + sufijo) }, ahora, default));

            // Rotacion simultanea: gana uno
            var f2 = Guid.NewGuid();
            var a = new RefreshAuth(Secretos.Hash("a-" + sufijo), clientId, id, "mcp:read", "x", ahora.AddDays(30), f2);
            await store.AddRefreshAsync(a, default);
            var rot = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => store.RotarRefreshAsync(a.Hash, a with { Hash = Secretos.Hash($"n{i}-" + sufijo) }, ahora, default)));
            Assert.Equal(1, rot.Count(x => x));

            // Segundo factor: secreto cifrado, anti-replay atomico y revocacion por usuario
            await store.SetTotpAsync(id, "v1.secreto-cifrado-de-prueba", default);
            Assert.Equal("v1.secreto-cifrado-de-prueba", (await store.GetUsuarioPorIdAsync(id, default))!.TotpSecreto);
            Assert.True(await store.UsarPasoTotpAsync(id, 1000, default));
            Assert.False(await store.UsarPasoTotpAsync(id, 1000, default));   // mismo paso: replay
            Assert.False(await store.UsarPasoTotpAsync(id, 999, default));    // paso anterior
            Assert.True(await store.UsarPasoTotpAsync(id, 1001, default));
            await store.SetTotpAsync(id, "v1.nuevo", default);                 // reinscripcion: reinicia el ultimo paso
            Assert.True(await store.UsarPasoTotpAsync(id, 5, default));
            var pasos = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.UsarPasoTotpAsync(id, 7000, default)));
            Assert.Equal(1, pasos.Count(x => x));                              // simultaneos: gana uno solo
            var f3 = Guid.NewGuid();
            await store.AddRefreshAsync(new RefreshAuth(Secretos.Hash("u-" + sufijo), clientId, id, "mcp:read", "x", ahora.AddDays(30), f3), default);
            await store.RevocarUsuarioAsync(id, default);
            Assert.True((await store.GetRefreshAsync(Secretos.Hash("u-" + sufijo), default)).Invalido);

            // Evento y purga
            await store.EventoAsync("test", usuario, clientId, "127.0.0.1", "ok", default);
            await store.PurgarVencidosAsync(ahora, default);
        }
        finally
        {
            await using var cn = new SqlConnection(Cs);
            await cn.ExecuteAsync("DELETE FROM dbo.MCP_REFRESH_TOKEN WHERE ClientId = @clientId; DELETE FROM dbo.MCP_CODIGO WHERE ClientId = @clientId; " +
                                  "DELETE FROM dbo.MCP_CLIENTE_OAUTH WHERE ClientId = @clientId; DELETE FROM dbo.MCP_EVENTO WHERE ClientId = @clientId; DELETE FROM dbo.MCP_USUARIO WHERE Usuario = @usuario",
                new { clientId, usuario });
        }
    }
}
