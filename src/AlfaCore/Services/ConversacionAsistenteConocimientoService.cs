using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AlfaCore.Models;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IConversacionAsistenteConocimientoService
{
    /// <summary>Hay API key de OpenAI en el servidor (necesaria para subir y buscar archivos).</summary>
    bool ArchivosDisponibles { get; }

    Task<IReadOnlyList<AsistenteBloqueDto>> GetBloquesAsync(CancellationToken ct = default);
    Task<int> SaveBloqueAsync(AsistenteBloqueDto bloque, CancellationToken ct = default);
    Task DeleteBloqueAsync(int idBloque, CancellationToken ct = default);

    /// <summary>Lista los archivos; con <paramref name="refrescarEstados"/> consulta en OpenAI los que siguen procesándose.</summary>
    Task<IReadOnlyList<AsistenteArchivoDto>> GetArchivosAsync(bool refrescarEstados, CancellationToken ct = default);
    Task<AsistenteArchivoDto> SubirArchivoAsync(Stream contenido, string nombreArchivo, string tipoContenido, long tamano, CancellationToken ct = default);
    Task DeleteArchivoAsync(int idArchivo, CancellationToken ct = default);

    /// <summary>
    /// Información que recibe el bot: la "Información general", los bloques activos y, si hay archivos
    /// listos y una consulta, los fragmentos más relevantes. Nunca lanza: ante cualquier falla devuelve
    /// al menos la información general.
    /// </summary>
    Task<string> ComponerInformacionAsync(string informacionGeneral, string? consulta, CancellationToken ct = default);
}

