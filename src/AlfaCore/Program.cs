using AlfaCore.Components;
using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Repositories;
using AlfaCore.Services;
using System.Net;
using System.Diagnostics;
using System.Text.Json;

namespace AlfaCore;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        DotEnvLoader.LoadIfPresent(builder.Environment.ContentRootPath);
        var startupConnectionString = StartupConnectionResolver.Resolve(args, builder.Configuration, builder.Environment.ContentRootPath);

        if (!string.IsNullOrWhiteSpace(startupConnectionString))
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AlfaGestion"] = startupConnectionString
            });
        }

        var serverOptions = builder.Configuration.GetSection(ServidorWebOptions.SectionName).Get<ServidorWebOptions>() ?? new();

        builder.Host.UseWindowsService(options =>
        {
            options.ServiceName = "AlfaCore";
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            if (serverOptions.EscucharEnRed)
            {
                options.ListenAnyIP(serverOptions.Puerto);
            }
            else
            {
                options.ListenLocalhost(serverOptions.Puerto);
            }
        });

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddSingleton<ISessionService, SessionService>();
        builder.Services.AddScoped<IComprasDashboardService, ComprasDashboardService>();
        builder.Services.AddScoped<IInformesIaService, InformesIaService>();
        builder.Services.AddScoped<IConsultasService, ConsultasService>();
        builder.Services.AddScoped<ICostosService, CostosService>();
        builder.Services.AddScoped<IConversacionesService, ConversacionesService>();
        builder.Services.AddScoped<IConversacionesConfigService, ConversacionesConfigService>();
        builder.Services.AddScoped<IInterfacesService, InterfacesService>();
        builder.Services.AddScoped<IInterfacesConfigService, InterfacesConfigService>();
        builder.Services.AddScoped<IActualizacionesService, ActualizacionesService>();
        builder.Services.AddScoped<IUsuariosService, UsuariosService>();
        builder.Services.AddScoped<IUsuariosValidator, UsuariosValidator>();
        builder.Services.AddScoped<IContactosService, ContactosService>();
        builder.Services.AddScoped<IContactosValidator, ContactosValidator>();
        builder.Services.AddSingleton<AppUserSessionStore>();
        builder.Services.AddScoped<IAppUserSessionService, AppUserSessionService>();
        builder.Services.AddSingleton<UsuariosPasswordCodec>();
        builder.Services.AddScoped<IAuditoriaService, AuditoriaService>();
        builder.Services.AddScoped<IGestionDashboardService, GestionDashboardService>();
        builder.Services.AddScoped<IAppUiOperationService, AppUiOperationService>();
        builder.Services.AddSingleton<IAuxErrRepository, AuxErrRepository>();
        builder.Services.AddSingleton<IAppEventService, AppEventService>();
        builder.Services.AddSingleton<ConsultasExcelExporter>();
        builder.Services.AddSingleton<InformesIaHistoryStore>();
        builder.Services.AddSingleton<InformesIaResultStore>();
        builder.Services.AddScoped<FilterStateService>();
        builder.Services.AddScoped<GestionFilterStateService>();
        builder.Services.AddHttpClient();
        builder.Services.AddHttpContextAccessor();
        builder.Services.Configure<ServidorWebOptions>(builder.Configuration.GetSection(ServidorWebOptions.SectionName));
        builder.Services.Configure<DatosSqlOptions>(builder.Configuration.GetSection(DatosSqlOptions.SectionName));
        builder.Services.Configure<WhatsAppOptions>(builder.Configuration.GetSection(WhatsAppOptions.SectionName));
        builder.Services.AddHostedService<ServerStartupHostedService>();
        builder.Services.AddHostedService<DatabaseUpdatesHostedService>();
        builder.Services.AddHostedService<InterfacesCompraIaWorkerHostedService>();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseMiddleware<AppExceptionLoggingMiddleware>();
        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.MapGet("/api/costos/importaciones/{batchId:int}/descargar-archivo", async (
            int batchId,
            ICostosService costosSvc,
            CancellationToken ct) =>
        {
            var detail = await costosSvc.GetBatchDetailAsync(batchId, ct);
            if (detail is null) return Results.NotFound();

            var path = detail.Batch.SourceFilePath;
            if (!File.Exists(path)) return Results.NotFound("El archivo ya no existe en el servidor.");

            var ext = Path.GetExtension(path).ToLowerInvariant();
            var contentType = ext == ".xlsx"
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "text/plain";

            return Results.File(path, contentType, Path.GetFileName(path));
        });

        app.MapGet("/api/interfaces/adjuntos/{idAdjunto:long}", async (
            long idAdjunto,
            IInterfacesService interfacesSvc,
            IInterfacesConfigService interfacesConfigSvc,
            CancellationToken ct) =>
        {
            var file = await interfacesSvc.GetAttachmentForServeAsync(idAdjunto, ct);
            if (file is null) return Results.NotFound();

            var contentType = string.IsNullOrWhiteSpace(file.MimeType)
                ? "application/octet-stream"
                : file.MimeType;

            if (file.RutaCompleta.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
            {
                var settings = await interfacesConfigSvc.GetUploadSettingsAsync(ct);
#pragma warning disable SYSLIB0014
                var request = (FtpWebRequest)WebRequest.Create(file.RutaCompleta);
                request.Method = WebRequestMethods.Ftp.DownloadFile;
                request.Credentials = new NetworkCredential(settings.FtpUsuario, settings.FtpClave);
                request.UseBinary = true;
                request.UsePassive = settings.FtpModoPasivo;
                request.KeepAlive = false;

                using var response = (FtpWebResponse)await request.GetResponseAsync();
                await using var responseStream = response.GetResponseStream();
                if (responseStream is null)
                    return Results.NotFound("No se pudo abrir el archivo remoto.");

                await using var ms = new MemoryStream();
                await responseStream.CopyToAsync(ms, ct);
#pragma warning restore SYSLIB0014
                return Results.File(ms.ToArray(), contentType, file.NombreArchivo);
            }

            if (!File.Exists(file.RutaCompleta)) return Results.NotFound("El archivo ya no existe en el servidor.");
            return Results.File(file.RutaCompleta, contentType, file.NombreArchivo);
        });

        app.MapGet("/api/usuarios/{nombre}/foto", async (
            string nombre,
            IUsuariosService usuariosSvc,
            CancellationToken ct) =>
        {
            var photo = await usuariosSvc.GetPhotoForServeAsync(nombre, ct);
            if (photo is null || !File.Exists(photo.RutaCompleta))
                return Results.NotFound();

            return Results.File(photo.RutaCompleta, photo.MimeType, photo.NombreArchivo);
        });

        app.MapGet("/consultas/{id:int}/descargar-excel", async (
            int id,
            HttpRequest request,
            IConsultasService svc,
            ConsultasExcelExporter exporter,
            CancellationToken ct) =>
        {
            var consulta = await svc.GetConsultaAsync(id, ct);
            if (consulta is null) return Results.NotFound();

            var valores = new List<string>();
            for (int i = 0; request.Query.ContainsKey($"p{i}"); i++)
                valores.Add(request.Query[$"p{i}"].ToString());

            var resultado = await svc.EjecutarAsync(new EjecutarConsultaRequest
            {
                ConsultaId = id,
                ValoresParametros = valores,
                MaxFilas = 100_000
            }, ct);

            if (!resultado.Exitoso)
                return Results.BadRequest(resultado.MensajeError);

            var agruparPor = request.Query["agruparPor"].ToString();
            var columnasAgrupadas = request.Query["ga"]
                .Select(v => v ?? string.Empty)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();
            var filasAgrupadas = request.Query["gf"].ToArray()
                .Select(f => (f ?? string.Empty).Split('\u001F'))
                .ToList();

            var exportarAgrupado =
                !string.IsNullOrWhiteSpace(agruparPor) &&
                columnasAgrupadas.Length > 0 &&
                filasAgrupadas.Count > 0;

            var bytes = exportarAgrupado
                ? exporter.ExportarAgrupado(
                    consulta,
                    resultado.EjecutadoEn,
                    columnasAgrupadas,
                    filasAgrupadas,
                    $"Agrupado por {agruparPor}")
                : exporter.Exportar(consulta, resultado);
            var filename = ConsultasExcelExporter.NombreArchivo(consulta);
            return Results.File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                filename);
        });

        app.MapGet("/api/conversaciones", async (
            string? modo,
            string? search,
            string? idTecnicoActual,
            string? codigoEstado,
            int? limit,
            int? offset,
            IConversacionesService svc,
            CancellationToken ct) =>
        {
            var filters = new ConversacionesInboxFilters
            {
                Modo = modo ?? "todas",
                Search = search ?? string.Empty,
                IdTecnicoActual = idTecnicoActual,
                CodigoEstado = codigoEstado,
                Limit = limit ?? 50,
                Offset = offset ?? 0
            };

            return Results.Ok(await svc.GetInboxAsync(filters, ct));
        });

        app.MapGet("/api/conversaciones/{id:long}", async (
            long id,
            IConversacionesService svc,
            CancellationToken ct) =>
        {
            var item = await svc.GetConversationAsync(id, ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        app.MapGet("/api/conversaciones/{id:long}/mensajes", async (
            long id,
            IConversacionesService svc,
            CancellationToken ct) =>
        {
            var items = await svc.GetMessagesAsync(id, ct);
            return Results.Ok(items);
        });

        app.MapPost("/api/conversaciones/{id:long}/mensajes", async (
            long id,
            ConversacionSendMessageRequest request,
            IConversacionesService svc,
            CancellationToken ct) =>
        {
            request.IdConversacion = id;
            var result = await svc.SendMessageAsync(request, ct);
            return Results.Ok(result);
        });

        app.MapPost("/api/conversaciones/{id:long}/notas", async (
            long id,
            ConversacionNotaInternaRequest request,
            IConversacionesService svc,
            CancellationToken ct) =>
        {
            request.IdConversacion = id;
            var noteId = await svc.AddInternalNoteAsync(request, ct);
            return Results.Ok(new { IdMensaje = noteId });
        });

        app.MapPost("/api/conversaciones/{id:long}/asignacion", async (
            long id,
            ConversacionAsignacionRequest request,
            IConversacionesService svc,
            CancellationToken ct) =>
        {
            request.IdConversacion = id;
            await svc.AssignConversationAsync(request, ct);
            return Results.Ok();
        });

        app.MapPost("/api/conversaciones/{id:long}/estado", async (
            long id,
            ConversacionEstadoRequest request,
            IConversacionesService svc,
            CancellationToken ct) =>
        {
            request.IdConversacion = id;
            await svc.ChangeStatusAsync(request, ct);
            return Results.Ok();
        });

        app.MapGet("/api/conversaciones/whatsapp/webhook", async (
            HttpRequest request,
            IConversacionesConfigService configService,
            CancellationToken ct) =>
        {
            var options = await configService.GetWhatsAppConfigAsync(ct);
            var mode = request.Query["hub.mode"].ToString();
            var verifyToken = request.Query["hub.verify_token"].ToString();
            var challenge = request.Query["hub.challenge"].ToString();

            if (!string.Equals(mode, "subscribe", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest("Modo de verificación inválido.");

            if (!options.IsConfiguredForVerify)
                return Results.Problem("WhatsApp VerifyToken no está configurado.", statusCode: StatusCodes.Status500InternalServerError);

            return string.Equals(verifyToken, options.VerifyToken, StringComparison.Ordinal)
                ? Results.Text(challenge)
                : Results.Unauthorized();
        });

        app.MapPost("/api/conversaciones/whatsapp/webhook", async (
            HttpRequest request,
            IConversacionesService svc,
            CancellationToken ct) =>
        {
            using var payload = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
            var headers = request.Headers.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);

            var result = await svc.RegisterIncomingWebhookAsync(new ConversacionWebhookRequest
            {
                Payload = payload,
                Headers = headers
            }, ct);

            return Results.Ok(result);
        });

        app.MapPost("/api/conversaciones/{id:long}/adjuntos", async (
            long id,
            HttpRequest request,
            IConversacionesService svc,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Se esperaba multipart/form-data.");

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("archivo");
            if (file is null || file.Length == 0)
                return Results.BadRequest("No se recibió ningún archivo.");

            var tipoArchivo = form["tipoArchivo"].ToString();
            var idTecnico = form["idTecnico"].ToString();

            var uploadRequest = new ConversacionUploadAdjuntoRequest
            {
                IdConversacion = id,
                NombreArchivo = file.FileName,
                MimeType = file.ContentType,
                TipoArchivo = string.IsNullOrWhiteSpace(tipoArchivo)
                    ? InferTipoArchivo(file.ContentType, file.FileName)
                    : tipoArchivo,
                Contenido = file.OpenReadStream(),
                TamanoBytes = file.Length,
                IdTecnicoAutor = string.IsNullOrWhiteSpace(idTecnico) ? null : idTecnico
            };

            var result = await svc.UploadAttachmentAsync(uploadRequest, ct);
            return Results.Ok(result);
        });

        app.MapGet("/api/conversaciones/adjuntos/{idAdjunto:long}", async (
            long idAdjunto,
            HttpRequest request,
            IConversacionesService svc,
            CancellationToken ct) =>
        {
            var adjunto = await svc.GetAttachmentForServeAsync(idAdjunto, ct);
            if (adjunto is null || !File.Exists(adjunto.RutaLocal))
                return Results.NotFound();

            var mime = NormalizeAttachmentMime(adjunto.MimeType, adjunto.NombreArchivo);
            var download = string.Equals(request.Query["download"].ToString(), "1", StringComparison.OrdinalIgnoreCase);
            return Results.File(
                adjunto.RutaLocal,
                contentType: mime,
                fileDownloadName: download ? adjunto.NombreArchivo : null,
                enableRangeProcessing: true);
        });

        try
        {
            app.Run();
        }
        catch (IOException ex)
        {
            WriteStartupError(
                $"No se pudo iniciar AlfaCore en el puerto {serverOptions.Puerto}. Verificá si el puerto está ocupado o bloqueado.",
                ex);
            throw;
        }
    }

    private static string InferTipoArchivo(string mimeType, string fileName)
    {
        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return "IMAGE";
        if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return "AUDIO";
        if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return "VIDEO";
        return "DOCUMENT";
    }

    private static string NormalizeAttachmentMime(string? mimeType, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(mimeType))
            return mimeType.Trim();

        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ogg" or ".oga" => "audio/ogg",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".m4a" or ".mp4" => "audio/mp4",
            ".webm" => "audio/webm",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }

    private static void WriteStartupError(string message, Exception exception)
    {
        var fullMessage = $"{message}{Environment.NewLine}{exception}";

        try
        {
            Console.Error.WriteLine(fullMessage);
        }
        catch
        {
            // Avoid masking the original startup failure if stderr is unavailable.
        }

        try
        {
            Trace.TraceError(fullMessage);
        }
        catch
        {
            // Best-effort diagnostic fallback only.
        }
    }
}
