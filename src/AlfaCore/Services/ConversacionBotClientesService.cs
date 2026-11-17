using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

// Alta de clientes desde el chat (2026-11-17). Reglas, mismas que los pedidos por chat:
// - La IA sólo PREPARA el borrador con los datos que pidió (nombre, DNI, dirección). El Cliente se graba
//   recién cuando el contacto responde "sí" a ese resumen (lo decide ConversacionesService, no el modelo).
// - Antes de proponer un alta se busca si ya es cliente. Coincidencia fuerte y única (mismo DNI, mismo
//   teléfono, o mismo nombre y misma calle y altura) → se vincula la conversación a ese cliente, sin crear
//   otro. Coincidencias dudosas o varias → no se crea nada y queda una nota para que una persona decida.
// - Los valores comerciales del alta (IVA, provincia, lista, condición, vendedor) salen del cliente modelo
//   de CONV_ASISTENTE_ALTA_CLIENTE_MODELO o, sin él, de los valores más usados por los clientes activos.
public sealed class ConversacionBotClientesService(
    IConfiguration configuration,
    ISessionService sessionService,
    ICuentasComercialesService cuentasComercialesService,
    IAppEventService appEvents) : IConversacionBotClientesService
{
    private const string ModuleName = "Conversaciones";
    internal const string ClaveClienteModelo = "CONV_ASISTENTE_ALTA_CLIENTE_MODELO";
    internal static readonly TimeSpan VigenciaBorrador = TimeSpan.FromHours(12);
    private const int MaxCandidatos = 200;

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    internal sealed record DatosAlta(string Nombre, string Documento, string Calle, string Numero, string Localidad, string Referencia);

    internal sealed class ClienteExistente
    {
        public string Codigo { get; set; } = string.Empty;
        public string RazonSocial { get; set; } = string.Empty;
        public string Calle { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public string Localidad { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string Documento { get; set; } = string.Empty;
    }

    internal enum Coincidencia { Ninguna, Posible, Fuerte }

    public async Task<ConversacionBotAltaPreparada> PrepararAsync(long idConversacion, string argumentosJson, CancellationToken ct = default)
    {
        try
        {
            return await PrepararCoreAsync(idConversacion, argumentosJson, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await appEvents.LogErrorAsync(ModuleName, "BotAltaClientePreparar", ex,
                "No se pudo preparar el alta de cliente pedida por chat.", new { idConversacion }, AppEventSeverity.Warning, ct);
            return new ConversacionBotAltaPreparada(
                "No se pudo preparar el alta por un error del sistema. Respondé tipo DERIVA: avisale que alguien del equipo lo registra y le toma el pedido.");
        }
    }

    private async Task<ConversacionBotAltaPreparada> PrepararCoreAsync(long idConversacion, string argumentosJson, CancellationToken ct)
    {
        var datos = LeerDatos(argumentosJson);
        var faltan = DatosFaltantes(datos);
        if (faltan.Count > 0)
            return new ConversacionBotAltaPreparada(
                $"Faltan datos para registrarlo: {string.Join(", ", faltan)}. Respondé tipo ACLARA pidiendo SOLO lo que falta, en una sola pregunta. No digas que ya está registrado.");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        var telefono = await LeerTelefonoConversacionAsync(cn, idConversacion, ct);
        var candidatos = await BuscarCandidatosAsync(cn, datos, telefono, ct);
        var fuertes = candidatos.Where(c => Evaluar(datos, telefono, c) == Coincidencia.Fuerte).ToList();
        var posibles = candidatos.Where(c => Evaluar(datos, telefono, c) == Coincidencia.Posible).ToList();

        if (fuertes.Count == 1)
        {
            var cliente = fuertes[0];
            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.CONV_CONVERSACIONES SET ClienteCodigo = @Codigo
                WHERE IdConversacion = @IdConversacion AND ISNULL(LTRIM(RTRIM(ClienteCodigo)), N'') = N'';
                """,
                new { Codigo = cliente.Codigo.Trim(), IdConversacion = idConversacion }, cancellationToken: ct));
            await DescartarPendienteAsync(cn, idConversacion, ct);
            var vinculado = new ConversacionCuentaVinculadaDto(cliente.Codigo.Trim(), CuentaComercialTipo.Cliente, cliente.RazonSocial.Trim());
            return new ConversacionBotAltaPreparada(
                $"Ya es cliente: {cliente.RazonSocial.Trim()} ({DireccionTexto(cliente.Calle, cliente.Numero, cliente.Localidad)}). No hay que registrarlo. "
                + "Respondé tipo RESUELVE: decile que ya lo tenés registrado como cliente y seguí con lo que necesitaba (si quería hacer un pedido, preguntale qué y cuántos, o armalo si ya lo dijo).",
                $"👤 El asistente reconoció al contacto como el cliente {cliente.Codigo.Trim()} - {cliente.RazonSocial.Trim()} por los datos que dio ({MotivoCoincidencia(datos, telefono, cliente)}) y vinculó la conversación. Revisar si es correcto.",
                vinculado);
        }

        if (fuertes.Count > 1 || posibles.Count > 0)
        {
            var dudas = fuertes.Concat(posibles).Take(5)
                .Select(c => $"{c.Codigo.Trim()} - {c.RazonSocial.Trim()} ({DireccionTexto(c.Calle, c.Numero, c.Localidad)})");
            await DescartarPendienteAsync(cn, idConversacion, ct);
            return new ConversacionBotAltaPreparada(
                "Puede que ya esté registrado como cliente, pero no se puede confirmar por chat. No lo registres. "
                + "Respondé tipo DERIVA: decile que puede que ya lo tengan cargado y que alguien del equipo lo confirma enseguida para tomarle el pedido.",
                $"👤 Alta pedida por chat sin registrar: {DatosTexto(datos)}. Podría ser un cliente existente: {string.Join("; ", dudas)}. "
                + "Si es uno de ellos, vinculá la conversación a ese cliente; si no, dalo de alta.");
        }

        await cn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.CONV_BOT_CLIENTES SET Estado = N'REEMPLAZADO', FechaEstado = GETDATE()
            WHERE IdConversacion = @IdConversacion AND Estado = N'PENDIENTE';
            INSERT INTO dbo.CONV_BOT_CLIENTES (IdConversacion, Nombre, Documento, Calle, Numero, Localidad, Referencia, Telefono)
            VALUES (@IdConversacion, @Nombre, @Documento, @Calle, @Numero, @Localidad, @Referencia, @Telefono);
            """,
            new
            {
                IdConversacion = idConversacion,
                datos.Nombre,
                Documento = NullSiVacio(datos.Documento),
                datos.Calle,
                Numero = NullSiVacio(datos.Numero),
                datos.Localidad,
                Referencia = NullSiVacio(datos.Referencia),
                Telefono = NullSiVacio(TelefonoNacional(telefono))
            },
            cancellationToken: ct));

        return new ConversacionBotAltaPreparada(
            "Datos para registrarlo como cliente (todavía SIN registrar):\n" + DatosTexto(datos, conSaltos: true)
            + "\nRespondé tipo ACLARA mostrándole estos datos y preguntándole si están bien: que responda SÍ para registrarlo, o que te diga qué corregir. "
            + "NO digas que ya está registrado: se graba recién cuando confirma.");
    }

    public async Task<bool> HayPendienteAsync(long idConversacion, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        return await LeerPendienteAsync(cn, idConversacion, ct) is not null;
    }

    public async Task<ConversacionBotAltaConfirmada> ConfirmarAsync(long idConversacion, CancellationToken ct = default)
    {
        BorradorRow? pendiente;
        await using (var cn = new SqlConnection(ConnectionString))
        {
            await cn.OpenAsync(ct);
            pendiente = await LeerPendienteAsync(cn, idConversacion, ct);
        }

        if (pendiente is null)
            return new ConversacionBotAltaConfirmada("No tengo datos pendientes para registrar. Decime tu nombre y dirección y te registro.");

        try
        {
            var request = await ArmarAltaAsync(pendiente, idConversacion, ct);
            var codigo = (await cuentasComercialesService.SaveAsync(CuentaComercialTipo.Cliente, request, ct)).Trim();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE dbo.CONV_CONVERSACIONES SET ClienteCodigo = @Codigo
                WHERE IdConversacion = @IdConversacion AND ISNULL(LTRIM(RTRIM(ClienteCodigo)), N'') = N'';
                UPDATE dbo.CONV_BOT_CLIENTES SET Estado = N'CONFIRMADO', FechaEstado = GETDATE(), ClienteCodigo = @Codigo
                WHERE IdBorrador = @IdBorrador;
                """,
                new { Codigo = codigo, IdConversacion = idConversacion, pendiente.IdBorrador }, cancellationToken: ct));

            await appEvents.LogAuditAsync(ModuleName, "BotAltaCliente", "CONV_BOT_CLIENTES",
                pendiente.IdBorrador.ToString(CultureInfo.InvariantCulture),
                "Cliente dado de alta por el asistente desde el chat.", new { idConversacion, codigo }, ct);

            var nombre = PrimerNombre(pendiente.Nombre);
            return new ConversacionBotAltaConfirmada(
                $"¡Listo{(nombre.Length > 0 ? ", " + nombre : string.Empty)}! Ya quedaste registrado como cliente. ¿Qué te gustaría pedir?",
                new ConversacionCuentaVinculadaDto(codigo, CuentaComercialTipo.Cliente, request.RazonSocial),
                $"🆕 El asistente dio de alta al cliente {codigo} - {request.RazonSocial} ({DireccionTexto(pendiente.Calle, pendiente.Numero, pendiente.Localidad)}). Revisar la zona o día de reparto y la lista de precios.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var detalle = ex is AppValidationException validacion ? $"{validacion.UserMessage} {ex.Message}" : ex.Message;
            await using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync(ct);
                await cn.ExecuteAsync(new CommandDefinition(
                    "UPDATE dbo.CONV_BOT_CLIENTES SET Estado = N'ERROR', FechaEstado = GETDATE(), Error = LEFT(@Error, 500) WHERE IdBorrador = @IdBorrador;",
                    new { Error = detalle, pendiente.IdBorrador }, cancellationToken: ct));
            }
            await appEvents.LogErrorAsync(ModuleName, "BotAltaClienteConfirmar", ex,
                "No se pudo dar de alta el cliente pedido por chat.", new { idConversacion, pendiente.IdBorrador }, AppEventSeverity.Warning, ct);
            return new ConversacionBotAltaConfirmada(
                "No pude terminar tu registro en este momento. Alguien del equipo lo completa y te toma el pedido a la brevedad.",
                NotaInterna: $"⚠️ El asistente no pudo dar de alta al contacto ({pendiente.Nombre}, {DireccionTexto(pendiente.Calle, pendiente.Numero, pendiente.Localidad)}): {Truncar(detalle, 300)}. Darlo de alta a mano.");
        }
    }

    public async Task<string> CancelarAsync(long idConversacion, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        if (await LeerPendienteAsync(cn, idConversacion, ct) is null)
            return "No hay ningún registro pendiente. ¿Te ayudo con algo más?";

        await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.CONV_BOT_CLIENTES SET Estado = N'CANCELADO', FechaEstado = GETDATE() WHERE IdConversacion = @IdConversacion AND Estado = N'PENDIENTE';",
            new { IdConversacion = idConversacion }, cancellationToken: ct));
        return "Listo, no registré nada. Si querés, decime los datos correctos y lo armamos de nuevo.";
    }

    public async Task<ConversacionCuentaVinculadaDto?> BuscarPorTelefonoAsync(long idConversacion, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        var telefono = await LeerTelefonoConversacionAsync(cn, idConversacion, ct);
        var nacional = TelefonoNacional(telefono);
        if (nacional.Length < 8)
            return null;

        var filas = await cn.QueryAsync<ClienteExistente>(new CommandDefinition(
            """
            SELECT TOP (50) LTRIM(RTRIM(CODIGO)) AS Codigo, ISNULL(LTRIM(RTRIM(RAZON_SOCIAL)), N'') AS RazonSocial,
                   ISNULL(TELEFONO, N'') AS Telefono
            FROM dbo.VT_CLIENTES
            WHERE ISNULL(Dada_De_Baja, 0) = 0
              AND TELEFONO LIKE N'%' + @Ultimos + N'%';
            """,
            new { Ultimos = nacional[^6..] }, cancellationToken: ct));

        var coinciden = filas.Where(f => TelefonoCoincide(f.Telefono, telefono))
            .GroupBy(f => f.Codigo, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        return coinciden.Count == 1
            ? new ConversacionCuentaVinculadaDto(coinciden[0].Codigo, CuentaComercialTipo.Cliente, coinciden[0].RazonSocial) { PorTelefono = true }
            : null;
    }

    // ── Búsqueda de clientes existentes ──────────────────────────────────────────────────────────────

    private static async Task<List<ClienteExistente>> BuscarCandidatosAsync(SqlConnection cn, DatosAlta datos, string telefono, CancellationToken ct)
    {
        var tokensNombre = Tokens(datos.Nombre);
        var nombreClave = tokensNombre.OrderByDescending(t => t.Length).FirstOrDefault() ?? string.Empty;
        var calleClave = Tokens(datos.Calle).OrderByDescending(t => t.Length).FirstOrDefault() ?? string.Empty;
        var documento = SoloDigitos(datos.Documento);
        var nacional = TelefonoNacional(telefono);

        var filas = await cn.QueryAsync<ClienteExistente>(new CommandDefinition(
            $"""
            SELECT TOP ({MaxCandidatos}) LTRIM(RTRIM(CODIGO)) AS Codigo, ISNULL(LTRIM(RTRIM(RAZON_SOCIAL)), N'') AS RazonSocial,
                   ISNULL(LTRIM(RTRIM(CALLE)), N'') AS Calle, ISNULL(LTRIM(RTRIM(NUMERO)), N'') AS Numero,
                   ISNULL(LTRIM(RTRIM(LOCALIDAD)), N'') AS Localidad, ISNULL(TELEFONO, N'') AS Telefono,
                   ISNULL(LTRIM(RTRIM(NUMERO_DOCUMENTO)), N'') AS Documento
            FROM dbo.VT_CLIENTES
            WHERE ISNULL(Dada_De_Baja, 0) = 0
              AND (
                    (@Documento <> N'' AND REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(NUMERO_DOCUMENTO, N''))), N'.', N''), N'-', N''), N' ', N'') LIKE N'%' + @Documento + N'%')
                 OR (@Telefono <> N'' AND TELEFONO LIKE N'%' + @Telefono + N'%')
                 OR (@Nombre <> N'' AND RAZON_SOCIAL LIKE N'%' + @Nombre + N'%')
                 OR (@Calle <> N'' AND CALLE LIKE N'%' + @Calle + N'%')
              );
            """,
            new
            {
                Documento = documento.Length >= 7 ? documento : string.Empty,
                Telefono = nacional.Length >= 8 ? nacional[^6..] : string.Empty,
                Nombre = nombreClave.Length >= 3 ? nombreClave : string.Empty,
                Calle = calleClave.Length >= 3 ? calleClave : string.Empty
            },
            cancellationToken: ct));
        return filas.ToList();
    }

    internal static Coincidencia Evaluar(DatosAlta datos, string telefonoChat, ClienteExistente cliente)
    {
        var documento = SoloDigitos(datos.Documento);
        var documentoCliente = SoloDigitos(cliente.Documento);
        // CUIT/CUIL contiene el DNI en el medio: "20-23456789-3" coincide con el DNI 23456789.
        var mismoDocumento = documento.Length >= 7 && documentoCliente.Length >= 7
                             && (documento == documentoCliente || (documentoCliente.Length == 11 && documentoCliente.Substring(2, 8).TrimStart('0') == documento.TrimStart('0')));
        var mismoTelefono = TelefonoCoincide(cliente.Telefono, telefonoChat);
        var mismoNombre = NombreCoincide(datos.Nombre, cliente.RazonSocial);
        var mismaDireccion = DireccionCoincide(datos.Calle, datos.Numero, cliente.Calle, cliente.Numero);

        if (mismoDocumento || mismoTelefono || (mismoNombre && mismaDireccion))
            return Coincidencia.Fuerte;
        return mismoNombre || mismaDireccion ? Coincidencia.Posible : Coincidencia.Ninguna;
    }

    private static string MotivoCoincidencia(DatosAlta datos, string telefono, ClienteExistente cliente)
    {
        var documento = SoloDigitos(datos.Documento);
        if (documento.Length >= 7 && SoloDigitos(cliente.Documento).Contains(documento.TrimStart('0'), StringComparison.Ordinal))
            return "mismo documento";
        return TelefonoCoincide(cliente.Telefono, telefono) ? "mismo teléfono" : "mismo nombre y dirección";
    }

    /// <summary>Todas las palabras del nombre (de 3 letras o más) están en la razón social, sin importar el orden.</summary>
    internal static bool NombreCoincide(string nombre, string razonSocial)
    {
        var buscadas = Tokens(nombre).Where(t => t.Length >= 3).ToList();
        if (buscadas.Count < 2)
            return false;
        var existentes = Tokens(razonSocial).ToHashSet(StringComparer.Ordinal);
        return buscadas.All(existentes.Contains);
    }

    /// <summary>Misma altura y la calle de uno contiene a la del otro ("San Martín" / "Av. San Martín").</summary>
    internal static bool DireccionCoincide(string calle, string numero, string calleCliente, string numeroCliente)
    {
        var n1 = SoloDigitos(numero);
        var n2 = SoloDigitos(numeroCliente);
        if (n1.Length == 0 || n1 != n2)
            return false;
        var c1 = string.Join(' ', Tokens(calle).Where(t => !PalabrasCalle.Contains(t)));
        var c2 = string.Join(' ', Tokens(calleCliente).Where(t => !PalabrasCalle.Contains(t)));
        return c1.Length >= 3 && c2.Length >= 3 && (c1.Contains(c2, StringComparison.Ordinal) || c2.Contains(c1, StringComparison.Ordinal));
    }

    private static readonly HashSet<string> PalabrasCalle = new(StringComparer.Ordinal)
    {
        "calle", "av", "avda", "avenida", "pasaje", "pje", "ruta", "barrio", "bo", "de", "del", "la", "las", "los", "el"
    };

    /// <summary>
    /// El teléfono cargado del cliente (puede traer varios, con espacios o guiones) coincide con el del chat:
    /// mismos últimos 10 dígitos, o un número local de 6 a 8 dígitos con el que termina el del chat.
    /// </summary>
    internal static bool TelefonoCoincide(string? telefonoCliente, string? telefonoChat)
    {
        var chat = TelefonoNacional(telefonoChat);
        if (chat.Length < 8 || string.IsNullOrWhiteSpace(telefonoCliente))
            return false;

        foreach (var parte in Regex.Split(telefonoCliente, @"[/,;]|\by\b|\bo\b"))
        {
            var digitos = SoloDigitos(parte);
            if (digitos.Length >= 10 && TelefonoNacional(digitos) == chat)
                return true;
            if (digitos.Length is >= 6 and <= 8 && chat.EndsWith(digitos, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Últimos 10 dígitos (código de área + número, sin 54 ni 9 de celular), o todos si son menos.</summary>
    internal static string TelefonoNacional(string? telefono)
    {
        var digitos = SoloDigitos(telefono);
        return digitos.Length > 10 ? digitos[^10..] : digitos;
    }

    // ── Alta ─────────────────────────────────────────────────────────────────────────────────────────

    private sealed class ValoresAlta
    {
        public string Iva { get; set; } = string.Empty;
        public string TipoDocumento { get; set; } = string.Empty;
        public string Provincia { get; set; } = string.Empty;
        public string Pais { get; set; } = string.Empty;
        public string Condicion { get; set; } = string.Empty;
        public string Lista { get; set; } = string.Empty;
        public string Vendedor { get; set; } = string.Empty;
        public string Clase { get; set; } = string.Empty;
    }

    private async Task<CuentaComercialSaveRequest> ArmarAltaAsync(BorradorRow borrador, long idConversacion, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        var modelo = (await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT TOP (1) LTRIM(RTRIM(VALOR)) FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;",
            new { Clave = ClaveClienteModelo }, cancellationToken: ct)) ?? string.Empty).Trim();

        // Cliente modelo configurado o, sin él, el valor más usado por los clientes activos (nada fijo en código).
        var valores = await cn.QueryFirstOrDefaultAsync<ValoresAlta>(new CommandDefinition(
            """
            ;WITH c AS (
                SELECT * FROM dbo.VT_CLIENTES
                WHERE ISNULL(Dada_De_Baja, 0) = 0
                  AND (@Modelo = N'' OR UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(@Modelo))
            )
            SELECT
                (SELECT TOP (1) LTRIM(RTRIM(CODIGO)) FROM dbo.TA_CONDIVA
                 WHERE @Modelo = N'' AND UPPER(DESCRIPCION) LIKE N'%CONSUMIDOR%FINAL%') AS Iva,
                (SELECT TOP (1) LTRIM(RTRIM(CODIGO)) FROM dbo.TA_TIPODOCUMENTO
                 WHERE UPPER(REPLACE(REPLACE(DESCRIPCION, N'.', N''), N' ', N'')) = N'DNI') AS TipoDocumento,
                (SELECT TOP (1) LTRIM(RTRIM(CAST(PROVINCIA AS nvarchar(10)))) FROM c WHERE ISNULL(PROVINCIA, N'') <> N'' GROUP BY PROVINCIA ORDER BY COUNT(*) DESC) AS Provincia,
                (SELECT TOP (1) LTRIM(RTRIM(CAST(PAIS AS nvarchar(10)))) FROM c WHERE ISNULL(PAIS, N'') <> N'' GROUP BY PAIS ORDER BY COUNT(*) DESC) AS Pais,
                (SELECT TOP (1) LTRIM(RTRIM(CAST(idCond_Cpra_Vta AS nvarchar(10)))) FROM c WHERE ISNULL(idCond_Cpra_Vta, N'') <> N'' GROUP BY idCond_Cpra_Vta ORDER BY COUNT(*) DESC) AS Condicion,
                (SELECT TOP (1) LTRIM(RTRIM(CAST(IdLista AS nvarchar(10)))) FROM c WHERE ISNULL(IdLista, N'') <> N'' GROUP BY IdLista ORDER BY COUNT(*) DESC) AS Lista,
                (SELECT TOP (1) LTRIM(RTRIM(CAST(IdVendedor AS nvarchar(10)))) FROM c WHERE ISNULL(IdVendedor, N'') <> N'' GROUP BY IdVendedor ORDER BY COUNT(*) DESC) AS Vendedor,
                (SELECT TOP (1) LTRIM(RTRIM(CAST(Clase AS nvarchar(10)))) FROM c WHERE Clase IS NOT NULL GROUP BY Clase ORDER BY COUNT(*) DESC) AS Clase;
            """,
            new { Modelo = modelo }, cancellationToken: ct)) ?? new ValoresAlta();

        if (string.IsNullOrWhiteSpace(valores.Iva))
        {
            valores.Iva = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
                """
                SELECT TOP (1) LTRIM(RTRIM(CAST(IVA AS nvarchar(10)))) FROM dbo.VT_CLIENTES
                WHERE ISNULL(Dada_De_Baja, 0) = 0 AND (@Modelo = N'' OR UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(@Modelo))
                GROUP BY IVA ORDER BY COUNT(*) DESC;
                """,
                new { Modelo = modelo }, cancellationToken: ct)) ?? string.Empty;
        }

        var documento = SoloDigitos(borrador.Documento);
        return new CuentaComercialSaveRequest
        {
            RazonSocial = Truncar(borrador.Nombre, 50),
            Contacto = Truncar(borrador.Nombre, 70),
            Calle = Truncar(borrador.Calle, 50),
            Numero = Truncar(borrador.Numero ?? string.Empty, 6),
            Localidad = Truncar(borrador.Localidad, 50),
            ProvinciaCodigo = valores.Provincia,
            PaisCodigo = valores.Pais,
            Telefono = Truncar(borrador.Telefono ?? string.Empty, 50),
            DocumentoTipoCodigo = valores.TipoDocumento,
            // El alta exige documento: sin DNI queda en 0 (como otros clientes de mostrador) para completarlo después.
            NumeroDocumento = documento.Length > 0 ? Truncar(documento, 13) : "0",
            IvaCodigo = valores.Iva,
            CondicionComercialCodigo = valores.Condicion,
            ListaPrecioCodigo = valores.Lista,
            VendedorCodigo = valores.Vendedor,
            Clase = short.TryParse(valores.Clase, NumberStyles.Integer, CultureInfo.InvariantCulture, out var clase) ? clase : null,
            LugarEntrega = Truncar(borrador.Referencia ?? string.Empty, 200),
            Observaciones = Truncar($"Alta por chat (asistente) - Conversación #{idConversacion}", 200)
        };
    }

    // ── Borradores ───────────────────────────────────────────────────────────────────────────────────

    private sealed class BorradorRow
    {
        public int IdBorrador { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string? Documento { get; set; }
        public string Calle { get; set; } = string.Empty;
        public string? Numero { get; set; }
        public string Localidad { get; set; } = string.Empty;
        public string? Referencia { get; set; }
        public string? Telefono { get; set; }
    }

    private static async Task<BorradorRow?> LeerPendienteAsync(SqlConnection cn, long idConversacion, CancellationToken ct)
    {
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID(N'dbo.CONV_BOT_CLIENTES', N'U') IS NULL THEN 0 ELSE 1 END;", cancellationToken: ct)) == 0)
            return null;

        return await cn.QueryFirstOrDefaultAsync<BorradorRow>(new CommandDefinition(
            """
            SELECT TOP (1) IdBorrador, Nombre, Documento, Calle, Numero, Localidad, Referencia, Telefono
            FROM dbo.CONV_BOT_CLIENTES
            WHERE IdConversacion = @IdConversacion AND Estado = N'PENDIENTE' AND FechaAlta >= @Desde
            ORDER BY IdBorrador DESC;
            """,
            new { IdConversacion = idConversacion, Desde = DateTime.Now - VigenciaBorrador }, cancellationToken: ct));
    }

    private static Task DescartarPendienteAsync(SqlConnection cn, long idConversacion, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition(
            """
            IF OBJECT_ID(N'dbo.CONV_BOT_CLIENTES', N'U') IS NOT NULL
                UPDATE dbo.CONV_BOT_CLIENTES SET Estado = N'REEMPLAZADO', FechaEstado = GETDATE()
                WHERE IdConversacion = @IdConversacion AND Estado = N'PENDIENTE';
            """,
            new { IdConversacion = idConversacion }, cancellationToken: ct));

    private static async Task<string> LeerTelefonoConversacionAsync(SqlConnection cn, long idConversacion, CancellationToken ct)
        => await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT TOP (1) ISNULL(TelefonoWhatsApp, N'') FROM dbo.CONV_CONVERSACIONES WHERE IdConversacion = @IdConversacion;",
            new { IdConversacion = idConversacion }, cancellationToken: ct)) ?? string.Empty;

    // ── Texto ────────────────────────────────────────────────────────────────────────────────────────

    internal static DatosAlta LeerDatos(string? argumentosJson)
    {
        string Leer(JsonElement raiz, string nombre)
            => raiz.TryGetProperty(nombre, out var v) ? v.ValueKind switch
            {
                JsonValueKind.String => (v.GetString() ?? string.Empty).Trim(),
                JsonValueKind.Number => v.GetRawText(),
                _ => string.Empty
            } : string.Empty;

        if (string.IsNullOrWhiteSpace(argumentosJson))
            return new DatosAlta(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        try
        {
            using var doc = JsonDocument.Parse(argumentosJson);
            var r = doc.RootElement;
            return new DatosAlta(
                Regex.Replace(Leer(r, "nombre_apellido"), @"\s+", " "),
                Leer(r, "dni"),
                Leer(r, "calle"),
                Leer(r, "numero"),
                Leer(r, "localidad"),
                Leer(r, "referencia"));
        }
        catch (JsonException)
        {
            return new DatosAlta(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }
    }

    internal static List<string> DatosFaltantes(DatosAlta datos)
    {
        var faltan = new List<string>();
        if (Tokens(datos.Nombre).Count < 2)
            faltan.Add("nombre y apellido");
        if (datos.Calle.Length == 0)
            faltan.Add("calle");
        if (datos.Numero.Length == 0 && datos.Referencia.Length == 0)
            faltan.Add("número de la casa (o, si no tiene, una referencia para encontrarla)");
        if (datos.Localidad.Length == 0)
            faltan.Add("localidad");
        return faltan;
    }

    private static string DatosTexto(DatosAlta d, bool conSaltos = false)
    {
        var partes = new List<string> { $"Nombre: {d.Nombre}" };
        if (d.Documento.Length > 0)
            partes.Add($"DNI: {d.Documento}");
        partes.Add($"Dirección: {DireccionTexto(d.Calle, d.Numero, d.Localidad)}");
        if (d.Referencia.Length > 0)
            partes.Add($"Referencia: {d.Referencia}");
        return conSaltos ? string.Join('\n', partes.Select(p => "- " + p)) : string.Join(", ", partes);
    }

    internal static string DireccionTexto(string? calle, string? numero, string? localidad)
    {
        var direccion = $"{(calle ?? string.Empty).Trim()} {(numero ?? string.Empty).Trim()}".Trim();
        var loc = (localidad ?? string.Empty).Trim();
        return loc.Length == 0 || loc == "." ? direccion : $"{direccion}, {loc}";
    }

    private static List<string> Tokens(string? texto)
        => Regex.Split(ConversacionAsistenteHerramientasService.NormalizarTexto(texto), "[^a-z0-9]+")
            .Where(t => t.Length > 0)
            .ToList();

    private static string PrimerNombre(string nombre)
    {
        var primero = (nombre ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return primero.Length == 0 ? primero : CultureInfo.GetCultureInfo("es-AR").TextInfo.ToTitleCase(primero.ToLowerInvariant());
    }

    private static string SoloDigitos(string? texto) => new((texto ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    private static string? NullSiVacio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private static string Truncar(string texto, int max) => texto.Length <= max ? texto : texto[..max];
}
