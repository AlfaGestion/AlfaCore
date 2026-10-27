using System.Data;
using System.Text;
using System.Text.Json;
using AlfaCore.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>Captura explícita de lectura; no llama al coordinador de emisión ni a ARCA.</summary>
[Collection("CargaComprobantes SQL")]
public sealed class CargaComprobantesComparacionTests
{
    [ComparacionExportarFact]
    public async Task ExportaGrabacionesExistentesSinEmitirNiModificarMovimientos()
    {
        var ids = Environment.GetEnvironmentVariable("ALFACORE_COMPARACION_IDS")!.Split(',')
            .Select(x => int.Parse(x.Trim(), System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToArray();
        Assert.InRange(ids.Length, 1, 20);
        Assert.All(ids, id => Assert.True(id > 0));
        var destino = Path.GetFullPath(Environment.GetEnvironmentVariable("ALFACORE_COMPARACION_DESTINO")!);
        Directory.CreateDirectory(destino);
        using var runtime = new CargaComprobantesArcaRuntime();
        try
        {
            await using var cn = new SqlConnection(Environment.GetEnvironmentVariable("ALFACORE_TEST_SQL"));
            await cn.OpenAsync();
            var objetos = new[] { "V_MV_CPTE", "V_MV_CPTEINSUMOS", "V_MV_STOCK", "MV_ASIENTOS", "MV_APLICACION",
                "V_MV_CPTE_ELECTRONICOS", "V_MV_CPTETAREAS", "V_MV_CPTE_OBSERV", "TA_CONFIGURACION" };
            foreach (var tabla in objetos)
                Assert.Equal("U", (await cn.ExecuteScalarAsync<string>(
                    "SELECT type FROM sys.objects WHERE object_id=OBJECT_ID(@Nombre)", new { Nombre = "dbo." + tabla }))?.Trim());

            // Una captura por transacción evita mezclar cambios concurrentes entre las secciones.
            // No hay consultas de red, interacción del usuario ni escrituras comerciales dentro.
            foreach (var id in ids)
            {
                using var tx = cn.BeginTransaction(IsolationLevel.Serializable);
                try
                {
                    async Task<List<Dictionary<string, object?>>> Leer(string sql, object parametros)
                    {
                        var rows = await cn.QueryAsync(new CommandDefinition(sql, parametros, tx, commandTimeout: 30));
                        return rows.Select(row => ((IDictionary<string, object>)row).ToDictionary(
                            x => x.Key, x => x.Value is DBNull ? null : (object?)x.Value)).ToList();
                    }
                    var cabecera = await Leer("SELECT * FROM dbo.V_MV_CPTE WHERE ID=@Id", new { Id = id });
                    Assert.Single(cabecera);
                    var c = cabecera[0];
                    var tc = Convert.ToString(c["TC"])!.Trim();
                    Assert.Contains(tc, new[] { "FC", "NC", "ND", "FP" });
                    var comprobante = Convert.ToString(c["IDCOMPROBANTE"])!;
                    Assert.Equal(0, Convert.ToInt32(c["IDCOMPLEMENTO"]));
                    var configuracion = await Leer("""
                        SELECT CLAVE,VALOR,CONVERT(nvarchar(max),ValorAux) ValorAux FROM dbo.TA_CONFIGURACION
                        WHERE LTRIM(RTRIM(CLAVE)) IN ('EQU_FC','EQU_NC','EQU_ND','EQU_FP','PreciosFinales',
                            'PreciosFinalesNeto','AjustaRedondeoTotal','RobaIvaProforma','UsaFacturaProforma',
                            'UsaFacturaProformaSinRegistracion','ArtExentoaNoGrav','ConfirmaUDEquiv','EntregaParcialRemito',
                            'MaxDto','MaxDtoProforma','UsaListasDePrecios')
                        """, new { });
                    var clave = configuracion.SingleOrDefault(x => Convert.ToString(x["CLAVE"])?.Trim() == "EQU_" + tc);
                    var valor = clave is null ? "" : Convert.ToString(clave["VALOR"])?.Trim();
                    if (string.IsNullOrWhiteSpace(valor) && clave is not null) valor = Convert.ToString(clave["ValorAux"])?.Trim();
                    Assert.False(string.IsNullOrWhiteSpace(valor), "Falta equivalencia contable actual; no se asume el TC.");
                    var p = new { Tc = tc, Comprobante = comprobante, Contable = valor,
                        Sucursal = c["SUCURSAL"], Numero = c["NUMERO"], Letra = c["LETRA"], Unidad = c["UNEGOCIO"] };
                    var secciones = new Dictionary<string, object> { ["Cabecera"] = cabecera };
                    foreach (var (nombre, tabla) in new[] { ("Articulos", "V_MV_CPTEINSUMOS"), ("Stock", "V_MV_STOCK"),
                        ("Tareas", "V_MV_CPTETAREAS"), ("Conceptos", "V_MV_CPTE_OBSERV"), ("Electronica", "V_MV_CPTE_ELECTRONICOS") })
                        secciones[nombre] = await Leer($"SELECT * FROM dbo.{tabla} WHERE LTRIM(RTRIM(TC))=@Tc AND IDCOMPROBANTE=@Comprobante", p);
                    secciones["Asientos"] = await Leer("""
                        SELECT * FROM dbo.MV_ASIENTOS WHERE LTRIM(RTRIM(TC))=@Contable
                            AND SUCURSAL=@Sucursal AND NUMERO=@Numero AND LETRA=@Letra
                            AND LTRIM(RTRIM(UNEGOCIO))=LTRIM(RTRIM(@Unidad))
                        """, p);
                    Assert.NotEmpty((List<Dictionary<string, object?>>)secciones["Asientos"]);
                    secciones["AplicacionesEmitidas"] = await Leer("""
                        SELECT * FROM dbo.MV_APLICACION WHERE LTRIM(RTRIM(TC))=@Tc AND IDCOMPROBANTE=@Comprobante
                        """, p);
                    secciones["AplicacionesRecibidas"] = await Leer("""
                        SELECT * FROM dbo.MV_APLICACION WHERE LTRIM(RTRIM(TCO_ORIGEN))=@Tc AND IDCOMPROBANTE_ORIGEN=@Comprobante
                        """, p);
                    // La cabecera consolidada no inventa un origen único: recorrer también las aplicaciones.
                    const string origenesSql = """
                        SELECT o.* FROM dbo.V_MV_CPTE o WHERE o.IDCOMPLEMENTO=0 AND LTRIM(RTRIM(o.TC)) IN ('NP','RM','FC') AND
                        (EXISTS(SELECT 1 FROM dbo.V_MV_CPTE c WHERE c.ID=@Id AND LTRIM(RTRIM(c.TCORIGEN))=LTRIM(RTRIM(o.TC))
                            AND c.COMPROBANTEORIGEN=o.IDCOMPROBANTE)
                         OR EXISTS(SELECT 1 FROM dbo.MV_APLICACION a WHERE LTRIM(RTRIM(a.TC))=@Tc AND a.IDCOMPROBANTE=@Comprobante
                            AND LTRIM(RTRIM(a.TCO_ORIGEN))=LTRIM(RTRIM(o.TC)) AND a.IDCOMPROBANTE_ORIGEN=o.IDCOMPROBANTE));
                        """;
                    var origenes = await Leer(origenesSql, new { Id=id, Tc=tc, Comprobante=comprobante });
                    secciones["OrigenCabecera"] = origenes;
                    var detalleOrigenes = new List<Dictionary<string,object?>>();
                    foreach (var origen in origenes)
                        detalleOrigenes.AddRange(await Leer("SELECT * FROM dbo.V_MV_CPTEINSUMOS WHERE TC=@Tc AND IDCOMPROBANTE=@Comprobante AND IDCOMPLEMENTO=0",
                            new { Tc=origen["TC"], Comprobante=origen["IDCOMPROBANTE"] }));
                    secciones["OrigenArticulos"] = detalleOrigenes;
                    if(await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID('dbo.ALFACORE_VENTAS_CARGA_ORIGEN_LINEA','U') IS NULL THEN 0 ELSE 1 END",transaction:tx))==1)
                        secciones["TrazabilidadOrigen"]=await Leer("""
                            SELECT t.* FROM dbo.ALFACORE_VENTAS_CARGA_ORIGEN_LINEA t
                            JOIN dbo.V_MV_CPTEINSUMOS i ON i.ID=t.DetalleDestinoId
                            WHERE LTRIM(RTRIM(i.TC))=@Tc AND i.IDCOMPROBANTE=@Comprobante;
                            """,p);
                    var captura = new { Version = 1, CapturaUtc = DateTime.UtcNow, Base = cn.Database,
                        DocumentoId = id, Tc = tc, Comprobante = comprobante.Trim(),
                        Advertencia = "Estado actual, no delta antes/después; equivalencia contable según configuración actual. No certifica VB6.",
                        Configuracion = configuracion, Secciones = secciones };
                    var json = JsonSerializer.Serialize(captura, new JsonSerializerOptions { WriteIndented = true });
                    tx.Rollback();
                    await File.WriteAllTextAsync(Path.Combine(destino, $"{tc}-{comprobante.Trim()}.json"), json, new UTF8Encoding(false));
                }
                finally { if (tx.Connection is not null) tx.Rollback(); }
            }
        }
        catch (Exception ex)
        {
            await runtime.Events.LogErrorAsync("Ventas.CargaComprobantes", "ExportarComparacion", ex,
                "No se pudo capturar la grabación para comparar.");
            throw;
        }
    }
}

public sealed class ComparacionExportarFactAttribute : FactAttribute
{
    public ComparacionExportarFactAttribute()
    {
        if (new[] { "ALFACORE_TEST_SQL", "ALFACORE_COMPARACION_IDS", "ALFACORE_COMPARACION_DESTINO" }
            .Any(x => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(x))))
            Skip = "Captura de lectura opt-in: requiere conexión de pruebas, IDs explícitos y destino local.";
    }
}