/// <summary>
/// Conocimiento del asistente (2026-10-05): bloques temáticos en la base del cliente y archivos en un
/// vector store de OpenAI propio de cada base (OpenAI los procesa y busca por significado). Aislamiento:
/// el vector store se crea con metadata <c>alfacore_base</c> y, antes de usarlo, se verifica que coincida
/// con la base activa; si no coincide no se busca ni se sube nada y se registra en AUX_ERR.
/// </summary>
public sealed class ConversacionAsistenteConocimientoService(
    ISessionService sessionService,
    IAppUserSessionService appUserSession,
    IConversacionesAuthorizationService authorization,
    IHttpClientFactory httpClientFactory,
    IAppEventService appEvents,
    IIaUsoRecorder iaUso) : IConversacionAsistenteConocimientoService
{
    private const string ModuleName = "Conversaciones";
    private const string ClaveVectorStore = "CONV_ASISTENTE_VECTOR_STORE_ID";
    private const string ClaveUsoAlmacenamiento = "CONV_ASISTENTE_VS_USO_DIA";
    private const string ConfigGroup = "CONVERSACIONES";
    private const string MetadataBase = "alfacore_base";
    internal const long TamanoMaximo = 20 * 1024 * 1024;
    internal const int FragmentosMaximos = 5;
    internal const int CaracteresMaximosFragmentos = 6000;

    internal static readonly IReadOnlySet<string> ExtensionesPermitidas =
        new HashSet<string>([".pdf", ".txt", ".md", ".docx", ".doc", ".pptx", ".html", ".htm", ".json"], StringComparer.OrdinalIgnoreCase);

    // Vector stores ya verificados contra su base en este proceso: evita un GET a OpenAI por mensaje.
    private static readonly ConcurrentDictionary<string, string> VectorStoresVerificados = new(StringComparer.Ordinal);

    public bool ArchivosDisponibles => ApiKey.Length > 0;

    private static string ApiKey => (Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty).Trim();

    private string ConnectionString => sessionService.GetConnectionString();

    private string ClaveBase
    {
        get
        {
            var sesion = sessionService.GetActiveSession();
            return sesion?.BaseId is > 0
                ? sesion.BaseId.ToString(CultureInfo.InvariantCulture)
                : (sesion?.BaseDatos ?? string.Empty).Trim().ToUpperInvariant();
        }
    }

    // ---------------------------------------------------------------- bloques

    public Task<IReadOnlyList<AsistenteBloqueDto>> GetBloquesAsync(CancellationToken ct = default)
        => LoggedAsync("GetBloquesAsistente", async () =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            return (IReadOnlyList<AsistenteBloqueDto>)await LeerBloquesAsync(cn, soloActivos: false, ct);
        }, "No se pudieron cargar los bloques de información del asistente.", ct);

    public Task<int> SaveBloqueAsync(AsistenteBloqueDto bloque, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bloque);
        if (string.IsNullOrWhiteSpace(bloque.Titulo))
            throw new InvalidOperationException("El bloque necesita un título.");

        return LoggedAsync("SaveBloqueAsistente", async () =>
        {
            await authorization.EnsureCanManageAsync(ct);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            await RequerirTablaAsync(cn, "CONV_ASISTENTE_BLOQUES", ct);

            await using var cmd = new SqlCommand(bloque.IdBloque > 0
                ? """
                    UPDATE dbo.CONV_ASISTENTE_BLOQUES
                       SET Categoria = @Categoria, Titulo = @Titulo, Contenido = @Contenido, Activo = @Activo, Orden = @Orden,
                           FechaModificacion = GETDATE(), UsuarioModificacion = @Usuario
                     WHERE IdBloque = @IdBloque;
                    SELECT @IdBloque;
                    """
                : """
                    INSERT INTO dbo.CONV_ASISTENTE_BLOQUES (Categoria, Titulo, Contenido, Activo, Orden, UsuarioAlta)
                    VALUES (@Categoria, @Titulo, @Contenido, @Activo, @Orden, @Usuario);
                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """, cn);
            cmd.Parameters.AddWithValue("@Categoria", NormalizarCategoria(bloque.Categoria));
            cmd.Parameters.AddWithValue("@Titulo", Recortar(bloque.Titulo, 150));
            cmd.Parameters.AddWithValue("@Contenido", (bloque.Contenido ?? string.Empty).Trim());
            cmd.Parameters.AddWithValue("@Activo", bloque.Activo);
            cmd.Parameters.AddWithValue("@Orden", bloque.Orden <= 0 ? 100 : bloque.Orden);
            cmd.Parameters.AddWithValue("@Usuario", Usuario);
            if (bloque.IdBloque > 0)
                cmd.Parameters.AddWithValue("@IdBloque", bloque.IdBloque);

            var id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            await appEvents.LogAuditAsync(ModuleName, "SaveBloqueAsistente", "CONV_ASISTENTE_BLOQUES",
                id.ToString(CultureInfo.InvariantCulture), "Bloque de información del asistente guardado.",
                new { id, bloque.Titulo, bloque.Activo }, ct);
            return id;
        }, "No se pudo guardar el bloque.", ct);
    }

    public Task DeleteBloqueAsync(int idBloque, CancellationToken ct = default)
        => LoggedAsync("DeleteBloqueAsistente", async () =>
        {
            await authorization.EnsureCanManageAsync(ct);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            await RequerirTablaAsync(cn, "CONV_ASISTENTE_BLOQUES", ct);
            await using var cmd = new SqlCommand("DELETE FROM dbo.CONV_ASISTENTE_BLOQUES WHERE IdBloque = @IdBloque;", cn);
            cmd.Parameters.AddWithValue("@IdBloque", idBloque);
            await cmd.ExecuteNonQueryAsync(ct);
            await appEvents.LogAuditAsync(ModuleName, "DeleteBloqueAsistente", "CONV_ASISTENTE_BLOQUES",
                idBloque.ToString(CultureInfo.InvariantCulture), "Bloque de información del asistente eliminado.", new { idBloque }, ct);
            return true;
        }, "No se pudo eliminar el bloque.", ct);

    // ---------------------------------------------------------------- archivos

    public Task<IReadOnlyList<AsistenteArchivoDto>> GetArchivosAsync(bool refrescarEstados, CancellationToken ct = default)
        => LoggedAsync("GetArchivosAsistente", async () =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            if (!await ExisteTablaAsync(cn, "CONV_ASISTENTE_ARCHIVOS", ct))
                return (IReadOnlyList<AsistenteArchivoDto>)[];

            var archivos = await LeerArchivosAsync(cn, ct);
            if (refrescarEstados && ArchivosDisponibles && archivos.Any(a => a.Estado == AsistenteArchivoEstados.Procesando))
            {
                var vectorStoreId = await LeerConfigAsync(cn, ClaveVectorStore, ct);
                if (!string.IsNullOrWhiteSpace(vectorStoreId))
                {
                    foreach (var archivo in archivos.Where(a => a.Estado == AsistenteArchivoEstados.Procesando && a.OpenAiFileId.Length > 0))
                    {
                        var (estado, error) = await ConsultarEstadoArchivoAsync(vectorStoreId, archivo.OpenAiFileId, ct);
                        if (estado != archivo.Estado)
                        {
                            await ActualizarEstadoAsync(cn, archivo.IdArchivo, estado, error, ct);
                            archivo.Estado = estado;
                            archivo.UltimoError = error;
                        }
                    }
                }
            }

            await RegistrarAlmacenamientoDiarioAsync(cn, ct);
            return (IReadOnlyList<AsistenteArchivoDto>)archivos;
        }, "No se pudieron cargar los archivos del asistente.", ct);

    public Task<AsistenteArchivoDto> SubirArchivoAsync(Stream contenido, string nombreArchivo, string tipoContenido, long tamano, CancellationToken ct = default)
    {
        var error = ValidarArchivo(nombreArchivo, tamano);
        if (error is not null)
            throw new InvalidOperationException(error);
        if (!ArchivosDisponibles)
            throw new InvalidOperationException("Falta la API key de OpenAI en el servidor: no se pueden subir archivos.");

        return LoggedAsync("SubirArchivoAsistente", async () =>
        {
            await authorization.EnsureCanManageAsync(ct);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            await RequerirTablaAsync(cn, "CONV_ASISTENTE_ARCHIVOS", ct);

            var vectorStoreId = await ObtenerOCrearVectorStoreAsync(cn, ct);
            var nombre = Path.GetFileName(nombreArchivo.Trim());

            using var client = CrearCliente(TimeSpan.FromMinutes(3));
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("assistants"), "purpose");
            var fileContent = new StreamContent(contenido);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(tipoContenido) ? "application/octet-stream" : tipoContenido);
            form.Add(fileContent, "file", nombre);
            using var uploadResponse = await client.PostAsync("https://api.openai.com/v1/files", form, ct);
            var uploadBody = await uploadResponse.Content.ReadAsStringAsync(ct);
            if (!uploadResponse.IsSuccessStatusCode)
                throw new InvalidOperationException($"OpenAI no aceptó el archivo ({(int)uploadResponse.StatusCode}). {ExtraerMensajeError(uploadBody)}");
            var fileId = LeerString(uploadBody, "id");

            using var attachResponse = await client.PostAsync(
                $"https://api.openai.com/v1/vector_stores/{vectorStoreId}/files",
                JsonContent(new { file_id = fileId }), ct);
            var attachBody = await attachResponse.Content.ReadAsStringAsync(ct);
            if (!attachResponse.IsSuccessStatusCode)
            {
                await BorrarEnOpenAiAsync(client, null, fileId, ct);
                throw new InvalidOperationException($"OpenAI no pudo indexar el archivo ({(int)attachResponse.StatusCode}). {ExtraerMensajeError(attachBody)}");
            }

            var (estado, errorOpenAi) = MapearEstado(attachBody);
            await using var cmd = new SqlCommand("""
                INSERT INTO dbo.CONV_ASISTENTE_ARCHIVOS (NombreArchivo, TipoContenido, Tamano, OpenAiFileId, VectorStoreId, Estado, UltimoError, UsuarioAlta)
                VALUES (@Nombre, @Tipo, @Tamano, @FileId, @VectorStoreId, @Estado, @Error, @Usuario);
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, cn);
            cmd.Parameters.AddWithValue("@Nombre", Recortar(nombre, 260));
            cmd.Parameters.AddWithValue("@Tipo", Recortar(tipoContenido, 120));
            cmd.Parameters.AddWithValue("@Tamano", tamano);
            cmd.Parameters.AddWithValue("@FileId", fileId);
            cmd.Parameters.AddWithValue("@VectorStoreId", vectorStoreId);
            cmd.Parameters.AddWithValue("@Estado", estado);
            cmd.Parameters.AddWithValue("@Error", Recortar(errorOpenAi, 500));
            cmd.Parameters.AddWithValue("@Usuario", Usuario);
            var id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);

            await appEvents.LogAuditAsync(ModuleName, "SubirArchivoAsistente", "CONV_ASISTENTE_ARCHIVOS",
                id.ToString(CultureInfo.InvariantCulture), "Archivo de conocimiento del asistente subido.",
                new { id, nombre, tamano, fileId }, ct);

            return new AsistenteArchivoDto
            {
                IdArchivo = id,
                NombreArchivo = nombre,
                TipoContenido = tipoContenido,
                Tamano = tamano,
                Estado = estado,
                UltimoError = errorOpenAi,
                FechaAlta = DateTime.Now,
                UsuarioAlta = Usuario,
                OpenAiFileId = fileId
            };
        }, "No se pudo subir el archivo.", ct);
    }

    public Task DeleteArchivoAsync(int idArchivo, CancellationToken ct = default)
        => LoggedAsync("DeleteArchivoAsistente", async () =>
        {
            await authorization.EnsureCanManageAsync(ct);
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            await RequerirTablaAsync(cn, "CONV_ASISTENTE_ARCHIVOS", ct);

            string? fileId = null;
            string? vectorStoreId = null;
            await using (var read = new SqlCommand("SELECT OpenAiFileId, VectorStoreId FROM dbo.CONV_ASISTENTE_ARCHIVOS WHERE IdArchivo = @Id AND Baja = 0;", cn))
            {
                read.Parameters.AddWithValue("@Id", idArchivo);
                await using var rd = await read.ExecuteReaderAsync(ct);
                if (await rd.ReadAsync(ct))
                {
                    fileId = rd.IsDBNull(0) ? null : rd.GetString(0);
                    vectorStoreId = rd.IsDBNull(1) ? null : rd.GetString(1);
                }
            }

            if (!string.IsNullOrWhiteSpace(fileId) && ArchivosDisponibles)
            {
                using var client = CrearCliente(TimeSpan.FromSeconds(30));
                await BorrarEnOpenAiAsync(client, vectorStoreId, fileId, ct);
            }

            await using var cmd = new SqlCommand("UPDATE dbo.CONV_ASISTENTE_ARCHIVOS SET Baja = 1, FechaModificacion = GETDATE() WHERE IdArchivo = @Id;", cn);
            cmd.Parameters.AddWithValue("@Id", idArchivo);
            await cmd.ExecuteNonQueryAsync(ct);
            await appEvents.LogAuditAsync(ModuleName, "DeleteArchivoAsistente", "CONV_ASISTENTE_ARCHIVOS",
                idArchivo.ToString(CultureInfo.InvariantCulture), "Archivo de conocimiento del asistente eliminado (también en OpenAI).",
                new { idArchivo, fileId }, ct);
            return true;
        }, "No se pudo eliminar el archivo.", ct);

    // ---------------------------------------------------------------- bot

    public async Task<string> ComponerInformacionAsync(string informacionGeneral, string? consulta, CancellationToken ct = default)
    {
        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            var bloques = await ExisteTablaAsync(cn, "CONV_ASISTENTE_BLOQUES", ct)
                ? await LeerBloquesAsync(cn, soloActivos: true, ct)
                : [];

            IReadOnlyList<AsistenteFragmentoDto> fragmentos = [];
            if (!string.IsNullOrWhiteSpace(consulta) && ArchivosDisponibles
                && await ExisteTablaAsync(cn, "CONV_ASISTENTE_ARCHIVOS", ct)
                && await HayArchivosListosAsync(cn, ct))
            {
                var vectorStoreId = await LeerConfigAsync(cn, ClaveVectorStore, ct);
                if (!string.IsNullOrWhiteSpace(vectorStoreId) && await VectorStoreEsDeLaBaseAsync(vectorStoreId, ct))
                {
                    fragmentos = await BuscarAsync(vectorStoreId, consulta, ct);
                    await RegistrarAlmacenamientoDiarioAsync(cn, ct);
                }
            }

            return ComponerInformacion(informacionGeneral, bloques, fragmentos);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(ModuleName, "ComponerInformacionAsistente", ex,
                "No se pudo sumar los bloques/archivos a la información del asistente; respondió con la información general.",
                null, AppEventSeverity.Warning, ct);
            return informacionGeneral ?? string.Empty;
        }
    }

    /// <summary>Arma el texto que recibe el modelo: información general, bloques por categoría y fragmentos.</summary>
    internal static string ComponerInformacion(
        string? informacionGeneral,
        IEnumerable<AsistenteBloqueDto> bloques,
        IEnumerable<AsistenteFragmentoDto> fragmentos)
    {
        var sb = new StringBuilder();
        var general = (informacionGeneral ?? string.Empty).Trim();
        if (general.Length > 0)
            sb.AppendLine(general);

        foreach (var grupo in bloques
                     .Where(b => b.Activo && !string.IsNullOrWhiteSpace(b.Contenido))
                     .OrderBy(b => b.Orden).ThenBy(b => b.IdBloque)
                     .GroupBy(b => NormalizarCategoria(b.Categoria)))
        {
            if (sb.Length > 0)
                sb.AppendLine();
            sb.AppendLine($"## {AsistenteBloqueCategorias.Nombre(grupo.Key)}");
            foreach (var bloque in grupo)
            {
                sb.AppendLine($"### {bloque.Titulo.Trim()}");
                sb.AppendLine(bloque.Contenido.Trim());
            }
        }

        var restantes = CaracteresMaximosFragmentos;
        var textos = new List<string>();
        foreach (var f in fragmentos)
        {
            var texto = f.Texto.Trim();
            if (texto.Length == 0 || restantes <= 0)
                continue;
            if (texto.Length > restantes)
                texto = texto[..restantes];
            restantes -= texto.Length;
            textos.Add($"[{f.Archivo}]\n{texto}");
        }

        if (textos.Count > 0)
        {
            if (sb.Length > 0)
                sb.AppendLine();
            sb.AppendLine("## Fragmentos de documentos del negocio relacionados con la consulta");
            sb.AppendLine("Usalos solo si responden a lo que preguntó el cliente.");
            foreach (var t in textos)
            {
                sb.AppendLine();
                sb.AppendLine(t);
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>Valida nombre y tamaño antes de subir; devuelve el mensaje de error o null.</summary>
    internal static string? ValidarArchivo(string? nombreArchivo, long tamano)
    {
        var nombre = Path.GetFileName((nombreArchivo ?? string.Empty).Trim());
        if (nombre.Length == 0)
            return "El archivo no tiene nombre.";
        if (!ExtensionesPermitidas.Contains(Path.GetExtension(nombre)))
            return $"Formato no admitido. Podés subir: {string.Join(", ", ExtensionesPermitidas.Order())}.";
        if (tamano <= 0)
            return "El archivo está vacío.";
        if (tamano > TamanoMaximo)
            return $"El archivo supera el máximo de {TamanoMaximo / (1024 * 1024)} MB.";
        return null;
    }

    /// <summary>Lee la respuesta de /vector_stores/{id}/search.</summary>
    internal static IReadOnlyList<AsistenteFragmentoDto> LeerResultadosBusqueda(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        var resultado = new List<AsistenteFragmentoDto>();
        foreach (var item in data.EnumerateArray())
        {
            var archivo = item.TryGetProperty("filename", out var fn) && fn.ValueKind == JsonValueKind.String ? fn.GetString() ?? string.Empty : string.Empty;
            var puntaje = item.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetDouble() : 0;
            var texto = new StringBuilder();
            if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var parte in content.EnumerateArray())
                {
                    if (parte.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        texto.AppendLine(t.GetString());
                }
            }

            if (texto.Length > 0)
                resultado.Add(new AsistenteFragmentoDto(archivo, puntaje, texto.ToString().Trim()));
        }

        return resultado;
    }

    /// <summary>El vector store fue creado por AlfaCore para esta base (metadata alfacore_base).</summary>
    internal static bool PerteneceALaBase(string vectorStoreJson, string claveBase)
    {
        using var doc = JsonDocument.Parse(vectorStoreJson);
        return doc.RootElement.TryGetProperty("metadata", out var metadata)
               && metadata.ValueKind == JsonValueKind.Object
               && metadata.TryGetProperty(MetadataBase, out var valor)
               && valor.ValueKind == JsonValueKind.String
               && claveBase.Length > 0
               && string.Equals(valor.GetString(), claveBase, StringComparison.OrdinalIgnoreCase);
    }

    internal static (string Estado, string Error) MapearEstado(string vectorStoreFileJson)
    {
        using var doc = JsonDocument.Parse(vectorStoreFileJson);
        var status = doc.RootElement.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        var error = doc.RootElement.TryGetProperty("last_error", out var le) && le.ValueKind == JsonValueKind.Object
                    && le.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString() ?? string.Empty
            : string.Empty;
        return status switch
        {
            "completed" => (AsistenteArchivoEstados.Listo, string.Empty),
            "failed" or "cancelled" => (AsistenteArchivoEstados.Error, string.IsNullOrWhiteSpace(error) ? "OpenAI no pudo procesar el archivo." : error),
            _ => (AsistenteArchivoEstados.Procesando, string.Empty)
        };
    }

    // ---------------------------------------------------------------- OpenAI

    private async Task<string> ObtenerOCrearVectorStoreAsync(SqlConnection cn, CancellationToken ct)
    {
        var claveBase = ClaveBase;
        if (claveBase.Length == 0)
            throw new InvalidOperationException("No se pudo identificar la base activa para guardar los archivos.");

        var existente = await LeerConfigAsync(cn, ClaveVectorStore, ct);
        if (!string.IsNullOrWhiteSpace(existente))
        {
            if (!await VectorStoreEsDeLaBaseAsync(existente, ct))
                throw new InvalidOperationException("El almacén de archivos configurado no corresponde a esta base. Se registró el incidente; contactá a soporte.");
            return existente;
        }

        using var client = CrearCliente(TimeSpan.FromSeconds(30));
        using var response = await client.PostAsync("https://api.openai.com/v1/vector_stores", JsonContent(new
        {
            name = $"alfacore-base-{claveBase}",
            metadata = new Dictionary<string, string> { [MetadataBase] = claveBase }
        }), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI no pudo crear el almacén de archivos ({(int)response.StatusCode}). {ExtraerMensajeError(body)}");

        var id = LeerString(body, "id");
        await GuardarConfigAsync(cn, ClaveVectorStore, id, ct);
        VectorStoresVerificados[id] = claveBase;
        return id;
    }

    private async Task<bool> VectorStoreEsDeLaBaseAsync(string vectorStoreId, CancellationToken ct)
    {
        var claveBase = ClaveBase;
        if (VectorStoresVerificados.TryGetValue(vectorStoreId, out var verificada))
            return string.Equals(verificada, claveBase, StringComparison.OrdinalIgnoreCase);

        using var client = CrearCliente(TimeSpan.FromSeconds(15));
        using var response = await client.GetAsync($"https://api.openai.com/v1/vector_stores/{vectorStoreId}", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return false;

        if (!PerteneceALaBase(body, claveBase))
        {
            await appEvents.LogErrorAsync(ModuleName, "VectorStoreDeOtraBase",
                new InvalidOperationException($"El vector store {vectorStoreId} no pertenece a la base {claveBase}."),
                "El almacén de archivos configurado no corresponde a esta base: no se usó.",
                new { vectorStoreId, claveBase }, AppEventSeverity.Error, ct);
            return false;
        }

        VectorStoresVerificados[vectorStoreId] = claveBase;
        return true;
    }

    private async Task<IReadOnlyList<AsistenteFragmentoDto>> BuscarAsync(string vectorStoreId, string consulta, CancellationToken ct)
    {
        using var client = CrearCliente(TimeSpan.FromSeconds(15));
        using var response = await client.PostAsync(
            $"https://api.openai.com/v1/vector_stores/{vectorStoreId}/search",
            JsonContent(new { query = Recortar(consulta, 1000), max_num_results = FragmentosMaximos }), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI no respondió la búsqueda en archivos ({(int)response.StatusCode}). {ExtraerMensajeError(body)}");

        iaUso.RegistrarArchivos(IaUsoFunciones.ArchivosBusqueda, busquedas: 1, bytesAlmacenados: 0, referencia: vectorStoreId);
        return LeerResultadosBusqueda(body);
    }

    private async Task<(string Estado, string Error)> ConsultarEstadoArchivoAsync(string vectorStoreId, string fileId, CancellationToken ct)
    {
        using var client = CrearCliente(TimeSpan.FromSeconds(15));
        using var response = await client.GetAsync($"https://api.openai.com/v1/vector_stores/{vectorStoreId}/files/{fileId}", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode
            ? MapearEstado(body)
            : (AsistenteArchivoEstados.Procesando, string.Empty);
    }

    /// <summary>Una vez por día y por base registra los bytes del vector store para la medición.</summary>
    private async Task RegistrarAlmacenamientoDiarioAsync(SqlConnection cn, CancellationToken ct)
    {
        try
        {
            var hoy = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (await LeerConfigAsync(cn, ClaveUsoAlmacenamiento, ct) == hoy)
                return;

            var vectorStoreId = await LeerConfigAsync(cn, ClaveVectorStore, ct);
            if (string.IsNullOrWhiteSpace(vectorStoreId) || !ArchivosDisponibles)
                return;

            using var client = CrearCliente(TimeSpan.FromSeconds(15));
            using var response = await client.GetAsync($"https://api.openai.com/v1/vector_stores/{vectorStoreId}", ct);
            if (!response.IsSuccessStatusCode)
                return;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var bytes = doc.RootElement.TryGetProperty("usage_bytes", out var ub) && ub.ValueKind == JsonValueKind.Number ? ub.GetInt64() : 0;
            await GuardarConfigAsync(cn, ClaveUsoAlmacenamiento, hoy, ct);
            iaUso.RegistrarArchivos(IaUsoFunciones.ArchivosAlmacenamiento, busquedas: 0, bytesAlmacenados: bytes, referencia: vectorStoreId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La medición de almacenamiento nunca bloquea al asistente.
        }
    }

    private static async Task BorrarEnOpenAiAsync(HttpClient client, string? vectorStoreId, string fileId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(vectorStoreId))
            using (await client.DeleteAsync($"https://api.openai.com/v1/vector_stores/{vectorStoreId}/files/{fileId}", ct)) { }
        using (await client.DeleteAsync($"https://api.openai.com/v1/files/{fileId}", ct)) { }
    }

    private HttpClient CrearCliente(TimeSpan timeout)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = timeout;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        return client;
    }

    private static StringContent JsonContent(object payload)
        => new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static string LeerString(string json, string propiedad)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : throw new InvalidOperationException($"OpenAI respondió sin '{propiedad}'.");
    }

    private static string ExtraerMensajeError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m)
                ? m.GetString() ?? string.Empty
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    // ---------------------------------------------------------------- SQL

    private static async Task<List<AsistenteBloqueDto>> LeerBloquesAsync(SqlConnection cn, bool soloActivos, CancellationToken ct)
    {
        if (!await ExisteTablaAsync(cn, "CONV_ASISTENTE_BLOQUES", ct))
            return [];

        var sql = $"""
            SELECT IdBloque, Categoria, Titulo, Contenido, Activo, Orden
            FROM dbo.CONV_ASISTENTE_BLOQUES
            {(soloActivos ? "WHERE Activo = 1" : string.Empty)}
            ORDER BY Orden, IdBloque;
            """;
        var lista = new List<AsistenteBloqueDto>();
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            lista.Add(new AsistenteBloqueDto
            {
                IdBloque = rd.GetInt32(0),
                Categoria = rd.GetString(1),
                Titulo = rd.GetString(2),
                Contenido = rd.GetString(3),
                Activo = rd.GetBoolean(4),
                Orden = rd.GetInt32(5)
            });
        }
        return lista;
    }

    private static async Task<List<AsistenteArchivoDto>> LeerArchivosAsync(SqlConnection cn, CancellationToken ct)
    {
        const string sql = """
            SELECT IdArchivo, NombreArchivo, ISNULL(TipoContenido, ''), Tamano, Estado, ISNULL(UltimoError, ''),
                   FechaAlta, ISNULL(UsuarioAlta, ''), ISNULL(OpenAiFileId, '')
            FROM dbo.CONV_ASISTENTE_ARCHIVOS
            WHERE Baja = 0
            ORDER BY FechaAlta DESC;
            """;
        var lista = new List<AsistenteArchivoDto>();
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            lista.Add(new AsistenteArchivoDto
            {
                IdArchivo = rd.GetInt32(0),
                NombreArchivo = rd.GetString(1),
                TipoContenido = rd.GetString(2),
                Tamano = rd.GetInt64(3),
                Estado = rd.GetString(4),
                UltimoError = rd.GetString(5),
                FechaAlta = rd.GetDateTime(6),
                UsuarioAlta = rd.GetString(7),
                OpenAiFileId = rd.GetString(8)
            });
        }
        return lista;
    }

    private static async Task<bool> HayArchivosListosAsync(SqlConnection cn, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT TOP (1) 1 FROM dbo.CONV_ASISTENTE_ARCHIVOS WHERE Baja = 0 AND Estado = N'LISTO';", cn);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    private static async Task ActualizarEstadoAsync(SqlConnection cn, int idArchivo, string estado, string error, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            UPDATE dbo.CONV_ASISTENTE_ARCHIVOS SET Estado = @Estado, UltimoError = @Error, FechaModificacion = GETDATE()
            WHERE IdArchivo = @Id;
            """, cn);
        cmd.Parameters.AddWithValue("@Estado", estado);
        cmd.Parameters.AddWithValue("@Error", Recortar(error, 500));
        cmd.Parameters.AddWithValue("@Id", idArchivo);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string> LeerConfigAsync(SqlConnection cn, string clave, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT TOP (1) LTRIM(RTRIM(CAST(ISNULL(VALOR, '') AS nvarchar(200))))
            FROM dbo.TA_CONFIGURACION
            WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            """, cn);
        cmd.Parameters.AddWithValue("@Clave", clave);
        return (await cmd.ExecuteScalarAsync(ct) as string) ?? string.Empty;
    }

    private static async Task GuardarConfigAsync(SqlConnection cn, string clave, string valor, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            UPDATE dbo.TA_CONFIGURACION SET VALOR = @Valor WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.TA_CONFIGURACION (CLAVE, VALOR, GRUPO) VALUES (@Clave, @Valor, @Grupo);
            """, cn);
        cmd.Parameters.AddWithValue("@Clave", clave);
        cmd.Parameters.AddWithValue("@Valor", valor);
        cmd.Parameters.AddWithValue("@Grupo", ConfigGroup);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> ExisteTablaAsync(SqlConnection cn, string tabla, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT OBJECT_ID(@Nombre, N'U');", cn);
        cmd.Parameters.AddWithValue("@Nombre", $"dbo.{tabla}");
        return await cmd.ExecuteScalarAsync(ct) is not (null or DBNull);
    }

    private static async Task RequerirTablaAsync(SqlConnection cn, string tabla, CancellationToken ct)
    {
        if (!await ExisteTablaAsync(cn, tabla, ct))
            throw new InvalidOperationException("Falta aplicar la actualización de la base (bloques y archivos del asistente). Se aplica sola al volver a entrar a la base, o desde Actualizaciones.");
    }

    // ---------------------------------------------------------------- helpers

    private string Usuario => appUserSession.GetCurrentUserName("SYSTEM");

    internal static string NormalizarCategoria(string? categoria)
    {
        var c = (categoria ?? string.Empty).Trim().ToUpperInvariant();
        return AsistenteBloqueCategorias.Todas.Any(x => x.Clave == c) ? c : AsistenteBloqueCategorias.General;
    }

    private static string Recortar(string? valor, int max)
    {
        var texto = (valor ?? string.Empty).Trim();
        return texto.Length <= max ? texto : texto[..max];
    }

    private async Task<T> LoggedAsync<T>(string action, Func<Task<T>> operation, string userMessage, CancellationToken ct)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var incidentId = await appEvents.LogErrorAsync(ModuleName, action, ex, userMessage, null, AppEventSeverity.Error, ct);
            throw new InvalidOperationException($"{userMessage} Código: {incidentId}", ex);
        }
    }
}
