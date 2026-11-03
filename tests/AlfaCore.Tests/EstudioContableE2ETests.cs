using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace AlfaCore.Tests;

/// <summary>
/// Punta a punta del acceso del estudio contra ALFA_CENTRAL y ALFANET2007_PRUEBAS (base 4283): invitar, crear la cuenta del
/// estudio y aceptar, login central, ingreso automático a la base, portal y revocación. Borra todo lo que crea (la tabla
/// ESTUDIO_ACCESOS queda). Requiere ALFACORE_TEST_SQL (PRUEBAS), ALFACORE_TEST_CENTRAL_SQL y ALFACORE_TEST_ESTUDIO_E2E=1.
/// </summary>
[Collection("CargaComprobantes SQL")]
public sealed class EstudioContableE2ETests(ITestOutputHelper output)
{
    private const int IdBasePruebas = 4283;
    private const string Email = "estudio.e2e.alfacore@example.com";

    public sealed class E2EFactAttribute : FactAttribute
    {
        public E2EFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("ALFACORE_TEST_ESTUDIO_E2E") != "1" || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALFACORE_TEST_CENTRAL_SQL")))
                Skip = "Definí ALFACORE_TEST_ESTUDIO_E2E=1 y ALFACORE_TEST_CENTRAL_SQL.";
        }
    }

    [E2EFact]
    public async Task InvitarAceptarEntrarYRevocar()
    {
        var cs = Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL")!;
        var central = Environment.GetEnvironmentVariable("ALFACORE_TEST_CENTRAL_SQL")!;
        using var runtime = new CargaComprobantesArcaRuntime();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:AlfaCentral"] = central, ["ConnectionStrings:AlfaGestion"] = cs }).Build();
        await using var cn = new SqlConnection(cs); await cn.OpenAsync();
        Assert.Equal("ALFANET2007_PRUEBAS", cn.Database);
        await using var cc = new SqlConnection(central); await cc.OpenAsync();

        var sesion = new SessionDto { BaseId = IdBasePruebas };
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)runtime.Sessions).Handler = (m, _) => m.Name switch
        { "GetActiveSession" => sesion, "GetConnectionString" => cs, _ => null };
        var duenio = await cn.QuerySingleAsync<AppUserSessionInfo>("SELECT LTRIM(RTRIM(NOMBRE)) UserName,LTRIM(RTRIM(SISTEMA)) SystemCode FROM TA_USUARIOS WHERE NOMBRE='Alberto'");
        AppUserSessionInfo? actual = duenio;
        var users = DispatchProxy.Create<IAppUserSessionService, CargaComprobantesUiTests.EventOnlyProxy>();
        ((CargaComprobantesUiTests.EventOnlyProxy)(object)users).Handler = (m, _) => m.Name switch
        { "get_IsAuthenticated" => actual is not null, "IsAuthorizedForSession" => true, "GetCurrentUserName" => actual?.UserName ?? "", "get_CurrentUser" => actual, _ => null };
        var bases = new CentralBasesService(config, runtime.Events);
        var clientes = new CentralClientesService(config, runtime.Events);
        var accesos = new EstudioAccesosService(runtime.Sessions, users, new PermissionService(runtime.Configuration, runtime.Sessions, users, runtime.Events), runtime.Events, config);
        var portal = new EstudioPortalService(config, users, bases, clientes, runtime.Events);

        await LimpiarAsync(cn, cc); // por si quedó algo de una corrida anterior
        string? idEstudio = null;
        try
        {
            // 1. El dueño invita.
            Assert.True(await accesos.PuedeAdministrarAsync());
            var inv = await accesos.InvitarAsync(new EstudioInvitarSolicitud { Email = Email, Perfil = "CONSULTA" }, "https://alfacentral.ddns.net/ALFANET/");
            var token = inv.Enlace.Split('/').Last();
            output.WriteLine($"Invitación {inv.Acceso.IdAcceso}: usuario {inv.Acceso.UsuarioBase}, enlace …/{token[..6]}…");
            Assert.Equal("PENDIENTE", inv.Acceso.Estado);
            var u0 = await cn.QuerySingleAsync<(string Email, bool Activo)>("SELECT ISNULL(email_de,''), CONVERT(bit,ISNULL(Activo,1)) FROM TA_USUARIOS WHERE NOMBRE=@U", new { U = inv.Acceso.UsuarioBase });
            Assert.Equal(("", false), u0); // nadie entra hasta aceptar
            Assert.Equal(EstudioContableCentral.Perfiles["CONSULTA"].Length,
                await cn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ALFACORE_TAREAS_WEB WHERE Usuario=@U", new { U = inv.Acceso.UsuarioBase }));
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => accesos.InvitarAsync(new EstudioInvitarSolicitud { Email = Email, Perfil = "CONSULTA" }, "https://x/"));

            // 2. El estudio abre el enlace y crea su cuenta.
            actual = null;
            var info = await portal.InvitacionAsync(token);
            Assert.NotNull(info);
            output.WriteLine($"Invitación de «{info!.Empresa}» / {info.BaseNombre} para {info.EmailInvitado}");
            Assert.Null(await portal.InvitacionAsync("no-existe"));
            var login = await portal.RegistrarYAceptarAsync(token, new EstudioRegistroSolicitud
            { NombreEstudio = "ESTUDIO PRUEBA E2E", Email = Email, Nombre = "Contador E2E", Password = "PruebaE2E-2026" });
            idEstudio = await cc.ExecuteScalarAsync<string>("SELECT idcliente FROM clientes WHERE idcliente IN (SELECT idcliente FROM users WHERE [user]=@E)", new { E = Email });
            output.WriteLine($"Cuenta del estudio {idEstudio} ({login})");
            Assert.StartsWith("EST", idEstudio);
            Assert.Null(await portal.InvitacionAsync(token)); // el enlace ya no sirve

            // 3. El usuario de la base quedó con el login del estudio: el ingreso automático por email lo encuentra.
            var u1 = await cn.QuerySingleAsync<(string Email, bool Activo)>("SELECT ISNULL(email_de,''), CONVERT(bit,ISNULL(Activo,1)) FROM TA_USUARIOS WHERE NOMBRE=@U", new { U = inv.Acceso.UsuarioBase });
            Assert.Equal((Email, true), u1);

            // 4. Login central: entra aunque el estudio no tenga bases propias, y va al portal.
            var auth = new CentralAuthService(config, clientes, bases, new PlainTextPasswordVerifier(), runtime.Events);
            var r = await auth.LoginAsync(new LoginRequest { UserName = Email, Password = "PruebaE2E-2026" });
            Assert.True(r.Success, r.Message);
            Assert.Equal("/estudio", r.RedirectUrl);
            Assert.Contains(r.Session!.Bases, b => b.IdBase == IdBasePruebas);
            Assert.False((await auth.LoginAsync(new LoginRequest { UserName = Email, Password = "otra" })).Success);
            Assert.Contains(await bases.GetConcedidasAsync(idEstudio!), b => b.IdBase == IdBasePruebas);

            // 5. Portal: la empresa con el estado del mes.
            actual = new AppUserSessionInfo { UserName = Email, CentralLogin = Email, IdCliente = idEstudio!, SystemCode = "CENTRAL" };
            Assert.True(await portal.EsEstudioAsync());
            var empresas = await portal.EmpresasAsync(new DateTime(2026, 10, 1));
            var e = Assert.Single(empresas);
            output.WriteLine($"Portal: {e.Empresa} / {e.BaseNombre} → /{e.IdWebEmpresa}/{e.IdBase} · ventas {e.Estado?.Ventas} (cerrado {e.Estado?.VentasCerradas}) · compras {e.Estado?.Compras} · {e.Error}");
            Assert.Null(e.Error);
            Assert.True(e.Estado!.Ventas > 0);

            // 6. El dueño revoca: el usuario de la base queda inactivo y sin email; ya no hay bases concedidas.
            actual = duenio;
            var lista = await accesos.ListarAsync();
            Assert.Contains(lista, a => a.IdAcceso == inv.Acceso.IdAcceso && a.Estado == "ACTIVO" && a.NombreEstudio == "ESTUDIO PRUEBA E2E");
            await accesos.RevocarAsync(inv.Acceso.IdAcceso);
            var u2 = await cn.QuerySingleAsync<(string Email, bool Activo)>("SELECT ISNULL(email_de,''), CONVERT(bit,ISNULL(Activo,1)) FROM TA_USUARIOS WHERE NOMBRE=@U", new { U = inv.Acceso.UsuarioBase });
            Assert.Equal(("", false), u2);
            Assert.Empty(await bases.GetConcedidasAsync(idEstudio!));
            var rLuego = await auth.LoginAsync(new LoginRequest { UserName = Email, Password = "PruebaE2E-2026" });
            Assert.True(rLuego.Success); // la cuenta del estudio sigue, sin empresas
            Assert.DoesNotContain(rLuego.Session!.Bases, b => b.IdBase == IdBasePruebas);
            output.WriteLine("Revocado: el estudio ya no ve la empresa ni puede entrar.");
        }
        finally
        {
            await LimpiarAsync(cn, cc);
        }
    }

    private async Task LimpiarAsync(SqlConnection cn, SqlConnection cc)
    {
        var usuarios = (await cc.QueryAsync<string>(
            "IF OBJECT_ID('dbo.ESTUDIO_ACCESOS') IS NOT NULL SELECT UsuarioBase FROM dbo.ESTUDIO_ACCESOS WHERE EmailInvitado=@E AND IdBase=@B ELSE SELECT TOP 0 ''",
            new { E = Email, B = IdBasePruebas })).Append(EstudioAccesosService.UsuarioDesdeEmail(Email)).Distinct().ToList();
        foreach (var u in usuarios)
            await cn.ExecuteAsync("DELETE FROM ALFACORE_TAREAS_WEB WHERE Usuario=@U; DELETE FROM TA_USUARIOS WHERE NOMBRE=@U AND NOMBRE LIKE 'EST[_]%';", new { U = u });
        var borrados = await cc.ExecuteAsync("""
            IF OBJECT_ID('dbo.ESTUDIO_ACCESOS') IS NOT NULL DELETE FROM dbo.ESTUDIO_ACCESOS WHERE EmailInvitado=@E;
            DELETE FROM dbo.clientes WHERE idcliente IN (SELECT idcliente FROM dbo.users WHERE [user]=@E) AND idcliente LIKE 'EST%' AND type='E';
            DELETE FROM dbo.users WHERE [user]=@E AND idcliente LIKE 'EST%';
            """, new { E = Email });
        output.WriteLine($"Limpieza: {usuarios.Count} usuario(s) de la base, {borrados} fila(s) centrales.");
    }
}
