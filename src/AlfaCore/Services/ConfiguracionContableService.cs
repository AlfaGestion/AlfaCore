using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Una clave de la solapa Gestión contable. <paramref name="GrupoTabla"/> es el GRUPO con el que el legacy crea la clave.</summary>
public sealed record ConfiguracionContableDefinicion(string Clave, string Titulo, string Seccion, string Tipo, string Ayuda, string GrupoTabla = "DATOS");

/// <summary>
/// Configuración → Gestión contable, como la solapa «Gestión Contable» del legacy (Config.frm): plan general de cuentas
/// (estructura de codificación), índices de ajuste, datos generales, centros de costo, SIFERE y fechas del ejercicio.
/// Mismas claves de TA_CONFIGURACION que usa Alfa Gestión. Guardar requiere administrador central, como la carga de comprobantes.
/// Quedan afuera el «Utilitario CD-W» (ruta de un programa de la PC) y el alta/edición de ejercicios (MV_EJERCICIOS).
/// </summary>
public sealed class ConfiguracionContableService(ISessionService sessions, IAppUserSessionService users, IAppEventService events)
{
    public const string SeccionPlan = "Plan general de cuentas";
    public const string SeccionIndices = "Índices de ajuste";
    public const string SeccionDatos = "Datos generales";
    public const string SeccionCentros = "Centros de costo";
    public const string SeccionEjercicio = "Ejercicio";

