using System.Text.RegularExpressions;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// AlfaCore nunca escribe en TA_MENU / TA_TAREAS (menu y permisos del legacy VB6): los scripts de
/// App_Data/updates corren sobre copias (ALFACORE_SANDBOX_TA_*).
/// </summary>
public sealed class LegacyTablesUpdateGuardTests
{
    [Theory]
    [InlineData("INSERT INTO dbo.TA_MENU(Menu) VALUES(1)", "INSERT INTO dbo.ALFACORE_SANDBOX_TA_MENU(Menu) VALUES(1)")]
    [InlineData("UPDATE [dbo].[TA_MENU] SET Nombre=''", "UPDATE [dbo].[ALFACORE_SANDBOX_TA_MENU] SET Nombre=''")]
    [InlineData("IF OBJECT_ID(N'dbo.TA_TAREAS',N'U') IS NULL RETURN;", "IF OBJECT_ID(N'dbo.ALFACORE_SANDBOX_TA_TAREAS',N'U') IS NULL RETURN;")]
    [InlineData("insert into ta_tareas select 1", "insert into ALFACORE_SANDBOX_TA_TAREAS select 1")]
    [InlineData("SELECT * FROM dbo.V_TA_TAREAS", "SELECT * FROM dbo.V_TA_TAREAS")]
    [InlineData("INSERT INTO dbo.ALFACORE_TAREAS_WEB(Clave) VALUES('x')", "INSERT INTO dbo.ALFACORE_TAREAS_WEB(Clave) VALUES('x')")]
    [InlineData("INSERT INTO dbo.ALFACORE_MENU_WEB(Clave) VALUES('x')", "INSERT INTO dbo.ALFACORE_MENU_WEB(Clave) VALUES('x')")]
    [InlineData("SELECT * FROM #TA_MENU; DECLARE @TA_MENU int;", "SELECT * FROM #TA_MENU; DECLARE @TA_MENU int;")]
    [InlineData("SELECT * FROM dbo.TA_MENU_ESPECIAL", "SELECT * FROM dbo.TA_MENU_ESPECIAL")]
    public void RewriteCambiaSoloLasTablasDelLegacy(string sql, string esperado)
        => Assert.Equal(esperado, LegacyTablesUpdateGuard.Rewrite(sql));

    [Fact]
    public void NingunScriptDeActualizacionEscribeElMenuNiLosPermisosDelLegacy()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "AlfaCore.sln")))
            repo = repo.Parent;
        Assert.NotNull(repo);
        var updates = Path.Combine(repo!.FullName, "src", "AlfaCore", "App_Data", "updates");
        var scripts = Directory.GetFiles(updates, "*.sql");
        Assert.NotEmpty(scripts);

        var escritura = new Regex(
            @"\b(INSERT(\s+INTO)?|UPDATE|DELETE(\s+FROM)?|MERGE(\s+INTO)?|TRUNCATE\s+TABLE|DROP\s+TABLE|ALTER\s+TABLE)\s+(\[?dbo\]?\.)?\[?(TA_MENU|TA_TAREAS)\b",
            RegexOptions.IgnoreCase);
        // UPDATE alias SET ... FROM dbo.TA_MENU alias / DELETE alias FROM dbo.TA_TAREAS alias
        var conAlias = new Regex(
            @"\b(UPDATE|DELETE)\s+\w+\s+(SET\b[\s\S]{0,2000}?\s)?FROM\s+(\[?dbo\]?\.)?\[?(TA_MENU|TA_TAREAS)\b",
            RegexOptions.IgnoreCase);

        var fallas = new List<string>();
        foreach (var script in scripts)
        {
            var sql = LegacyTablesUpdateGuard.Rewrite(File.ReadAllText(script));
            if (escritura.IsMatch(sql) || conAlias.IsMatch(sql))
                fallas.Add(Path.GetFileName(script));
        }

        Assert.True(fallas.Count == 0, "Escriben TA_MENU/TA_TAREAS despues del aislamiento: " + string.Join(", ", fallas));
    }
}
