using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.VisualBasic.FileIO;

namespace AlfaCore.Services;

/// <summary>Editor acotado de claves confirmadas; no expone valores del inventario legacy completo.</summary>
public sealed class ConfiguracionCargaComprobantesService(ISessionService sessions, IAppUserSessionService users, IAppEventService events)
{
    public static IReadOnlyList<ConfiguracionCargaDefinicion> Definiciones { get; } =
    [
        new("PreciosFinales","Usar precios finales","Precios","SI_NO","Carga / Alfa Gestión"),
        new("PreciosFinalesNeto","Precios finales netos","Precios","SI_NO","Carga / Alfa Gestión; circuito aún acotado"),
        new("MaestroArticuloConIVA","Precios del maestro incluyen IVA","Precios","SI_NO","Artículos / listas / Carga"),
        new("UsaListasDePrecios","Usar listas de precios","Precios","SI_NO","Clientes / listas / Carga"),
        new("FIJALISTASCONIVA","Listas con IVA","Precios","SI_NO","Listas / Carga"),
        new("FIJALISTASSINIVA","Listas sin IVA","Precios","SI_NO","Listas / Carga; se conserva la clave original"),
        new("CLASEPRECIOVENTA","Clase de precio predeterminada","Precios","CLASE","Artículos / Carga"),
        new("ClaseDePrecioDefault","Clase de precio alternativa","Precios","CLASE","Carga; fallback cuando falta la clase principal"),
        new("PIVA","Alícuota general de IVA (%)","Importes e IVA","PORCENTAJE","Artículos / otros procesos de venta"),
        new("CantidadDecimalesVentas","Decimales de ventas (0 = heredar)","Importes e IVA","DECIMALES","Ventas / Alfa Gestión"),
        new("CantidadDecimales","Decimales generales (0 = predeterminado)","Importes e IVA","DECIMALES","General / ventas"),
        new("AjustaRedondeoTotal","Ajustar diferencia final de redondeo","Importes e IVA","SI_NO","Carga / Alfa Gestión; solo NO lo desactiva"),
        new("MaxDto","Descuento máximo general (%)","Descuentos","PORCENTAJE","Carga / Alfa Gestión; también intervienen vendedor y condición"),
        new("MaxDtoProforma","Descuento máximo de proforma (%)","Descuentos","PORCENTAJE","Proforma / Alfa Gestión"),
        new("UsaFacturaProforma","Habilitar proforma en Carga de Comprobantes","Proforma","SI_NO","Carga / Alfa Gestión; distinta de USAPROFORMA"),
        new("RobaIvaProforma","Sumar IVA en proforma","Proforma","SI_NO","Carga / Alfa Gestión; no genera libro IVA"),
        new("UsaFacturaProformaSinRegistracion","Omitir asiento en proforma legacy","Proforma","LECTURA","Solo lectura: Carga siempre genera asiento por requerimiento"),
        new("USAPROFORMA","Proforma en Punto de Venta","Proforma","LECTURA","Punto de Venta: editar en su pestaña; no es UsaFacturaProforma"),
        new("EntregaParcialRemito","Permitir entrega parcial","Orígenes y stock","SI_NO","Carga / Alfa Gestión"),
        new("DescargaStockRemitos","Remitos descargan stock","Orígenes y stock","SI_NO","Remitos / facturación; se revalida el origen"),
        new("RevalorizaRemitos","Revalorizar remitos","Orígenes y stock","SI_NO","Circuito de revalorización aún pendiente en Carga"),
        new("DescargaStockxReparto","Descargar stock por reparto","Controles adicionales","SI_NO","Circuito de reparto aún pendiente en Carga"),
        new("NoFacturaVencidos","Control de deuda vencida","Controles adicionales","SI_NO","Control adicional aún pendiente en Carga"),
        new("SS_BloqueoActivo","Bloqueo de seguridad contable","Controles adicionales","SI_NO","Control adicional aún pendiente en Carga"),
        new("FECHAOPERDESDE","Fecha operativa desde (dd/MM/yyyy)","Contabilidad","FECHA","General / ventas"),
        new("FECHAOPERHASTA","Fecha operativa hasta (dd/MM/yyyy)","Contabilidad","FECHA","General / ventas"),
        new("PERIODO","Ejercicio / período activo","Contabilidad","LECTURA","Administrar junto al ejercicio contable"),
        new("EQU_FC","Equivalencia contable FC","Contabilidad","LECTURA","Comprobantes / contabilidad"),
        new("EQU_NC","Equivalencia contable NC","Contabilidad","LECTURA","Comprobantes / contabilidad"),
        new("EQU_ND","Equivalencia contable ND","Contabilidad","LECTURA","Comprobantes / contabilidad"),
        new("EQU_FP","Equivalencia contable FP","Contabilidad","LECTURA","Comprobantes / contabilidad"),
        new("MOTIVOSTOCKDEFAULT","Motivo de stock predeterminado","Contabilidad","LECTURA","Maestros de stock / comprobantes"),
        new("PV_EFACTURA","Punto electrónico global","Electrónica","LECTURA","Facturación electrónica; puede haber emisor por unidad"),
        new("USAFCELECTRONICA","Electrónica habilitada en legacy","Electrónica","LECTURA","No reemplaza ambiente/emisor ARCA"),
        new("ARCA_AMBIENTE","Ambiente ARCA","Electrónica","LECTURA","Editar en Facturación electrónica; Carga sigue limitada a homologación")
    ];