    public static IReadOnlyList<ConfiguracionContableDefinicion> Definiciones { get; } =
    [
        new("NIVEL_1", "Nombre del nivel 1", SeccionPlan, "TEXTO", "Ej. CAPITULO"),
        new("DIGITOS_CAPITULO", "Dígitos del nivel 1", SeccionPlan, "DIGITOS", "Cantidad de dígitos del capítulo"),
        new("NIVEL_2", "Nombre del nivel 2", SeccionPlan, "TEXTO", "Ej. SUB-CAPITULO"),
        new("DIGITOS_SUBCAPITULO", "Dígitos del nivel 2", SeccionPlan, "DIGITOS", "Cantidad de dígitos del sub-capítulo"),
        new("NIVEL_3", "Nombre del nivel 3", SeccionPlan, "TEXTO", "Ej. RUBRO"),
        new("DIGITOS_RUBRO", "Dígitos del nivel 3", SeccionPlan, "DIGITOS", "Cantidad de dígitos del rubro"),
        new("NIVEL_4", "Nombre del nivel 4", SeccionPlan, "TEXTO", "Ej. SUB-RUBRO"),
        new("DIGITOS_SUBRUBRO", "Dígitos del nivel 4", SeccionPlan, "DIGITOS", "Cantidad de dígitos del sub-rubro"),
        new("NIVEL_5", "Nombre del nivel 5", SeccionPlan, "TEXTO", "Ej. CUENTA"),
        new("DIGITOS_CUENTA", "Dígitos del nivel 5", SeccionPlan, "DIGITOS", "Cantidad de dígitos de la cuenta"),
        new("NIVEL_6", "Nombre del nivel 6", SeccionPlan, "TEXTO", "Ej. SUB-CUENTA"),
        new("DIGITOS_SUBCUENTA", "Dígitos del nivel 6", SeccionPlan, "DIGITOS", "Cantidad de dígitos de la sub-cuenta (0 = no se usa)"),
        new("CuentasAlfanumericas", "Cuentas alfanuméricas", SeccionPlan, "SI_NO", "Permite letras en el código de las cuentas"),
        new("Rubro_Imputable", "Permite 3° nivel imputable", SeccionPlan, "SI_NO", "El rubro puede ser cuenta imputable"),
        new("USADIGITOVERIFICADOR", "Dígito verificador", SeccionPlan, "SI_NO", "Muestra el dígito verificador de las cuentas"),
        new("PlanCuentas_CLyPR", "Mostrar clientes y proveedores", SeccionPlan, "SI_NO", "Las cuentas de clientes y proveedores aparecen como cuentas madre", "USADIGITOVERIFICADOR"),

        new("nombre_indice1", "Nombre del índice I", SeccionIndices, "TEXTO", "Índice de ajuste de las cuentas ajustables"),
        new("nombre_indice2", "Nombre del índice II", SeccionIndices, "TEXTO", ""),
        new("nombre_indice3", "Nombre del índice III", SeccionIndices, "TEXTO", ""),
        new("nombre_indice4", "Nombre del índice IV", SeccionIndices, "TEXTO", ""),

        new("AsientosXStock", "Genera asientos por movimientos de stock", SeccionDatos, "SI_NO", "", "USADIGITOVERIFICADOR"),
        new("ModificaPagos", "Modifica pagos", SeccionDatos, "SI_NO", ""),
        new("ModificaCobranzas", "Modifica cobranzas", SeccionDatos, "SI_NO", ""),
        new("UsaMultilesMonedas", "Usa múltiples monedas", SeccionDatos, "SI_NO", "Las cuentas y comprobantes pueden tener otra moneda"),
        new("ESCRIBE_SOLO_MAYUSCULAS", "Escribe solo en mayúsculas", SeccionDatos, "SI_NO", "Modo de escritura de los textos"),
        new("MODIFICA_ASIENTO", "Modificar asientos", SeccionDatos, "SI_NO", ""),
        new("USA BIENES DE USO", "Usa bienes de uso", SeccionDatos, "SI_NO", "Las cuentas pueden marcarse como bienes de uso"),
        new("USA CONTRASEÑA", "Usa contraseña", SeccionDatos, "SI_NO", ""),
        new("USADATOSADIC", "Usa datos adicionales", SeccionDatos, "SI_NO", "Datos adicionales de las cuentas por rangos"),
        new("SIFEREWEB_FECHACONTABLE", "Aplicativo SIFERE WEB: tomar fecha contable", SeccionDatos, "SI_NO", "", "SIFEREWEB_FECHACONTABLE"),

        new("USACCOSTO", "Usa centros de costo", SeccionCentros, "SI_NO", "Las cuentas imputables pueden distribuirse por centro de costo"),
        new("CC_NIVEL_1", "Nombre del nivel 1", SeccionCentros, "TEXTO", ""),
        new("CC_digitos_1", "Dígitos del nivel 1", SeccionCentros, "DIGITOS", ""),
        new("CC_NIVEL_2", "Nombre del nivel 2", SeccionCentros, "TEXTO", ""),
        new("CC_digitos_2", "Dígitos del nivel 2", SeccionCentros, "DIGITOS", ""),
        new("CC_NIVEL_3", "Nombre del nivel 3", SeccionCentros, "TEXTO", ""),
        new("CC_digitos_3", "Dígitos del nivel 3", SeccionCentros, "DIGITOS", ""),
        new("CC_NIVEL_4", "Nombre del nivel 4", SeccionCentros, "TEXTO", ""),
        new("CC_digitos_4", "Dígitos del nivel 4", SeccionCentros, "DIGITOS", ""),

        new("PERIODO", "N° de ejercicio vigente", SeccionEjercicio, "LECTURA", "Se cambia al crear o elegir el ejercicio en Alfa Gestión"),
        new("DESCRIPCION", "Nombre del ejercicio", SeccionEjercicio, "LECTURA", ""),
        new("[FECHA DESDE]", "Inicio del ejercicio", SeccionEjercicio, "LECTURA", ""),
        new("[FECHA HASTA]", "Fin del ejercicio", SeccionEjercicio, "LECTURA", ""),
        new("EJERCICIO_CERRADO", "Ejercicio cerrado", SeccionEjercicio, "LECTURA", ""),
        new("FECHAOPERDESDE", "Fecha operativa desde", SeccionEjercicio, "FECHA", "dd/mm/aaaa: primera fecha en la que se pueden cargar comprobantes", "FECHA"),
        new("FECHAOPERHASTA", "Fecha operativa hasta", SeccionEjercicio, "FECHA", "dd/mm/aaaa: última fecha en la que se pueden cargar comprobantes", "FECHA"),
        new("FechaUltimaDepuracion", "Fecha de última depuración", SeccionEjercicio, "FECHA", "dd/mm/aaaa", "FECHA")
    ];

    public Task<ConfiguracionCargaSnapshot> GetAsync(CancellationToken ct = default) => EjecutarAsync("Leer", async (cn, id, usuario) =>
    {
        var filas = await LeerAsync(cn, null, ct);
        return new ConfiguracionCargaSnapshot
        {
            SesionId = id,
            PuedeGuardar = await EsAdministradorAsync(cn, usuario, null, ct),
            Valores = Definiciones.Select(d =>
            {
                var f = Buscar(filas, d.Clave);
                return new ConfiguracionCargaValor { Clave = d.Clave, Existe = f is not null, Valor = (f?.Valor ?? string.Empty).Trim(), Version = Version(d.Clave, f) };
            }).ToList()
        };
    }, ct);