    public static IReadOnlyList<ConfiguracionCargaReferencia> GetInventarioLegacy()
    {
        using var stream = typeof(ConfiguracionCargaComprobantesService).Assembly.GetManifestResourceStream("AlfaCore.Ventas.ConfiguracionVb6.csv")
            ?? throw new InvalidOperationException("No se encontró el inventario de configuraciones de ventas.");
        using var csv = new TextFieldParser(stream, Encoding.UTF8) { HasFieldsEnclosedInQuotes = true };
        csv.SetDelimiters(","); csv.ReadFields();
        var entradas = new List<ConfiguracionCargaReferencia>();
        while (!csv.EndOfData)
        {
            var fila = csv.ReadFields();
            if (fila is { Length: >= 6 } && !fila[0].StartsWith('(')) entradas.Add(new(fila[0], fila[5] == "SI"));
        }
        return entradas.Distinct().OrderBy(x => x.Clave, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task<ConfiguracionCargaSnapshot> GetAsync(CancellationToken ct = default) => EjecutarAsync("Leer", async (cn, id, usuario) =>
    {
        var filas = await LeerAsync(cn, null, ct);
        return new ConfiguracionCargaSnapshot { SesionId = id,
            PuedeGuardar = await EsAdministradorAsync(cn, usuario, null, ct),
            Valores = Definiciones.Select(d =>
            {
                var f = filas.SingleOrDefault(x => x.Clave.Trim().Equals(d.Clave, StringComparison.OrdinalIgnoreCase));
                return new ConfiguracionCargaValor { Clave = d.Clave, Existe = f is not null,
                    Valor = Valor(f), Version = Version(d.Clave, f) };
            }).ToList() };
    }, ct);

    public Task GuardarAsync(Guid sesionId, IReadOnlyList<ConfiguracionCargaCambio> cambios, CancellationToken ct = default)
        => EjecutarAsync("Guardar", async (cn, id, usuario) =>
        {
            if (id != sesionId) throw new CargaComprobantesReglaException("Cambió la base activa. Recargá la configuración antes de guardar.");
            ValidarCambios(cambios);
            using var tx = cn.BeginTransaction(System.Data.IsolationLevel.Serializable);
            try
            {
                await GuardarEnTransaccionAsync(cn, tx, id, usuario, cambios, ct);
                tx.Commit(); return true;
            }
            finally { if (tx.Connection is not null) tx.Rollback(); }
        }, ct);

    private async Task GuardarEnTransaccionAsync(SqlConnection cn, SqlTransaction tx, Guid id, string usuario,
        IReadOnlyList<ConfiguracionCargaCambio> cambios, CancellationToken ct)
    {
        ValidarCambios(cambios);
        if (!await EsAdministradorAsync(cn, usuario, tx, ct))
            throw new CargaComprobantesReglaException("Se requiere un administrador central para modificar configuraciones compartidas de ventas.");
        var filas = await LeerAsync(cn, tx, ct);
        foreach (var cambio in cambios)
        {
            var actual = filas.SingleOrDefault(x => x.Clave.Trim().Equals(cambio.Clave, StringComparison.OrdinalIgnoreCase));
            if (Version(cambio.Clave, actual) != cambio.Version)
                throw new CargaComprobantesReglaException($"Cambió {cambio.Clave} desde que la abriste. Recargá para revisar los valores actuales.");
            var definicion = Definiciones.Single(x => x.Clave == cambio.Clave);
            await cn.ExecuteAsync(new CommandDefinition("""
                IF EXISTS(SELECT 1 FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE)))=UPPER(@Clave))
                    UPDATE dbo.TA_CONFIGURACION SET VALOR=@Valor,ValorAux=NULL
                    WHERE UPPER(LTRIM(RTRIM(CLAVE)))=UPPER(@Clave);
                ELSE INSERT INTO dbo.TA_CONFIGURACION(GRUPO,CLAVE,VALOR,DESCRIPCION)
                    VALUES('VENTAS',@Clave,@Valor,@Descripcion);
                """, new { cambio.Clave, Valor = cambio.Valor.Trim(), Descripcion = definicion.Titulo[..Math.Min(50,definicion.Titulo.Length)] }, tx, cancellationToken: ct));
        }
        if (cambios.Count > 0)
            await events.WriteAuditAsync(new AuditWriteRequest { Module = "ConfiguracionGeneral", EntityType = AuditEntityTypes.Configuracion,
                RecordId = "VENTAS_CARGA_COMPROBANTES", Operation = AuditOperations.Update, Message = "Actualización de configuración de carga de comprobantes",
                Changes = cambios.Select((c,i) => new AuditChangeWriteDto { FieldName = c.Clave,
                    OldValue = Valor(filas.SingleOrDefault(f => f.Clave.Trim().Equals(c.Clave,StringComparison.OrdinalIgnoreCase))),
                    NewValue = c.Valor.Trim(), Order = (short)i }).ToList() }, cn, tx, ct);
        AsegurarContexto(id, usuario);
    }

    public static void ValidarCambios(IReadOnlyList<ConfiguracionCargaCambio> cambios)
    {
        if (cambios.Count > Definiciones.Count || cambios.Select(x => x.Clave).Distinct(StringComparer.OrdinalIgnoreCase).Count() != cambios.Count)
            throw new CargaComprobantesReglaException("La lista de configuraciones contiene claves repetidas o inválidas.");
        foreach (var c in cambios)
        {
            var d = Definiciones.SingleOrDefault(x => x.Clave == c.Clave);
            var v = c.Valor.Trim();
            var correcto = d is not null && d.Tipo != "LECTURA" && v.Length <= 150 && c.Version.Length == 64;
            if (correcto && v.Length > 0)
                correcto = d!.Tipo switch
                {
                    "SI_NO" => v is "SI" or "NO",
                    "PORCENTAJE" => decimal.TryParse(v.Replace(',','.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,out var n) && n is >= 0 and <= 100,
                    "DECIMALES" => int.TryParse(v,out var n) && n is >= 0 and <= 4,
                    "CLASE" => int.TryParse(v,out var n) && n is >= 1 and <= 8,
                    "FECHA" => DateTime.TryParseExact(v,"dd/MM/yyyy",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),
                    _ => false
                };
            if (!correcto) throw new CargaComprobantesReglaException($"Revisá el valor o permiso de edición de {c.Clave}.");
        }
    }

    private static string Valor(Fila? f) => !string.IsNullOrWhiteSpace(f?.Valor) ? f.Valor.Trim() : f?.ValorAux?.Trim() ?? "";
    private static string Version(string clave, Fila? f) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Clave = clave.ToUpperInvariant(), Valor = f?.Valor, ValorAux = f?.ValorAux, Existe = f is not null }))));
    private static async Task<List<Fila>> LeerAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
        => (await cn.QueryAsync<Fila>(new CommandDefinition("""
            SELECT CLAVE,VALOR,CONVERT(nvarchar(max),ValorAux) ValorAux FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN @Claves;
            """,new { Claves = Definiciones.Select(x=>x.Clave.ToUpperInvariant()).ToArray() },tx,cancellationToken:ct))).ToList();
    private static Task<bool> EsAdministradorAsync(SqlConnection cn, string usuario, SqlTransaction? tx, CancellationToken ct)
        => cn.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT CONVERT(bit,ISNULL(AdminCentral,0)) FROM dbo.TA_USUARIOS WHERE UPPER(LTRIM(RTRIM(NOMBRE)))=UPPER(@Usuario)",new { Usuario = usuario },tx,cancellationToken:ct));
    private void AsegurarContexto(Guid id, string usuario)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions,users,out var actual) || actual != id || users.GetCurrentUserName().Trim() != usuario)
            throw new CargaComprobantesReglaException("Cambió la sesión. Volvé a abrir la configuración en la base correcta.");
    }
    private async Task<T> EjecutarAsync<T>(string accion, Func<SqlConnection,Guid,string,Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions,users,out var id))
            throw new CargaComprobantesReglaException("Ingresá en la base activa para consultar su configuración.");
        var usuario = users.GetCurrentUserName().Trim();
        try
        {
            await using var cn = new SqlConnection(sessions.GetConnectionString()); await cn.OpenAsync(ct);
            var r = await operacion(cn,id,usuario); AsegurarContexto(id,usuario); return r;
        }
        catch (CargaComprobantesReglaException) { throw; }
        catch (OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var incidente = await events.LogErrorAsync("ConfiguracionGeneral","CargaComprobantes."+accion,ex,"No se pudo completar la configuración de comprobantes.",ct:ct);
            throw new AppUserFacingException("No se pudo completar la configuración de comprobantes.",incidente,ex);
        }
    }
    private sealed class Fila { public string Clave { get; set; } = ""; public string? Valor { get; set; } public string? ValorAux { get; set; } }
}