    public Task GuardarAsync(Guid sesionId, IReadOnlyList<ConfiguracionCargaCambio> cambios, CancellationToken ct = default) => EjecutarAsync("Guardar", async (cn, id, usuario) =>
    {
        if (id != sesionId) throw new CargaComprobantesReglaException("Cambió la base activa. Recargá la configuración antes de guardar.");
        ValidarCambios(cambios);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (!await EsAdministradorAsync(cn, usuario, tx, ct))
            throw new CargaComprobantesReglaException("Se requiere un administrador central para modificar la configuración contable.");
        var filas = await LeerAsync(cn, tx, ct);
        foreach (var c in cambios)
            if (Version(c.Clave, Buscar(filas, c.Clave)) != c.Version)
                throw new CargaComprobantesReglaException($"Cambió {c.Clave} desde que la abriste. Recargá para revisar los valores actuales.");

        var finales = Definiciones.ToDictionary(d => d.Clave, d => (Buscar(filas, d.Clave)?.Valor ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);
        foreach (var c in cambios) finales[c.Clave] = c.Valor.Trim();
        ValidarConjunto(finales);
        await ValidarEstructuraAsync(cn, tx, filas, finales, ct);

        foreach (var c in cambios)
        {
            var d = Definiciones.Single(x => x.Clave == c.Clave);
            await ConfiguracionSql.GrabarAsync(cn, tx, d.Clave, c.Valor.Trim(), d.GrupoTabla, d.Titulo, ct);
        }
        if (cambios.Count > 0)
            await events.WriteAuditAsync(new AuditWriteRequest
            {
                Module = "ConfiguracionGeneral", EntityType = AuditEntityTypes.Configuracion, RecordId = "GESTION_CONTABLE", Operation = AuditOperations.Update,
                Message = "Actualización de la configuración de Gestión contable",
                Changes = cambios.Select((c, i) => new AuditChangeWriteDto { FieldName = c.Clave, OldValue = Buscar(filas, c.Clave)?.Valor?.Trim(), NewValue = c.Valor.Trim(), Order = (short)i }).ToList()
            }, cn, tx, ct);
        AsegurarContexto(id, usuario);
        await tx.CommitAsync(ct);
        return true;
    }, ct);

    /// <summary>Valida cada valor según su tipo; las claves de solo lectura no se pueden cambiar.</summary>
    public static void ValidarCambios(IReadOnlyList<ConfiguracionCargaCambio> cambios)
    {
        if (cambios.Count > Definiciones.Count || cambios.Select(x => x.Clave).Distinct(StringComparer.OrdinalIgnoreCase).Count() != cambios.Count)
            throw new CargaComprobantesReglaException("La lista de configuraciones contiene claves repetidas o inválidas.");
        foreach (var c in cambios)
        {
            var d = Definiciones.SingleOrDefault(x => x.Clave == c.Clave);
            var v = (c.Valor ?? string.Empty).Trim();
            var ok = d is not null && d.Tipo != "LECTURA" && c.Version.Length == 64 && v.Length <= (d.Tipo == "TEXTO" ? 50 : 150) && d.Tipo switch
            {
                "SI_NO" => v is "SI" or "NO",
                "DIGITOS" => v.Length == 0 || int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 0 and <= 9,
                "FECHA" => v.Length == 0 || DateTime.TryParseExact(v, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "TEXTO" => true,
                _ => false
            };
            if (!ok) throw new CargaComprobantesReglaException($"Revisá el valor de «{d?.Titulo ?? c.Clave}».");
        }
    }

    /// <summary>Reglas entre claves: estructura del plan (capítulo con dígitos, hasta 15 en total), centros de costo (hasta 4, como el legacy) y fechas operativas.</summary>
    public static void ValidarConjunto(IReadOnlyDictionary<string, string> v)
    {
        int D(string k) => int.TryParse(v.TryGetValue(k, out var x) ? x : "", out var n) ? n : 0;
        if (D("DIGITOS_CAPITULO") < 1) throw new CargaComprobantesReglaException("El nivel 1 del plan de cuentas necesita al menos un dígito.");
        var total = PlanCuentasReglas.ClavesDigitos.Sum(k => D(k));
        if (total > PlanCuentasReglas.LargoMaximoCodigo)
            throw new CargaComprobantesReglaException($"La suma de los dígitos del plan de cuentas no puede superar {PlanCuentasReglas.LargoMaximoCodigo} (hoy suma {total}).");
        if (Enumerable.Range(1, 4).Sum(i => D($"CC_digitos_{i}")) > 4)
            throw new CargaComprobantesReglaException("La suma de los dígitos de los centros de costo no puede superar 4.");
        DateTime? F(string k) => DateTime.TryParseExact(v.TryGetValue(k, out var x) ? x : "", "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        if (F("FECHAOPERDESDE") is { } desde && F("FECHAOPERHASTA") is { } hasta && desde > hasta)
            throw new CargaComprobantesReglaException("La fecha operativa desde no puede ser posterior a la fecha operativa hasta.");
    }

    /// <summary>
    /// Si cambian los dígitos y ya hay cuentas cargadas, todas tienen que seguir teniendo un largo válido en la nueva
    /// estructura: si no, el plan quedaría inconsistente (para eso está la recodificación del plan de cuentas).
    /// </summary>
    private static async Task ValidarEstructuraAsync(SqlConnection cn, SqlTransaction tx, List<Fila> filas, IReadOnlyDictionary<string, string> finales, CancellationToken ct)
    {
        if (!PlanCuentasReglas.ClavesDigitos.Any(k => (Buscar(filas, k)?.Valor ?? "").Trim() != (finales.TryGetValue(k, out var x) ? x : "").Trim())) return;
        var nueva = PlanCuentasReglas.EstructuraDesde(finales.ToDictionary(x => x.Key.ToUpperInvariant(), x => x.Value));
        var largos = (await cn.QueryAsync<(int Largo, int Cuentas)>(new CommandDefinition(
            "SELECT LEN(LTRIM(RTRIM(CODIGO))), COUNT(*) FROM dbo.MA_CUENTAS GROUP BY LEN(LTRIM(RTRIM(CODIGO)));", transaction: tx, cancellationToken: ct))).ToList();
        var invalidos = largos.Where(l => !nueva.Largos.Contains(l.Largo)).ToList();
        if (invalidos.Count > 0)
            throw new CargaComprobantesReglaException(
                $"Con esos dígitos quedarían {invalidos.Sum(x => x.Cuentas)} cuenta(s) con un largo que no corresponde a ningún nivel " +
                $"({string.Join(", ", invalidos.Select(x => $"{x.Cuentas} de {x.Largo} dígitos"))}). Para cambiar la estructura de un plan cargado usá la recodificación del plan de cuentas.");
    }

    private static Fila? Buscar(List<Fila> filas, string clave) => filas.FirstOrDefault(x => x.Clave.Trim().Equals(clave, StringComparison.OrdinalIgnoreCase));
    private static string Version(string clave, Fila? f) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Clave = clave.ToUpperInvariant(), f?.Valor, Existe = f is not null }))));

    private static async Task<List<Fila>> LeerAsync(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
        => (await cn.QueryAsync<Fila>(new CommandDefinition(
            "SELECT CLAVE, VALOR FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN @Claves;",
            new { Claves = Definiciones.Select(x => x.Clave.ToUpperInvariant()).ToArray() }, tx, cancellationToken: ct))).ToList();

    private static Task<bool> EsAdministradorAsync(SqlConnection cn, string usuario, SqlTransaction? tx, CancellationToken ct)
        => cn.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT CONVERT(bit,ISNULL(AdminCentral,0)) FROM dbo.TA_USUARIOS WHERE UPPER(LTRIM(RTRIM(NOMBRE)))=UPPER(@Usuario)",
            new { Usuario = usuario }, tx, cancellationToken: ct));

    private void AsegurarContexto(Guid id, string usuario)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var actual) || actual != id || users.GetCurrentUserName().Trim() != usuario)
            throw new CargaComprobantesReglaException("Cambió la sesión. Volvé a abrir la configuración en la base correcta.");
    }

    private async Task<T> EjecutarAsync<T>(string accion, Func<SqlConnection, Guid, string, Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out var id))
            throw new CargaComprobantesReglaException("Ingresá en la base activa para consultar su configuración.");
        var usuario = users.GetCurrentUserName().Trim();
        try
        {
            await using var cn = new SqlConnection(sessions.GetConnectionString());
            await cn.OpenAsync(ct);
            var r = await operacion(cn, id, usuario);
            AsegurarContexto(id, usuario);
            return r;
        }
        catch (CargaComprobantesReglaException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var incidente = await events.LogErrorAsync("ConfiguracionGeneral", "GestionContable." + accion, ex, "No se pudo completar la configuración contable.", ct: ct);
            throw new AppUserFacingException("No se pudo completar la configuración contable.", incidente, ex);
        }
    }

    private sealed class Fila
    {
        public string Clave { get; set; } = "";
        public string? Valor { get; set; }
    }
}
