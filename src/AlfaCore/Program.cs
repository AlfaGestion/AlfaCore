using AlfaCore.Components;
using AlfaCore.Configuration;
using AlfaCore.Models;
using AlfaCore.Repositories;
using AlfaCore.Services;
using System.Net;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlfaCore;

public class Program
{
    public static void Main(string[] args)
    {
        // Tiene que cargarse ANTES de CreateBuilder: el proveedor de variables de entorno
        // de ASP.NET Core lee el entorno del proceso durante CreateBuilder. Si .env se carga
        // despues, IConfiguration nunca ve esos valores (Environment.GetEnvironmentVariable
        // directo si los ve, por eso a veces parecia que "andaba").
        DotEnvLoader.LoadIfPresent(AppContext.BaseDirectory);

        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var webRootCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../wwwroot")),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
        };

        var projectRootCandidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..")),
            Directory.GetCurrentDirectory(),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."))
        };

        var selectedWebRoot = webRootCandidates.FirstOrDefault(Directory.Exists);
        var builder = selectedWebRoot is null
            ? WebApplication.CreateBuilder(args)
            : WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                WebRootPath = selectedWebRoot
            });

        var startupConnectionString = StartupConnectionResolver.Resolve(args, builder.Configuration);

        string? ResolveStaticAsset(string relativePath)
        {
            foreach (var candidate in webRootCandidates)
            {
                var fullPath = Path.Combine(candidate, relativePath);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            foreach (var candidate in projectRootCandidates)
            {
                var scopedCssCandidates = new[]
                {
                    Path.Combine(candidate, "obj", "Debug", "net8.0", "scopedcss", "bundle", relativePath),
                    Path.Combine(candidate, "obj", "Release", "net8.0", "scopedcss", "bundle", relativePath),
                    Path.Combine(candidate, "bin", "Debug", "net8.0", "scopedcss", "bundle", relativePath),
                    Path.Combine(candidate, "bin", "Release", "net8.0", "scopedcss", "bundle", relativePath)
                };

                foreach (var scopedCssPath in scopedCssCandidates)
                {
                    if (File.Exists(scopedCssPath))
                        return scopedCssPath;
                }
            }

            return null;
        }

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
        builder.Services.AddScoped<ISessionService, SessionService>();
        builder.Services.AddSingleton<IAppModeService, AppModeService>();
        builder.Services.AddScoped<IRouteContextService, RouteContextService>();
        builder.Services.AddScoped<IPasswordVerifier, PlainTextPasswordVerifier>();
        builder.Services.AddScoped<ICentralClientesService, CentralClientesService>();
        builder.Services.AddScoped<ICentralBasesService, CentralBasesService>();
        builder.Services.AddScoped<ICentralBackupControlService, CentralBackupControlService>();
        builder.Services.AddScoped<ICentralUsersService, CentralUsersService>();
        builder.Services.AddScoped<ICentralAdminService, CentralAdminService>();
        builder.Services.AddScoped<ICentralAuthService, CentralAuthService>();
        builder.Services.AddScoped<IRecaptchaValidationService, RecaptchaValidationService>();
        builder.Services.AddScoped<ICentralProvisioningService, CentralProvisioningService>();
        builder.Services.AddScoped<ICentralRegistrationService, CentralRegistrationService>();
        builder.Services.AddScoped<IConexionClienteService, ConexionClienteService>();
        builder.Services.AddScoped<ILegacyBaseUserSessionService, LegacyBaseUserSessionService>();
        builder.Services.AddScoped<IComprasDashboardService, ComprasDashboardService>();
        builder.Services.AddScoped<IReporteComprasService, ReporteComprasService>();
        builder.Services.AddScoped<IInformesIaService, InformesIaService>();
        builder.Services.AddScoped<IInformesService, InformesService>();
        builder.Services.AddScoped<INovedadesService, NovedadesService>();
        builder.Services.AddScoped<IConsultasService, ConsultasService>();
        builder.Services.AddScoped<ICostosService, CostosService>();
        builder.Services.AddScoped<IConversacionesService, ConversacionesService>();
        builder.Services.AddScoped<IConversacionesConfigService, ConversacionesConfigService>();
        builder.Services.AddScoped<IConversacionesInformesService, ConversacionesInformesService>();
        builder.Services.AddScoped<ITablasReferenciaService, TablasReferenciaService>();
        builder.Services.AddScoped<IAnyDeskLocalSettingsService, AnyDeskLocalSettingsService>();
        builder.Services.AddScoped<INotificacionesPushService, NotificacionesPushService>();
        builder.Services.AddScoped<ICalendarioService, CalendarioService>();
        builder.Services.AddScoped<IReunionesPublicasService, ReunionesPublicasService>();
        builder.Services.AddScoped<ICrmService, CrmService>();
        builder.Services.AddScoped<ICrmCotizacionService, CrmCotizacionService>();
        builder.Services.AddScoped<ITicketsService, TicketsService>();
        builder.Services.AddScoped<IPartesHorasService, PartesHorasService>();
        builder.Services.AddScoped<ITareasService, TareasService>();
        builder.Services.AddScoped<IInterfacesService, InterfacesService>();
        builder.Services.AddScoped<IInterfacesConfigService, InterfacesConfigService>();
        builder.Services.AddScoped<IInterfacesCatalogosService, InterfacesCatalogosService>();
        builder.Services.AddSingleton<IArticuloImagenFtpService, ArticuloImagenFtpService>();
        builder.Services.AddScoped<ICatalogoPublicoPdfService, CatalogoPublicoPdfService>();
        builder.Services.AddSingleton<InterfacesCompraIaWorkerState>();
        builder.Services.AddSingleton<DatabaseUpdatesRuntimeState>();
        builder.Services.AddScoped<IActualizacionesService, ActualizacionesService>();
        builder.Services.AddScoped<IPermissionService, PermissionService>();
        builder.Services.AddScoped<IMenuService, MenuService>();
        builder.Services.AddScoped<IAutorizacionTareasService, AutorizacionTareasService>();
        builder.Services.AddScoped<IFavoritesService, FavoritesService>();
        builder.Services.AddScoped<IRecentService, RecentService>();
        builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();
        builder.Services.AddScoped<IUsuariosService, UsuariosService>();
        builder.Services.AddScoped<IUsuariosValidator, UsuariosValidator>();
        builder.Services.AddScoped<ITecnicosService, TecnicosService>();
        builder.Services.AddScoped<ITecnicosValidator, TecnicosValidator>();
        builder.Services.AddScoped<IContactosService, ContactosService>();
        builder.Services.AddScoped<IContactosValidator, ContactosValidator>();
        builder.Services.AddScoped<ICuentasComercialesService, CuentasComercialesService>();
        builder.Services.AddScoped<ICuentasComercialesValidator, CuentasComercialesValidator>();
        builder.Services.AddScoped<ICargaViajesService, CargaViajesService>();
        builder.Services.AddScoped<ICargaViajesValidator, CargaViajesValidator>();
        builder.Services.AddScoped<IViajePreviewStateService, ViajePreviewStateService>();
        builder.Services.AddScoped<IComprobanteViewerService, ComprobanteViewerService>();
        builder.Services.AddSingleton<AppUserSessionStore>();
        builder.Services.AddScoped<IAppUserSessionService, AppUserSessionService>();
        builder.Services.AddSingleton<CatalogosClienteSessionStore>();
        builder.Services.AddScoped<ICatalogosClienteSessionService, CatalogosClienteSessionService>();
        builder.Services.AddSingleton<UsuariosPasswordCodec>();
        builder.Services.AddSingleton<Vb6BridgeTicketStore>();
        builder.Services.AddScoped<IVb6BridgeService, Vb6BridgeService>();
        builder.Services.AddScoped<IAuditoriaService, AuditoriaService>();
        builder.Services.AddScoped<IGestionDashboardService, GestionDashboardService>();
        builder.Services.AddScoped<IPuntoVentaService, PuntoVentaService>();
        builder.Services.AddScoped<IPuntoVentaCartStateService, PuntoVentaCartStateService>();
        builder.Services.AddScoped<IPuntoVentaConfigService, PuntoVentaConfigService>();
        builder.Services.AddScoped<IPuntoVentaConfigValidator, PuntoVentaConfigValidator>();
        builder.Services.AddScoped<IPuntoVentaPedidoService, PuntoVentaPedidoService>();
        builder.Services.AddScoped<IPuntoVentaPedidoValidator, PuntoVentaPedidoValidator>();
        builder.Services.AddScoped<IPuntoVentaTurnoService, PuntoVentaTurnoService>();
        builder.Services.AddScoped<IPuntoVentaTurnoValidator, PuntoVentaTurnoValidator>();
        builder.Services.AddScoped<IAppUiOperationService, AppUiOperationService>();
        builder.Services.AddScoped<IAppUiDialogService, AppUiDialogService>();
        builder.Services.AddScoped<IFloatingWindowService, FloatingWindowService>();
        builder.Services.AddScoped<IPageHeaderService, PageHeaderService>();
        builder.Services.AddScoped<IPageHeaderNavigationService, PageHeaderNavigationService>();
        builder.Services.AddScoped<IAuxErrRepository, AuxErrRepository>();
        builder.Services.AddScoped<IAppEventService, AppEventService>();
        builder.Services.AddSingleton<ConsultasExcelExporter>();
        builder.Services.AddSingleton<AuditoriaExcelExporter>();
        builder.Services.AddSingleton<ReporteComprasExcelExporter>();
        builder.Services.AddSingleton<ArticulosComprasExcelExporter>();
        builder.Services.AddSingleton<VentasRubrosExcelExporter>();
        builder.Services.AddSingleton<CargaViajesLiquidacionExcelExporter>();
        builder.Services.AddSingleton<CargaViajesTarifasExcelExporter>();
        builder.Services.AddSingleton<CargaViajesViajesExcelExporter>();
        builder.Services.AddSingleton<CargaViajesReporteExcelExporter>();
        builder.Services.AddSingleton<ConversacionesInformeExcelExporter>();
        builder.Services.AddSingleton<InformesIaHistoryStore>();
        builder.Services.AddSingleton<InformesIaResultStore>();
        builder.Services.AddScoped<FilterStateService>();
        builder.Services.AddScoped<GestionFilterStateService>();
        builder.Services.AddHttpClient();
        builder.Services.AddHttpContextAccessor();
        builder.Services.Configure<ServidorWebOptions>(builder.Configuration.GetSection(ServidorWebOptions.SectionName));
        builder.Services.Configure<DatosSqlOptions>(builder.Configuration.GetSection(DatosSqlOptions.SectionName));
        builder.Services.Configure<WhatsAppOptions>(builder.Configuration.GetSection(WhatsAppOptions.SectionName));
        builder.Services.Configure<PushNotificationsOptions>(builder.Configuration.GetSection(PushNotificationsOptions.SectionName));
        builder.Services.AddScoped<IAlfaKnowledgeSuggestionService, AlfaKnowledgeSuggestionService>();
        builder.Services.AddScoped<IConversacionAnalisisService, ConversacionAnalisisService>();
        builder.Services.AddScoped<IConversacionAsistenteService, ConversacionAsistenteService>();
        builder.Services.AddHostedService<ServerStartupHostedService>();
        builder.Services.AddHostedService<DatabaseUpdatesHostedService>();
        builder.Services.AddHostedService<InterfacesCompraIaWorkerHostedService>();
        builder.Services.AddHostedService<ModuloPruebaRecordatorioHostedService>();
        builder.Services.AddHostedService<ConversacionesAutoCierreHostedService>();
        builder.Services.AddHostedService<ConversacionesProgramadosHostedService>();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }

        app.Use(async (context, next) =>
        {
            var host = context.Request.Host.Host;
            var isTemporaryInstagramTunnel = host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase);
            var isInstagramWebhook = context.Request.Path.StartsWithSegments(
                "/api/conversaciones/instagram/webhook",
                StringComparison.OrdinalIgnoreCase);

            if (isTemporaryInstagramTunnel && !isInstagramWebhook)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next();
        });

        app.UseMiddleware<AppExceptionLoggingMiddleware>();
        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapGet("/app.css", () =>
            ResolveStaticAsset("app.css") is { } file
                ? Results.File(file, "text/css; charset=utf-8")
                : Results.NotFound());

        app.MapGet("/theme-overrides.css", () =>
            ResolveStaticAsset("theme-overrides.css") is { } file
                ? Results.File(file, "text/css; charset=utf-8")
                : Results.NotFound());

        app.MapGet("/AlfaCore.styles.css", () =>
            ResolveStaticAsset("AlfaCore.styles.css") is { } file
                ? Results.File(file, "text/css; charset=utf-8")
                : Results.NotFound());

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

        app.MapGet("/api/interfaces/adjuntos/{idBase:int}/{idAdjunto:long}", async (
            int idBase,
            long idAdjunto,
            HttpRequest httpRequest,
            IInterfacesService interfacesSvc,
            IInterfacesConfigService interfacesConfigSvc,
            IAppUserSessionService appUserSession,
            ISessionService sessionService,
            ICentralBasesService basesService,
            CancellationToken ct) =>
        {
            // Este endpoint corre fuera del circuito Blazor: no arrastra ni el usuario ni la base activa.
            // Restauramos el usuario desde el token y fijamos/validamos la base pedida antes de leer.
            var token = httpRequest.Query["token"].ToString();
            if (!string.IsNullOrWhiteSpace(token))
                appUserSession.TryRestoreFromToken(token);

            if (!await TryActivateInterfacesBaseAsync(idBase, sessionService, basesService, appUserSession, ct))
                return Results.NotFound();

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

        app.MapGet("/cotizacion/{idbase:int}/{token}", async (
            int idbase,
            string token,
            ICrmCotizacionService cotizSvc,
            CancellationToken ct) =>
        {
            var html = await cotizSvc.RenderPublicHtmlAsync(idbase, token, ct);
            return html is null
                ? Results.NotFound("La cotización no existe o el enlace expiró.")
                : Results.Content(html, "text/html; charset=utf-8");
        }).AllowAnonymous();

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

        app.MapGet("/api/catalogos/logo-publico/{idweb}", async (
            string idweb,
            int? idbase,
            IInterfacesCatalogosService catalogosSvc,
            ICentralBasesService centralBasesSvc,
            ISessionService sessionSvc,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var resolvedBaseId = idbase ?? ResolveSqlSessionBaseId(httpContext.Request.Cookies["AlfaCore.SqlSessionId"]);
            if (resolvedBaseId is > 0)
            {
                var routeBase = await centralBasesSvc.GetByIdAsync(resolvedBaseId.Value, ct);
                if (routeBase is not null)
                {
                    sessionSvc.SetWebhookOverride(new SessionDto
                    {
                        Id = Guid.Parse($"00000000-0000-0000-0000-{routeBase.IdBase:000000000000}"),
                        BaseId = routeBase.IdBase,
                        Nombre = routeBase.Nombre,
                        Servidor = routeBase.DbServer,
                        BaseDatos = routeBase.DbName,
                        Usuario = routeBase.DbUser,
                        Password = routeBase.DbPassword,
                        TrustServerCertificate = true,
                        Activa = true
                    });
                }
            }

            var logo = await catalogosSvc.GetPublicLogoForServeAsync(idweb, ct);
            if (logo is null || !File.Exists(logo.RutaCompleta))
                return Results.NotFound();

            httpContext.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
            httpContext.Response.Headers.Pragma = "no-cache";
            return Results.File(logo.RutaCompleta, logo.MimeType, logo.NombreArchivo);
        }).AllowAnonymous();

        app.MapGet("/api/catalogos/imagen-articulo/{idCliente}/{idArticulo}", async (
            string idCliente,
            string idArticulo,
            int? idbase,
            bool? thumb,
            IArticuloImagenFtpService imagenSvc,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var imagen = await imagenSvc.ObtenerImagenAsync(idCliente, idbase, idArticulo, thumb == true, ct);
            if (imagen is null || !File.Exists(imagen.RutaCompleta))
                return Results.NotFound();

            httpContext.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(imagen.RutaCompleta, imagen.MimeType);
        }).AllowAnonymous();

        app.MapGet("/api/catalogos/{idInsert:int}/pdf", async (
            int idInsert,
            string? idweb,
            int? idbase,
            string? vista,
            IInterfacesCatalogosService catalogosSvc,
            IPuntoVentaService puntoVentaSvc,
            ICatalogoPublicoPdfService pdfSvc,
            ICentralBasesService centralBasesSvc,
            ISessionService sessionSvc,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var resolvedBaseId = idbase ?? ResolveSqlSessionBaseId(httpContext.Request.Cookies["AlfaCore.SqlSessionId"]);
            if (resolvedBaseId is > 0)
            {
                var routeBase = await centralBasesSvc.GetByIdAsync(resolvedBaseId.Value, ct);
                if (routeBase is not null)
                {
                    sessionSvc.SetWebhookOverride(new SessionDto
                    {
                        Id = Guid.Parse($"00000000-0000-0000-0000-{routeBase.IdBase:000000000000}"),
                        BaseId = routeBase.IdBase,
                        Nombre = routeBase.Nombre,
                        Servidor = routeBase.DbServer,
                        BaseDatos = routeBase.DbName,
                        Usuario = routeBase.DbUser,
                        Password = routeBase.DbPassword,
                        TrustServerCertificate = true,
                        Activa = true
                    });
                }
            }

            var catalogo = await catalogosSvc.GetCatalogoPublicoAsync(idInsert, ct);
            if (catalogo is null)
                return Results.NotFound();

            var branding = await catalogosSvc.GetPublicIdentityAsync(idweb, ct);
            var settings = await puntoVentaSvc.GetSettingsAsync(ct);

            var esCuadricula = string.Equals(vista, "cuadricula", StringComparison.OrdinalIgnoreCase);
            var pdfBytes = await pdfSvc.GenerarPdfAsync(catalogo, branding, idweb, settings.FtpCodigoCta, resolvedBaseId, esCuadricula, ct);

            return Results.File(pdfBytes, "application/pdf", $"catalogo-{idInsert}.pdf");
        }).AllowAnonymous();

        static int? ResolveSqlSessionBaseId(string? sessionIdCookie)
        {
            if (string.IsNullOrWhiteSpace(sessionIdCookie))
                return null;

            if (!Guid.TryParse(sessionIdCookie, out var sessionId))
                return null;

            var bytes = sessionId.ToByteArray();
            var baseId = BitConverter.ToInt32(bytes, 0);
            return baseId > 0 ? baseId : null;
        }

        app.MapGet("/api/punto-venta/articulos/{idArticulo}/imagen", async (
            string idArticulo,
            IPuntoVentaService puntoVentaSvc,
            CancellationToken ct) =>
        {
            var image = await puntoVentaSvc.GetArticleImageForServeAsync(idArticulo, ct);
            if (image is null || !File.Exists(image.RutaCompleta))
                return Results.NotFound();

            return Results.File(image.RutaCompleta, image.MimeType, image.NombreArchivo);
        });

        app.MapGet("/api/comprobantes/{tc}/{idComprobante}", async (
            string tc,
            string idComprobante,
            int? idComplemento,
            IComprobanteViewerService comprobanteViewerSvc,
            CancellationToken ct) =>
        {
            var dto = await comprobanteViewerSvc.GetAsync(tc, idComprobante, idComplemento ?? 0, ct);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        app.MapGet("/api/comprobantes/{tc}/{idComprobante}/documentos", async (
            string tc,
            string idComprobante,
            int? idComplemento,
            string documento,
            IComprobanteViewerService comprobanteViewerSvc,
            CancellationToken ct) =>
        {
            var file = await comprobanteViewerSvc.GetDocumentoArchivoAsync(tc, idComprobante, idComplemento ?? 0, documento, ct);
            if (file is null) return Results.NotFound();
            return Results.File(file.RutaCompleta, file.MimeType, file.NombreArchivo);
        });

        app.MapPost("/api/vb6/auth-ticket", async (
            HttpRequest request,
            IVb6BridgeService vb6BridgeSvc,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Se esperaba application/x-www-form-urlencoded.");

            var form = await request.ReadFormAsync(ct);
            var vb6Request = new Vb6AuthTicketRequest
            {
                Servidor = form["servidor"].ToString(),
                BaseDatos = form["baseDatos"].ToString(),
                UsuarioSql = form["usuarioSql"].ToString(),
                PasswordSql = form["passwordSql"].ToString(),
                UsuarioSistema = form["usuarioSistema"].ToString(),
                PasswordSistema = form["passwordSistema"].ToString(),
                Modulo = form["modulo"].ToString(),
                NombreSesion = string.IsNullOrWhiteSpace(form["nombreSesion"]) ? null : form["nombreSesion"].ToString()
            };

            try
            {
                var ticket = await vb6BridgeSvc.CreateTicketAsync(vb6Request, ct);
                return Results.Text(ticket, "text/plain; charset=utf-8");
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        app.MapPost("/api/vb6/backup-status", async (
            HttpRequest request,
            ICentralBackupControlService backupControlSvc,
            IConfiguration config,
            CancellationToken ct) =>
        {
            var apiKeyConfigurada = config["BackupStatus:ApiKey"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(apiKeyConfigurada))
                return Results.Problem("El servidor no tiene configurada BackupStatus:ApiKey.", statusCode: StatusCodes.Status500InternalServerError);

            var apiKeyRecibida = request.Headers["X-Api-Key"].ToString();
            if (!string.Equals(apiKeyRecibida, apiKeyConfigurada, StringComparison.Ordinal))
                return Results.Unauthorized();

            if (!request.HasFormContentType)
                return Results.BadRequest("Se esperaba application/x-www-form-urlencoded.");

            var form = await request.ReadFormAsync(ct);

            if (!DateTime.TryParse(form["fechaHoraBackup"].ToString(), out var fechaHoraBackup))
                fechaHoraBackup = DateTime.Now;

            var backupRequest = new BackupStatusRequest
            {
                IdCliente = form["idCliente"].ToString(),
                DbServidor = form["dbServidor"].ToString(),
                DbNombre = form["dbNombre"].ToString(),
                TipoBackup = form["tipoBackup"].ToString(),
                Resultado = form["resultado"].ToString(),
                Mensaje = NullIfEmpty(form["mensaje"]),
                HostCliente = NullIfEmpty(form["hostCliente"]),
                UsuarioSql = NullIfEmpty(form["usuarioSql"]),
                InstanciaSql = NullIfEmpty(form["instanciaSql"]),
                VersionSql = NullIfEmpty(form["versionSql"]),
                SistemaOperativo = NullIfEmpty(form["sistemaOperativo"]),
                TamanioBaseMB = ParseDecimalOrNull(form["tamanioBaseMB"]),
                EspacioLibreDiscoGB = ParseDecimalOrNull(form["espacioLibreDiscoGB"]),
                EspacioTotalDiscoGB = ParseDecimalOrNull(form["espacioTotalDiscoGB"]),
                EspacioLibreDiscoBckGB = ParseDecimalOrNull(form["espacioLibreDiscoBckGB"]),
                EspacioTotalDiscoBckGB = ParseDecimalOrNull(form["espacioTotalDiscoBckGB"]),
                FechaHoraBackup = fechaHoraBackup
            };

            try
            {
                var idControl = await backupControlSvc.RegistrarAsync(backupRequest, ct);
                return Results.Text(idControl.ToString(), "text/plain; charset=utf-8");
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        app.MapGet("/api/vb6/cliente-por-licencia", async (
            HttpRequest request,
            ICentralClientesService clientesSvc,
            IConfiguration config,
            CancellationToken ct) =>
        {
            var apiKeyConfigurada = config["BackupStatus:ApiKey"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(apiKeyConfigurada))
                return Results.Problem("El servidor no tiene configurada BackupStatus:ApiKey.", statusCode: StatusCodes.Status500InternalServerError);

            var apiKeyRecibida = request.Headers["X-Api-Key"].ToString();
            if (!string.Equals(apiKeyRecibida, apiKeyConfigurada, StringComparison.Ordinal))
                return Results.Unauthorized();

            var licencia = request.Query["licenciaPrincipal"].ToString();
            if (string.IsNullOrWhiteSpace(licencia))
                return Results.BadRequest("Falta licenciaPrincipal.");

            var cliente = await clientesSvc.GetByLicenciaPrincipalAsync(licencia, ct);
            if (cliente is null)
                return Results.NotFound();

            return Results.Text(cliente.IdCliente, "text/plain; charset=utf-8");
        });

        app.MapGet("/vb6/consume", async (
            HttpRequest request,
            IVb6BridgeService vb6BridgeSvc,
            CancellationToken ct) =>
        {
            var ticket = request.Query["t"].ToString();
            if (string.IsNullOrWhiteSpace(ticket))
                return Results.BadRequest("Falta el ticket.");

            try
            {
                var result = await vb6BridgeSvc.ConsumeTicketAsync(ticket, ct);
                return Results.Content(BuildVb6ConsumeHtml(result), "text/html; charset=utf-8");
            }
            catch (InvalidOperationException ex)
            {
                return Results.Content(BuildVb6ErrorHtml(ex.Message), "text/html; charset=utf-8");
            }
            catch (Exception ex)
            {
                return Results.Content(BuildVb6ErrorHtml(ex.Message), "text/html; charset=utf-8");
            }
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

        var descargarInformeConversacionesExcel = async (
            int idInforme,
            IConversacionesInformesService svc,
            ConversacionesInformeExcelExporter exporter,
            CancellationToken ct) =>
        {
            var informe = await svc.GetAsync(idInforme, ct);
            if (informe is null) return Results.NotFound();
            var bytes = exporter.Exportar(informe);
            return Results.File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ConversacionesInformeExcelExporter.NombreArchivo(informe));
        };
        app.MapGet("/conversaciones/informes/{idInforme:int}/excel", descargarInformeConversacionesExcel);
        app.MapGet("/{idweb}/{idbase:int}/conversaciones/informes/{idInforme:int}/excel", descargarInformeConversacionesExcel);

        app.MapGet("/auditoria/usuarios/descargar-excel", async (
            HttpRequest request,
            IAuditoriaService auditoriaSvc,
            AuditoriaExcelExporter exporter,
            CancellationToken ct) =>
        {
            static DateTime? ParseDate(string? value)
                => DateTime.TryParse(value, out var parsed) ? parsed : null;

            static int? ParseNullableInt(string? value)
                => int.TryParse(value, out var parsed) ? parsed : null;

            var pagina = 1;
            var pageSize = 200;
            var allItems = new List<AuditoriaUsuarioRowDto>();
            AuditoriaUsuarioResultDto? lastResult = null;

            var filterBase = new AuditoriaUsuarioFilterDto
            {
                Desde = ParseDate(request.Query["desde"]),
                Hasta = ParseDate(request.Query["hasta"]),
                Texto = request.Query["texto"].ToString(),
                Usuario = request.Query["usuario"].ToString(),
                Pc = request.Query["pc"].ToString(),
                TipoMovimiento = request.Query["tipoMovimiento"].ToString(),
                TipoComprobante = request.Query["tipoComprobante"].ToString(),
                Riesgo = request.Query["riesgo"].ToString(),
                TipoControl = request.Query["tipoControl"].ToString(),
                CuentaCliente = request.Query["cuentaCliente"].ToString(),
                DiasMinimosSinFactura = ParseNullableInt(request.Query["diasMinimosSinFactura"]),
                UmbralModificaciones = ParseNullableInt(request.Query["umbralModificaciones"]),
                DiasToleranciaDuplicados = ParseNullableInt(request.Query["diasToleranciaDuplicados"]),
                SoloDiferenciasSucursal = string.Equals(request.Query["soloDiferenciasSucursal"], "true", StringComparison.OrdinalIgnoreCase),
                OrdenCampo = string.IsNullOrWhiteSpace(request.Query["ordenCampo"]) ? "fecha" : request.Query["ordenCampo"].ToString(),
                OrdenDireccion = string.IsNullOrWhiteSpace(request.Query["ordenDireccion"]) ? "desc" : request.Query["ordenDireccion"].ToString()
            };

            while (true)
            {
                var filter = new AuditoriaUsuarioFilterDto
                {
                    Desde = filterBase.Desde,
                    Hasta = filterBase.Hasta,
                    Texto = filterBase.Texto,
                    Usuario = filterBase.Usuario,
                    Pc = filterBase.Pc,
                    TipoMovimiento = filterBase.TipoMovimiento,
                    TipoComprobante = filterBase.TipoComprobante,
                    Riesgo = filterBase.Riesgo,
                    TipoControl = filterBase.TipoControl,
                    CuentaCliente = filterBase.CuentaCliente,
                    DiasMinimosSinFactura = filterBase.DiasMinimosSinFactura,
                    UmbralModificaciones = filterBase.UmbralModificaciones,
                    DiasToleranciaDuplicados = filterBase.DiasToleranciaDuplicados,
                    SoloDiferenciasSucursal = filterBase.SoloDiferenciasSucursal,
                    OrdenCampo = filterBase.OrdenCampo,
                    OrdenDireccion = filterBase.OrdenDireccion,
                    Pagina = pagina,
                    TamanioPagina = pageSize
                };

                var result = await auditoriaSvc.SearchUserAuditAsync(filter, ct);

                lastResult = result;
                if (result.Items.Count == 0)
                    break;

                allItems.AddRange(result.Items);
                if (allItems.Count >= result.TotalRegistros || result.Items.Count < pageSize)
                    break;

                pagina++;
            }

            lastResult ??= new AuditoriaUsuarioResultDto();
            var exportResult = new AuditoriaUsuarioResultDto
            {
                Items = allItems,
                Stats = lastResult.Stats,
                TotalRegistros = lastResult.TotalRegistros,
                Pagina = 1,
                TamanioPagina = allItems.Count,
                DiasMinimosSinFacturaDefault = lastResult.DiasMinimosSinFacturaDefault,
                UmbralModificacionesDefault = lastResult.UmbralModificacionesDefault,
                DiasToleranciaDuplicadosDefault = lastResult.DiasToleranciaDuplicadosDefault,
                SoloDiferenciasSucursalDefault = lastResult.SoloDiferenciasSucursalDefault
            };

            var bytes = exporter.ExportarUsuarios(filterBase, exportResult);
            var filename = AuditoriaExcelExporter.NombreArchivoUsuarios(filterBase);
            return Results.File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                filename);
        });

        app.MapGet("/compras/reportes/descargar-excel", async (
            HttpRequest request,
            IReporteComprasService reporteSvc,
            ReporteComprasExcelExporter exporter,
            CancellationToken ct) =>
        {
            static DateTime? ParseDate(string? v)
                => DateTime.TryParse(v, out var d) ? d : null;

            static string? NullIfEmpty(string? v)
                => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

            var tipo = request.Query["tipo"].ToString(); // "resumen" | "detalle"

            var filtros = new FiltrosReporteCompras
            {
                FechaDesde      = ParseDate(request.Query["fechaDesde"]),
                FechaHasta      = ParseDate(request.Query["fechaHasta"]),
                Proveedor       = NullIfEmpty(request.Query["proveedor"]),
                TipoComprobante = NullIfEmpty(request.Query["tc"]),
                TamanioPagina   = 500
            };

            if (string.Equals(tipo, "detalle", StringComparison.OrdinalIgnoreCase))
            {
                var allItems = new List<DetalleComprasFilaDto>();
                int totalRegistros = 0;
                int pagina = 1;

                while (true)
                {
                    filtros.Pagina = pagina;
                    var result = await reporteSvc.GetDetalleComprasAsync(filtros, ct);
                    totalRegistros = result.TotalRegistros;
                    if (result.Items.Count == 0) break;
                    allItems.AddRange(result.Items);
                    if (allItems.Count >= result.TotalRegistros || result.Items.Count < filtros.TamanioPagina) break;
                    pagina++;
                }

                var bytes = exporter.ExportarDetalle(allItems, filtros, totalRegistros);
                return Results.File(bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    ReporteComprasExcelExporter.NombreArchivoDetalle());
            }
            else
            {
                var resumen = await reporteSvc.GetResumenAsync(filtros, ct);
                var bytes = exporter.ExportarResumen(resumen, filtros);
                return Results.File(bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    ReporteComprasExcelExporter.NombreArchivoResumen());
            }
        });

        async Task<IResult> DescargarArticulosComprasExcel(
            HttpRequest request,
            IComprasDashboardService dashboardSvc,
            IAppUserSessionService appUserSession,
            ArticulosComprasExcelExporter exporter,
            CancellationToken ct)
        {
            RestoreUserSessionFromToken(request, appUserSession);

            static DateTime? ParseDate(string? value)
                => DateTime.TryParse(value, out var parsed) ? parsed : null;

            static string? NullIfEmpty(string? value)
                => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

            static bool MatchVariacion(ArticuloResumenDto item, string variacion, DashboardFilters filters)
                => variacion switch
                {
                    "subio" => item.VariacionPrecio > 0.005m,
                    "bajo" => item.VariacionPrecio < -0.005m,
                    "plano" => item.VariacionPrecio.HasValue && item.VariacionPrecio >= -0.005m && item.VariacionPrecio <= 0.005m,
                    "nuevo" => item.EsNuevo,
                    "alta" => EsAltaEnPeriodo(item, filters),
                    _ => true
                };

            static bool EsAltaEnPeriodo(ArticuloResumenDto item, DashboardFilters filters)
            {
                if (!item.FechaAlta.HasValue)
                    return false;

                var fechaAlta = item.FechaAlta.Value.Date;
                if (filters.FechaDesde.HasValue && fechaAlta < filters.FechaDesde.Value.Date)
                    return false;

                if (filters.FechaHasta.HasValue && fechaAlta > filters.FechaHasta.Value.Date)
                    return false;

                return true;
            }

            static bool MatchTexto(ArticuloResumenDto item, string texto)
            {
                if (string.IsNullOrWhiteSpace(texto))
                    return true;

                return Contains(item.IdArticulo, texto)
                    || Contains(item.DescripcionArticulo, texto)
                    || Contains(item.ProveedorPrincipal, texto)
                    || Contains(item.FechaAlta?.ToString("dd/MM/yyyy"), texto)
                    || Contains(item.UltimaCompra?.ToString("dd/MM/yyyy"), texto);
            }

            static bool Contains(string? value, string text)
                => !string.IsNullOrWhiteSpace(value)
                   && value.Contains(text, StringComparison.CurrentCultureIgnoreCase);

            var filters = new DashboardFilters
            {
                FechaDesde = ParseDate(request.Query["fechaDesde"]),
                FechaHasta = ParseDate(request.Query["fechaHasta"]),
                Proveedor = NullIfEmpty(request.Query["proveedor"]),
                Articulo = NullIfEmpty(request.Query["articulo"]),
                ArticuloCodigo = NullIfEmpty(request.Query["articuloCodigo"]),
                ArticuloDescripcion = NullIfEmpty(request.Query["articuloDescripcion"]),
                Rubro = NullIfEmpty(request.Query["rubro"]),
                Familia = NullIfEmpty(request.Query["familia"]),
                Usuario = NullIfEmpty(request.Query["usuario"]),
                Sucursal = NullIfEmpty(request.Query["sucursal"]),
                Deposito = NullIfEmpty(request.Query["deposito"]),
                Estado = NullIfEmpty(request.Query["estado"]),
                TipoComprobante = NullIfEmpty(request.Query["tc"])
            };

            var texto = request.Query["texto"].ToString().Trim();
            var variacion = request.Query["variacion"].ToString().Trim();
            var page = await dashboardSvc.GetArticulosPageDataAsync(filters, ct);
            var rows = page.Articulos
                .Where(x => MatchVariacion(x, variacion, filters))
                .Where(x => MatchTexto(x, texto))
                .ToList();

            var bytes = exporter.Exportar(rows, filters, texto, variacion);
            return Results.File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ArticulosComprasExcelExporter.NombreArchivo());
        }

        app.MapGet("/compras/articulos/descargar-excel", DescargarArticulosComprasExcel);
        app.MapGet("/{idweb}/{idbase:int}/compras/articulos/descargar-excel", DescargarArticulosComprasExcel);

        async Task<IResult> DescargarVentasRubrosExcel(
            HttpRequest request,
            IGestionDashboardService gestionSvc,
            IAppUserSessionService appUserSession,
            VentasRubrosExcelExporter exporter,
            CancellationToken ct)
        {
            RestoreUserSessionFromToken(request, appUserSession);

            static DateTime? ParseDate(string? value)
                => DateTime.TryParse(value, out var parsed) ? parsed : null;

            static string? NullIfEmpty(string? value)
                => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

            static List<string> ParseCodes(string? value)
                => string.IsNullOrWhiteSpace(value)
                    ? []
                    : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(x => x.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

            var filters = new VentasDashboardFilters
            {
                FechaDesde = ParseDate(request.Query["fechaDesde"]),
                FechaHasta = ParseDate(request.Query["fechaHasta"]),
                Cliente = NullIfEmpty(request.Query["cliente"]),
                Usuario = NullIfEmpty(request.Query["usuario"]),
                UnidadesNegocio = ParseCodes(request.Query["unidades"]),
                Deposito = NullIfEmpty(request.Query["deposito"]),
                TipoComprobante = NullIfEmpty(request.Query["tc"])
            };

            var page = await gestionSvc.GetVentasRubrosAsync(filters, ct);
            var bytes = exporter.Exportar(page, filters);
            return Results.File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                VentasRubrosExcelExporter.NombreArchivo());
        }

        app.MapGet("/ventas/rubros/descargar-excel", DescargarVentasRubrosExcel);
        app.MapGet("/{idweb}/{idbase:int}/ventas/rubros/descargar-excel", DescargarVentasRubrosExcel);

        static void RestoreUserSessionFromToken(HttpRequest request, IAppUserSessionService appUserSession)
        {
            var token = request.Query["token"].ToString();
            if (!string.IsNullOrWhiteSpace(token))
                appUserSession.TryRestoreFromToken(token);
        }

        async Task<IResult> DescargarLiquidacionExcel(
            HttpRequest request,
            ICargaViajesService cargaViajesSvc,
            IAppUserSessionService appUserSession,
            CargaViajesLiquidacionExcelExporter exporter,
            CancellationToken ct)
        {
            RestoreUserSessionFromToken(request, appUserSession);

            static DateTime? ParseDate(string? value)
                => DateTime.TryParse(value, out var parsed) ? parsed : null;

            static string TrimOrEmpty(string? value)
                => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

            var filters = new CargaViajesReporteLiquidacionFilters
            {
                FechaDesde = ParseDate(request.Query["desde"]),
                FechaHasta = ParseDate(request.Query["hasta"]),
                ChoferCodigo = TrimOrEmpty(request.Query["chofer"]),
                ClienteCodigo = TrimOrEmpty(request.Query["cliente"]),
                DestinoCodigo = TrimOrEmpty(request.Query["destino"]),
                TipoPersona = TrimOrEmpty(request.Query["tipoPersona"]),
                Estado = TrimOrEmpty(request.Query["estado"])
            };

            var rows = await cargaViajesSvc.SearchLiquidacionChoferesAsync(filters, ct);
            var chofer = string.IsNullOrWhiteSpace(filters.ChoferCodigo)
                ? null
                : await cargaViajesSvc.GetChoferByIdAsync(filters.ChoferCodigo, ct);
            var nombreEntidad = string.IsNullOrWhiteSpace(chofer?.Nombre)
                ? (string.IsNullOrWhiteSpace(filters.ChoferCodigo) ? "Chofer / Fletero" : filters.ChoferCodigo)
                : chofer.Nombre;
            var tituloEntidad = chofer is null
                ? nombreEntidad
                : chofer.EsFletero ? $"Fletero {nombreEntidad}" : $"Chofer {nombreEntidad}";

            var bytes = exporter.Exportar(rows, filters, tituloEntidad, nombreEntidad);
            return Results.File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                CargaViajesLiquidacionExcelExporter.NombreArchivo());
        }

        app.MapGet("/carga-viajes/liquidacion/descargar-excel", DescargarLiquidacionExcel);
        app.MapGet("/{idweb}/{idbase:int}/carga-viajes/liquidacion/descargar-excel", DescargarLiquidacionExcel);

        async Task<IResult> DescargarReporteExcel(
            HttpRequest request,
            ICargaViajesService cargaViajesSvc,
            IAppUserSessionService appUserSession,
            CargaViajesReporteExcelExporter exporter,
            CancellationToken ct)
        {
            RestoreUserSessionFromToken(request, appUserSession);

            static DateTime? ParseDate(string? value)
                => DateTime.TryParse(value, out var parsed) ? parsed : null;

            static string TrimOrEmpty(string? value)
                => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

            static bool ParseBool(string? value)
                => bool.TryParse(value, out var parsed) && parsed;

            var incluirClientes = ParseBool(request.Query["incluirClientes"]);
            var incluirTodoJunto = ParseBool(request.Query["incluirTodoJunto"]);

            var filters = new CargaViajesReporteLiquidacionFilters
            {
                FechaDesde = ParseDate(request.Query["desde"]),
                FechaHasta = ParseDate(request.Query["hasta"]),
                ChoferCodigo = TrimOrEmpty(request.Query["chofer"]),
                ClienteCodigo = TrimOrEmpty(request.Query["cliente"]),
                DestinoCodigo = TrimOrEmpty(request.Query["destino"]),
                TipoPersona = TrimOrEmpty(request.Query["tipoPersona"]),
                Estado = TrimOrEmpty(request.Query["estado"]),
                EstadoPago = TrimOrEmpty(request.Query["estadoPago"]),
                IncluirClientes = incluirClientes,
                IncluirTodoJunto = incluirTodoJunto
            };

            byte[] bytes;
            if (incluirTodoJunto)
            {
                var rows = await cargaViajesSvc.SearchReporteClientesAsync(filters, ct);
                bytes = exporter.ExportarTodoJunto(rows, filters);
            }
            else if (incluirClientes)
            {
                var rows = await cargaViajesSvc.SearchReporteClientesAsync(filters, ct);
                bytes = exporter.ExportarClientes(rows, filters);
            }
            else
            {
                var rows = await cargaViajesSvc.SearchLiquidacionChoferesAsync(filters, ct);
                bytes = exporter.ExportarChoferesFleteros(rows, filters);
            }

            return Results.File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                CargaViajesReporteExcelExporter.NombreArchivo());
        }

        app.MapGet("/carga-viajes/reportes/descargar-excel", DescargarReporteExcel);
        app.MapGet("/{idweb}/{idbase:int}/carga-viajes/reportes/descargar-excel", DescargarReporteExcel);

        async Task<IResult> DescargarTarifasExcel(
            HttpRequest request,
            ICargaViajesService cargaViajesSvc,
            IAppUserSessionService appUserSession,
            CargaViajesTarifasExcelExporter exporter,
            CancellationToken ct)
        {
            RestoreUserSessionFromToken(request, appUserSession);

            static bool ParseBool(string? value)
                => bool.TryParse(value, out var parsed) && parsed;

            static string TrimOrEmpty(string? value)
                => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

            var filters = new CargaViajesFilters
            {
                AgruparPor = TrimOrEmpty(request.Query["agruparPor"]),
                Texto = TrimOrEmpty(request.Query["texto"]),
                Cliente = TrimOrEmpty(request.Query["cliente"]),
                Chofer = TrimOrEmpty(request.Query["chofer"]),
                Destino = TrimOrEmpty(request.Query["destino"]),
                TipoVehiculo = TrimOrEmpty(request.Query["tipoVehiculo"]),
                TarifaFletero = TrimOrEmpty(request.Query["tarifaFletero"]),
                Activo = TrimOrEmpty(request.Query["activo"]),
                SortBy = TrimOrEmpty(request.Query["sortBy"]),
                SortDescending = ParseBool(request.Query["sortDescending"]),
                PageNumber = 1,
                PageSize = 500
            };

            var allItems = new List<CargaViajeTarifaGridItemDto>();
            while (true)
            {
                var page = await cargaViajesSvc.SearchTarifasAsync(filters, ct);
                if (page.Items.Count == 0)
                    break;

                allItems.AddRange(page.Items);
                if (allItems.Count >= page.Total || page.Items.Count < filters.PageSize)
                    break;

                filters.PageNumber++;
            }

            var bytes = exporter.Exportar(allItems, filters, filters.AgruparPor);
            return Results.File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                CargaViajesTarifasExcelExporter.NombreArchivo());
        }

        app.MapGet("/carga-viajes/tarifas/descargar-excel", DescargarTarifasExcel);
        app.MapGet("/{idweb}/{idbase:int}/carga-viajes/tarifas/descargar-excel", DescargarTarifasExcel);

        async Task<IResult> DescargarViajesExcel(
            HttpRequest request,
            ICargaViajesService cargaViajesSvc,
            IAppUserSessionService appUserSession,
            CargaViajesViajesExcelExporter exporter,
            CancellationToken ct)
        {
            RestoreUserSessionFromToken(request, appUserSession);

            static DateTime? ParseDate(string? value)
                => DateTime.TryParse(value, out var parsed) ? parsed : null;

            static string TrimOrEmpty(string? value)
                => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

            static bool ParseBool(string? value)
                => bool.TryParse(value, out var parsed) && parsed;

            var filters = new CargaViajesFilters
            {
                FechaDesde = ParseDate(request.Query["fechaDesde"]),
                FechaHasta = ParseDate(request.Query["fechaHasta"]),
                Texto = TrimOrEmpty(request.Query["texto"]),
                Cliente = TrimOrEmpty(request.Query["cliente"]),
                Chofer = TrimOrEmpty(request.Query["chofer"]),
                Destino = TrimOrEmpty(request.Query["destino"]),
                TipoVehiculo = TrimOrEmpty(request.Query["tipoVehiculo"]),
                Estado = TrimOrEmpty(request.Query["estado"]),
                Usuario = TrimOrEmpty(request.Query["usuario"]),
                IdComprobante = TrimOrEmpty(request.Query["idComprobante"]),
                SortBy = TrimOrEmpty(request.Query["sortBy"]),
                SortDescending = ParseBool(request.Query["sortDescending"]),
                PageNumber = 1,
                PageSize = 500
            };

            var allItems = new List<CargaViajesGridItemDto>();
            while (true)
            {
                var page = await cargaViajesSvc.SearchViajesAsync(filters, ct);
                if (page.Items.Count == 0)
                    break;

                allItems.AddRange(page.Items);
                if (allItems.Count >= page.Total || page.Items.Count < filters.PageSize)
                    break;

                filters.PageNumber++;
            }

            var bytes = exporter.Exportar(allItems, filters);
            return Results.File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                CargaViajesViajesExcelExporter.NombreArchivo());
        }

        app.MapGet("/carga-viajes/descargar-excel", DescargarViajesExcel);
        app.MapGet("/{idweb}/{idbase:int}/carga-viajes/descargar-excel", DescargarViajesExcel);

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

        app.MapGet("/api/conversaciones/anydesk/registro.reg", (
            string? ruta,
            IAnyDeskLocalSettingsService anyDeskSvc) =>
        {
            string script;
            try
            {
                script = anyDeskSvc.BuildRegistryScript(ruta ?? string.Empty);
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(exception.Message);
            }

            // UTF-16LE con BOM: es lo que regedit espera de forma confiable para archivos .reg,
            // sobre todo si la ruta tiene caracteres no ASCII.
            var bytes = new UnicodeEncoding(bigEndian: false, byteOrderMark: true).GetBytes(script);
            return Results.File(bytes, "application/octet-stream", "anydesk-conectar.reg");
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

        // Cada canal tiene dos rutas: la de siempre (sin token, cae al fallback de conexión por
        // defecto — se conserva para no romper lo que ya está configurado en Meta/MercadoLibre
        // para el cliente actual) y una nueva con "/{token}" (resuelve el tenant desde
        // ALFA_CENTRAL.dbo.bases.WebhookToken ANTES de tocar cualquier tabla, sin depender de la
        // sesión del usuario — necesario porque un webhook entrante nunca tiene sesión).
        app.MapGet("/api/conversaciones/whatsapp/webhook", HandleWhatsAppVerifyAsync);
        app.MapGet("/api/conversaciones/whatsapp/webhook/{token}", async (
            string token,
            HttpRequest request,
            IConversacionesConfigService configService,
            ICentralBasesService basesService,
            ISessionService sessionService,
            CancellationToken ct) =>
        {
            if (!await TryResolveWebhookTenantAsync(token, basesService, sessionService, ct))
                return Results.NotFound();

            return await HandleWhatsAppVerifyAsync(request, configService, ct);
        });

        app.MapPost("/api/conversaciones/whatsapp/webhook", HandleWhatsAppMessageAsync);
        app.MapPost("/api/conversaciones/whatsapp/webhook/{token}", async (
            string token,
            HttpRequest request,
            IConversacionesConfigService configService,
            IConversacionesService svc,
            ICentralBasesService basesService,
            ISessionService sessionService,
            CancellationToken ct) =>
        {
            if (!await TryResolveWebhookTenantAsync(token, basesService, sessionService, ct))
                return Results.NotFound();

            return await HandleWhatsAppMessageAsync(request, configService, svc, ct);
        });

        app.MapGet("/api/conversaciones/instagram/webhook", HandleInstagramVerifyAsync);
        app.MapGet("/api/conversaciones/instagram/webhook/{token}", async (
            string token,
            HttpRequest request,
            IConversacionesConfigService configService,
            ICentralBasesService basesService,
            ISessionService sessionService,
            CancellationToken ct) =>
        {
            if (!await TryResolveWebhookTenantAsync(token, basesService, sessionService, ct))
                return Results.NotFound();

            return await HandleInstagramVerifyAsync(request, configService, ct);
        });

        app.MapPost("/api/conversaciones/instagram/webhook", HandleInstagramMessageAsync);
        app.MapPost("/api/conversaciones/instagram/webhook/{token}", async (
            string token,
            HttpRequest request,
            IConversacionesConfigService configService,
            IConversacionesService svc,
            ICentralBasesService basesService,
            ISessionService sessionService,
            CancellationToken ct) =>
        {
            if (!await TryResolveWebhookTenantAsync(token, basesService, sessionService, ct))
                return Results.NotFound();

            return await HandleInstagramMessageAsync(request, configService, svc, ct);
        });

        app.MapGet("/api/conversaciones/facebook/webhook", HandleFacebookVerifyAsync);
        app.MapGet("/api/conversaciones/facebook/webhook/{token}", async (
            string token,
            HttpRequest request,
            IConversacionesConfigService configService,
            ICentralBasesService basesService,
            ISessionService sessionService,
            CancellationToken ct) =>
        {
            if (!await TryResolveWebhookTenantAsync(token, basesService, sessionService, ct))
                return Results.NotFound();

            return await HandleFacebookVerifyAsync(request, configService, ct);
        });

        app.MapPost("/api/conversaciones/facebook/webhook", HandleFacebookMessageAsync);
        app.MapPost("/api/conversaciones/facebook/webhook/{token}", async (
            string token,
            HttpRequest request,
            IConversacionesConfigService configService,
            IConversacionesService svc,
            ICentralBasesService basesService,
            ISessionService sessionService,
            CancellationToken ct) =>
        {
            if (!await TryResolveWebhookTenantAsync(token, basesService, sessionService, ct))
                return Results.NotFound();

            return await HandleFacebookMessageAsync(request, configService, svc, ct);
        });

        app.MapPost("/api/conversaciones/mercadolibre/webhook", HandleMercadoLibreMessageAsync);
        app.MapPost("/api/conversaciones/mercadolibre/webhook/{token}", async (
            string token,
            HttpRequest request,
            IConversacionesService svc,
            ICentralBasesService basesService,
            ISessionService sessionService,
            CancellationToken ct) =>
        {
            if (!await TryResolveWebhookTenantAsync(token, basesService, sessionService, ct))
                return Results.NotFound();

            return await HandleMercadoLibreMessageAsync(request, svc, ct);
        });

        app.MapGet("/api/conversaciones/mercadolibre/oauth/callback", async (
            HttpRequest request,
            IConversacionesConfigService configService,
            IAppEventService appEvents,
            IHttpClientFactory httpClientFactory,
            CancellationToken ct) =>
        {
            var code = request.Query["code"].ToString();
            var error = request.Query["error"].ToString();
            var errorDescription = request.Query["error_description"].ToString();

            await appEvents.LogAuditAsync(
                "Conversaciones",
                "MercadoLibreOAuthCallback",
                "TA_CONFIGURACION",
                "CONVERSACIONES",
                string.IsNullOrWhiteSpace(error)
                    ? "Callback OAuth de Mercado Libre recibido."
                    : "Callback OAuth de Mercado Libre recibido con error.",
                new
                {
                    TieneCode = !string.IsNullOrWhiteSpace(code),
                    Error = error,
                    ErrorDescription = errorDescription
                },
                ct);

            if (!string.IsNullOrWhiteSpace(error))
                return Results.Text("No se pudo vincular Mercado Libre. Volvé a AlfaCore y revisá la configuración del canal.", "text/plain; charset=utf-8");

            if (string.IsNullOrWhiteSpace(code))
                return Results.Text("Mercado Libre no devolvió un código de autorización.", "text/plain; charset=utf-8");

            var config = await configService.GetMercadoLibreConfigAsync(ct);
            if (!config.IsConfiguredForAuth)
                return Results.Text("Mercado Libre autorizó la app, pero faltan Client ID o Client Secret en AlfaCore para obtener los tokens.", "text/plain; charset=utf-8");

            var redirectUri = config.GetOAuthCallbackUrl();
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"{config.ApiBaseUrl.TrimEnd('/')}/oauth/token");
            tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = redirectUri
            });

            var client = httpClientFactory.CreateClient();
            using var tokenResponse = await client.SendAsync(tokenRequest, ct);
            var tokenBody = await tokenResponse.Content.ReadAsStringAsync(ct);
            if (!tokenResponse.IsSuccessStatusCode)
                return Results.Text($"Mercado Libre autorizó la app, pero no se pudieron obtener los tokens. Respuesta: {(int)tokenResponse.StatusCode}", "text/plain; charset=utf-8");

            using var tokenJson = JsonDocument.Parse(tokenBody);
            var root = tokenJson.RootElement;
            config.AccessToken = root.TryGetProperty("access_token", out var accessToken) ? accessToken.GetString() ?? string.Empty : string.Empty;
            config.RefreshToken = root.TryGetProperty("refresh_token", out var refreshToken) ? refreshToken.GetString() ?? string.Empty : string.Empty;
            config.SellerId = root.TryGetProperty("user_id", out var userId) ? userId.GetRawText().Trim('"') : config.SellerId;
            await configService.SaveMercadoLibreConfigAsync(config, ct);

            return Results.Text("Mercado Libre quedó vinculado correctamente. Ya podés volver a AlfaCore.", "text/plain; charset=utf-8");
        });

        app.MapGet("/api/conversaciones/instagram/oauth/callback", async (
            HttpRequest request,
            IAppEventService appEvents,
            CancellationToken ct) =>
        {
            var code = request.Query["code"].ToString();
            var error = request.Query["error"].ToString();
            var errorReason = request.Query["error_reason"].ToString();
            var errorDescription = request.Query["error_description"].ToString();

            await appEvents.LogAuditAsync(
                "Conversaciones",
                "InstagramOAuthCallback",
                "CONV_CUENTAS_CANAL",
                string.Empty,
                string.IsNullOrWhiteSpace(error)
                    ? "Callback OAuth de Instagram recibido."
                    : "Callback OAuth de Instagram recibido con error.",
                new
                {
                    TieneCode = !string.IsNullOrWhiteSpace(code),
                    Error = error,
                    ErrorReason = errorReason,
                    ErrorDescription = errorDescription
                },
                ct);

            if (!string.IsNullOrWhiteSpace(error))
                return Results.Text("No se pudo vincular Instagram. Volvé a AlfaCore y revisá la configuración del canal.", "text/plain; charset=utf-8");

            if (string.IsNullOrWhiteSpace(code))
                return Results.Text("Meta no devolvió un código de autorización para Instagram.", "text/plain; charset=utf-8");

            return Results.Text("Instagram devolvió autorización correctamente. Ya podés volver a AlfaCore para completar la vinculación.", "text/plain; charset=utf-8");
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
            var idBaseRaw = request.Query["idBase"].ToString();
            var idBase = int.TryParse(idBaseRaw, out var parsedIdBase) && parsedIdBase > 0
                ? parsedIdBase
                : (int?)null;
            var download = string.Equals(request.Query["download"].ToString(), "1", StringComparison.OrdinalIgnoreCase);
            var preview = string.Equals(request.Query["preview"].ToString(), "1", StringComparison.OrdinalIgnoreCase);
            var adjunto = await svc.GetAttachmentForServeAsync(idAdjunto, idBase, includeDownloadName: download, ct);
            if (adjunto is null)
                return Results.NotFound();

            var mime = NormalizeAttachmentMime(adjunto.MimeType, adjunto.NombreArchivo);
            var hasLocalFile = !string.IsNullOrWhiteSpace(adjunto.RutaLocal) && File.Exists(adjunto.RutaLocal);
            var hasSqlContent = adjunto.Contenido.Length > 0;
            if (!hasLocalFile && !hasSqlContent)
                return Results.NotFound();

            if (download)
            {
                request.HttpContext.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                request.HttpContext.Response.Headers.Pragma = "no-cache";
                request.HttpContext.Response.Headers.Expires = "0";
            }
            else
            {
                request.HttpContext.Response.Headers.CacheControl = preview
                    ? "private, max-age=604800, immutable"
                    : "private, max-age=86400";
                request.HttpContext.Response.Headers.Pragma = string.Empty;
                request.HttpContext.Response.Headers.Expires = DateTimeOffset.UtcNow.AddDays(preview ? 7 : 1).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }

            var downloadName = download
                ? string.IsNullOrWhiteSpace(adjunto.NombreDescarga) ? adjunto.NombreArchivo : adjunto.NombreDescarga
                : null;

            if (!hasLocalFile)
            {
                var fechaArchivo = adjunto.FechaHoraModificacion?.ToUniversalTime() ?? DateTime.UtcNow;
                var lastModifiedSql = new DateTimeOffset(fechaArchivo, TimeSpan.Zero);
                var entityTagSql = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"sql-{adjunto.Contenido.Length:x}-{lastModifiedSql.UtcTicks:x}\"");
                return Results.File(
                    adjunto.Contenido,
                    contentType: mime,
                    fileDownloadName: downloadName,
                    lastModified: lastModifiedSql,
                    entityTag: entityTagSql,
                    enableRangeProcessing: false);
            }

            var fileInfo = new FileInfo(adjunto.RutaLocal);
            var lastModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);
            var entityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{fileInfo.Length:x}-{fileInfo.LastWriteTimeUtc.Ticks:x}\"");
            return Results.File(
                adjunto.RutaLocal,
                contentType: mime,
                fileDownloadName: downloadName,
                lastModified: lastModified,
                entityTag: entityTag,
                enableRangeProcessing: true);
        });

        app.MapGet("/api/notificaciones-push/settings", async (
            string? deviceId,
            HttpRequest request,
            AppUserSessionStore sessionStore,
            INotificacionesPushService svc,
            CancellationToken ct) =>
        {
            if (!TryGetApiUser(request, sessionStore, out var user) || user is null)
                return PushApiUnauthorized();

            try
            {
                var settings = await svc.GetClientSettingsAsync(user.UserName, deviceId ?? string.Empty, ct);
                var diag = await svc.GetDiagnosticsAsync(user.UserName, deviceId ?? string.Empty, ct);
                var currentDevice = (deviceId ?? string.Empty).Trim();
                var currentRegistered = diag.Subscriptions.Any(x =>
                    string.Equals(x.DeviceId, currentDevice, StringComparison.OrdinalIgnoreCase) && x.Success);
                return Results.Json(new
                {
                    ok = true,
                    message = "Configuración de notificaciones cargada.",
                    settings.Configurado,
                    settings.PublicKey,
                    settings.PublicKeyConfigurada,
                    settings.PrivateKeyConfigurada,
                    settings.SubjectConfigurado,
                    settings.ConfiguracionMensaje,
                    settings.Preferences,
                    settings.TieneAccesoConversaciones,
                    diagnostics = new
                    {
                        subscriptionsFound = diag.SubscriptionCount,
                        activeSubscriptions = diag.ActiveSubscriptionCount,
                        currentDeviceRegistered = currentRegistered,
                        sentOk = 0,
                        sentFailed = 0,
                        lastProviderStatus = diag.Subscriptions
                            .Where(x => x.StatusCode.HasValue)
                            .Select(x => x.StatusCode)
                            .FirstOrDefault(),
                        lastProviderError = diag.Subscriptions
                            .Where(x => !string.IsNullOrWhiteSpace(x.Error))
                            .Select(x => x.Error)
                            .FirstOrDefault() ?? string.Empty
                    }
                });
            }
            catch (Exception ex)
            {
                return PushApiException(ex);
            }
        });

        app.MapPost("/api/notificaciones-push/subscription", async (
            NotificacionesPushRegistrationRequest body,
            HttpRequest request,
            AppUserSessionStore sessionStore,
            INotificacionesPushService svc,
            CancellationToken ct) =>
        {
            if (!TryGetApiUser(request, sessionStore, out var user) || user is null)
                return PushApiUnauthorized();

            try
            {
                await svc.SaveSubscriptionAsync(user.UserName, body, ct);
                var diag = await svc.GetDiagnosticsAsync(user.UserName, body.DeviceId, ct);
                return Results.Json(new
                {
                    ok = true,
                    message = "Suscripción push guardada.",
                    diagnostics = new
                    {
                        subscriptionsFound = diag.SubscriptionCount,
                        activeSubscriptions = diag.ActiveSubscriptionCount,
                        currentDeviceRegistered = diag.Subscriptions.Any(x => string.Equals(x.DeviceId, body.DeviceId, StringComparison.OrdinalIgnoreCase) && x.Success),
                        sentOk = 0,
                        sentFailed = 0,
                        lastProviderStatus = diag.Subscriptions.Where(x => x.StatusCode.HasValue).Select(x => x.StatusCode).FirstOrDefault(),
                        lastProviderError = diag.Subscriptions.Where(x => !string.IsNullOrWhiteSpace(x.Error)).Select(x => x.Error).FirstOrDefault() ?? string.Empty
                    }
                });
            }
            catch (Exception ex)
            {
                return PushApiException(ex);
            }
        });

        app.MapDelete("/api/notificaciones-push/subscription", async (
            string? deviceId,
            HttpRequest request,
            AppUserSessionStore sessionStore,
            INotificacionesPushService svc,
            CancellationToken ct) =>
        {
            if (!TryGetApiUser(request, sessionStore, out var user) || user is null)
                return PushApiUnauthorized();

            try
            {
                var currentDeviceId = deviceId ?? string.Empty;
                await svc.DeleteSubscriptionAsync(user.UserName, currentDeviceId, ct);
                var diag = await svc.GetDiagnosticsAsync(user.UserName, currentDeviceId, ct);
                return Results.Json(new
                {
                    ok = true,
                    message = "Suscripción push eliminada.",
                    diagnostics = new
                    {
                        subscriptionsFound = diag.SubscriptionCount,
                        activeSubscriptions = diag.ActiveSubscriptionCount,
                        currentDeviceRegistered = diag.Subscriptions.Any(x => string.Equals(x.DeviceId, currentDeviceId, StringComparison.OrdinalIgnoreCase) && x.Success),
                        sentOk = 0,
                        sentFailed = 0,
                        lastProviderStatus = diag.Subscriptions.Where(x => x.StatusCode.HasValue).Select(x => x.StatusCode).FirstOrDefault(),
                        lastProviderError = diag.Subscriptions.Where(x => !string.IsNullOrWhiteSpace(x.Error)).Select(x => x.Error).FirstOrDefault() ?? string.Empty
                    }
                });
            }
            catch (Exception ex)
            {
                return PushApiException(ex);
            }
        });

        app.MapPost("/api/notificaciones-push/preferences", async (
            NotificacionesPushPreferencesRequest body,
            HttpRequest request,
            AppUserSessionStore sessionStore,
            INotificacionesPushService svc,
            CancellationToken ct) =>
        {
            if (!TryGetApiUser(request, sessionStore, out var user) || user is null)
                return PushApiUnauthorized();

            try
            {
                await svc.SavePreferencesAsync(user.UserName, body, ct);
                var diag = await svc.GetDiagnosticsAsync(user.UserName, body.DeviceId, ct);
                return Results.Json(new
                {
                    ok = true,
                    message = "Preferencias de notificaciones guardadas.",
                    diagnostics = new
                    {
                        subscriptionsFound = diag.SubscriptionCount,
                        activeSubscriptions = diag.ActiveSubscriptionCount,
                        currentDeviceRegistered = diag.Subscriptions.Any(x => string.Equals(x.DeviceId, body.DeviceId, StringComparison.OrdinalIgnoreCase) && x.Success),
                        sentOk = 0,
                        sentFailed = 0,
                        lastProviderStatus = diag.Subscriptions.Where(x => x.StatusCode.HasValue).Select(x => x.StatusCode).FirstOrDefault(),
                        lastProviderError = diag.Subscriptions.Where(x => !string.IsNullOrWhiteSpace(x.Error)).Select(x => x.Error).FirstOrDefault() ?? string.Empty
                    }
                });
            }
            catch (Exception ex)
            {
                return PushApiException(ex);
            }
        });

        app.MapPost("/api/notificaciones-push/test", async (
            NotificacionesPushDeviceRequest body,
            HttpRequest request,
            AppUserSessionStore sessionStore,
            INotificacionesPushService svc,
            CancellationToken ct) =>
        {
            if (!TryGetApiUser(request, sessionStore, out var user) || user is null)
                return PushApiUnauthorized();

            try
            {
                var result = await svc.SendTestAsync(user.UserName, body.DeviceId, ct);
                var diag = await svc.GetDiagnosticsAsync(user.UserName, body.DeviceId, ct);
                return Results.Json(new
                {
                    ok = true,
                    message = result.TotalCount == 0
                        ? "No hay suscripciones activas para enviar prueba."
                        : "Prueba de push ejecutada.",
                    result.TotalCount,
                    result.SuccessCount,
                    result.FailCount,
                    result.Results,
                    diagnostics = new
                    {
                        subscriptionsFound = diag.SubscriptionCount,
                        activeSubscriptions = diag.ActiveSubscriptionCount,
                        currentDeviceRegistered = diag.Subscriptions.Any(x => string.Equals(x.DeviceId, body.DeviceId, StringComparison.OrdinalIgnoreCase) && x.Success),
                        sentOk = result.SuccessCount,
                        sentFailed = result.FailCount,
                        lastProviderStatus = result.Results.Where(x => x.StatusCode.HasValue).Select(x => x.StatusCode).FirstOrDefault(),
                        lastProviderError = result.Results.Where(x => !string.IsNullOrWhiteSpace(x.Error)).Select(x => x.Error).FirstOrDefault() ?? string.Empty
                    }
                });
            }
            catch (Exception ex)
            {
                return PushApiException(ex);
            }
        });

        app.MapGet("/api/notificaciones-push/diagnostico", async (
            string? deviceId,
            HttpRequest request,
            AppUserSessionStore sessionStore,
            INotificacionesPushService svc,
            CancellationToken ct) =>
        {
            if (!TryGetApiUser(request, sessionStore, out var user) || user is null)
                return PushApiUnauthorized();

            try
            {
                var result = await svc.GetDiagnosticsAsync(user.UserName, deviceId ?? string.Empty, ct);
                return Results.Json(new
                {
                    ok = true,
                    message = "Diagnóstico push cargado.",
                    result.UserName,
                    result.DeviceId,
                    result.SubscriptionCount,
                    result.ActiveSubscriptionCount,
                    result.Preferences,
                    result.Subscriptions,
                    diagnostics = new
                    {
                        subscriptionsFound = result.SubscriptionCount,
                        activeSubscriptions = result.ActiveSubscriptionCount,
                        currentDeviceRegistered = result.Subscriptions.Any(x => string.Equals(x.DeviceId, result.DeviceId, StringComparison.OrdinalIgnoreCase) && x.Success),
                        sentOk = 0,
                        sentFailed = 0,
                        lastProviderStatus = result.Subscriptions.Where(x => x.StatusCode.HasValue).Select(x => x.StatusCode).FirstOrDefault(),
                        lastProviderError = result.Subscriptions.Where(x => !string.IsNullOrWhiteSpace(x.Error)).Select(x => x.Error).FirstOrDefault() ?? string.Empty
                    }
                });
            }
            catch (Exception ex)
            {
                return PushApiException(ex);
            }
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

    private static bool TryGetApiUser(HttpRequest request, AppUserSessionStore sessionStore, out AppUserSessionInfo? user)
    {
        user = null;
        var token = request.Headers["X-AlfaCore-User-Token"].ToString();
        if (string.IsNullOrWhiteSpace(token))
            return false;

        return sessionStore.TryGet(token.Trim(), out user) && user is not null;
    }

    private static IResult PushApiOk()
        => Results.Json(new { ok = true });

    private static IResult PushApiUnauthorized()
        => Results.Json(
            new
            {
                ok = false,
                message = "Tu sesión expiró o no tenés permisos para configurar notificaciones."
            },
            statusCode: StatusCodes.Status401Unauthorized);

    private static IResult PushApiException(Exception exception)
    {
        var message = exception is AppUserFacingException userFacing
            ? userFacing.Message
            : exception.Message;

        return Results.Json(
            new
            {
                ok = false,
                message = string.IsNullOrWhiteSpace(message)
                    ? "No se pudo completar la operación de notificaciones."
                    : message
            },
            statusCode: StatusCodes.Status500InternalServerError);
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static decimal? ParseDecimalOrNull(string? value)
        => decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string BuildVb6ConsumeHtml(Vb6ConsumeTicketResult result)
    {
        var sessionId = HtmlEncode(result.SqlSessionId);
        var token = JsStringEncode(result.UserToken);
        var redirectUrl = JsStringEncode(result.RedirectUrl);

        return string.Concat(
            "<!doctype html>",
            "<html lang=\"es\">",
            "<head>",
            "<meta charset=\"utf-8\" />",
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />",
            "<title>AlfaCore</title>",
            "</head>",
            "<body>",
            "<script>",
            "(function () {",
            "try {",
            "if (window.alfaCoreSqlSession && typeof window.alfaCoreSqlSession.setActive === 'function') {",
            "window.alfaCoreSqlSession.setActive('", sessionId, "');",
            "} else {",
            "localStorage.setItem('alfacore.baseId', '", sessionId, "');",
            "}",
            "localStorage.setItem('alfacore_user_token', '", token, "');",
            "window.location.replace('", redirectUrl, "');",
            "} catch (error) {",
            "document.body.innerHTML = '<pre>No se pudo preparar la sesion: ' + (error && error.message ? error.message : error) + '</pre>';",
            "}",
            "})();",
            "</script>",
            "<noscript>Necesitas JavaScript habilitado para continuar.</noscript>",
            "</body>",
            "</html>");
    }

    private static string BuildVb6ErrorHtml(string message)
    {
        var safeMessage = HtmlEncode(message);
        return string.Concat(
            "<!doctype html>",
            "<html lang=\"es\">",
            "<head>",
            "<meta charset=\"utf-8\" />",
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />",
            "<title>AlfaCore - Error</title>",
            "</head>",
            "<body>",
            "<pre>", safeMessage, "</pre>",
            "</body>",
            "</html>");
    }

    private static string HtmlEncode(string value)
        => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    private static string JsStringEncode(string value)
        => (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Resuelve a qué base pertenece un token de webhook y, si existe, fuerza esa base como
    /// activa para el resto de este request (ver <see cref="ISessionService.SetWebhookOverride"/>).
    /// Devuelve <c>false</c> si el token no corresponde a ninguna base — el caller debe responder
    /// 404 sin exponer si el token "casi" era válido.
    /// </summary>
    private static async Task<bool> TryResolveWebhookTenantAsync(
        string token,
        ICentralBasesService basesService,
        ISessionService sessionService,
        CancellationToken ct)
    {
        var baseInfo = await basesService.GetByWebhookTokenAsync(token, ct);
        if (baseInfo is null)
        {
            return false;
        }

        sessionService.SetWebhookOverride(new SessionDto
        {
            BaseId = baseInfo.IdBase,
            Nombre = baseInfo.Nombre,
            Servidor = baseInfo.DbServer,
            BaseDatos = baseInfo.DbName,
            Usuario = baseInfo.DbUser,
            Password = baseInfo.DbPassword,
            TrustServerCertificate = true
        });

        return true;
    }

    /// <summary>
    /// Fija la base indicada como activa para este request (endpoint de descarga de adjuntos de
    /// Interfaces, que corre fuera del circuito Blazor y por eso no arrastra la base seleccionada).
    /// Valida que el usuario autenticado tenga acceso a esa base: superadmin ve todas, el resto solo
    /// las de su cliente. Devuelve <c>false</c> (el caller responde 404) si no hay usuario, la base
    /// no existe o el usuario no tiene acceso. <c>idBase == 0</c> se acepta como passthrough
    /// (modo legacy / base única: la resolución del scope ya devuelve la base correcta).
    /// </summary>
    private static async Task<bool> TryActivateInterfacesBaseAsync(
        int idBase,
        ISessionService sessionService,
        ICentralBasesService basesService,
        IAppUserSessionService appUserSession,
        CancellationToken ct)
    {
        if (idBase <= 0)
            return true;

        var user = appUserSession.CurrentUser;
        if (user is null)
            return false;

        var baseInfo = await basesService.GetByIdAsync(idBase, ct);
        if (baseInfo is null)
            return false;

        // Autorización: superadmin accede a todas; el resto solo a las bases de su cliente.
        if (!user.SuperAdmin &&
            !string.Equals(baseInfo.IdCliente.Trim(), user.IdCliente.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        sessionService.SetWebhookOverride(new SessionDto
        {
            BaseId = baseInfo.IdBase,
            Nombre = baseInfo.Nombre,
            Servidor = baseInfo.DbServer,
            BaseDatos = baseInfo.DbName,
            Usuario = baseInfo.DbUser,
            Password = baseInfo.DbPassword,
            TrustServerCertificate = true
        });

        return true;
    }

    private static async Task<IResult> HandleWhatsAppVerifyAsync(
        HttpRequest request,
        IConversacionesConfigService configService,
        CancellationToken ct)
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
    }

    private static async Task<IResult> HandleWhatsAppMessageAsync(
        HttpRequest request,
        IConversacionesConfigService configService,
        IConversacionesService svc,
        CancellationToken ct)
    {
        var options = await configService.GetWhatsAppConfigAsync(ct);

        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawPayload = await reader.ReadToEndAsync(ct);

        // A diferencia de Instagram/Facebook, WhatsApp no exigía App Secret hasta ahora — exigirlo
        // de golpe rompería al cliente que ya está en producción sin haberlo cargado. Se valida
        // la firma solo si el App Secret está configurado; queda como mejora pendiente pedirlo
        // siempre una vez que la configuración actual lo tenga cargado.
        if (!string.IsNullOrWhiteSpace(options.AppSecret))
        {
            var signature = request.Headers["X-Hub-Signature-256"].ToString();
            if (!IsValidMetaSignature(rawPayload, options.AppSecret, signature))
                return Results.Unauthorized();
        }

        using var payload = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawPayload) ? "{}" : rawPayload);
        var headers = request.Headers.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);

        var result = await svc.RegisterIncomingWebhookAsync(new ConversacionWebhookRequest
        {
            Payload = payload,
            RawPayload = rawPayload,
            Headers = headers
        }, ct);

        return Results.Ok(result);
    }

    private static async Task<IResult> HandleInstagramVerifyAsync(
        HttpRequest request,
        IConversacionesConfigService configService,
        CancellationToken ct)
    {
        var options = await configService.GetInstagramConfigAsync(ct);
        var mode = request.Query["hub.mode"].ToString();
        var verifyToken = request.Query["hub.verify_token"].ToString();
        var challenge = request.Query["hub.challenge"].ToString();

        if (string.IsNullOrWhiteSpace(options.VerifyToken))
            return Results.Problem("Instagram VerifyToken no está configurado.", statusCode: StatusCodes.Status500InternalServerError);

        if (mode == "subscribe" && verifyToken == options.VerifyToken)
            return Results.Text(challenge, "text/plain");

        return Results.Unauthorized();
    }

    private static async Task<IResult> HandleInstagramMessageAsync(
        HttpRequest request,
        IConversacionesConfigService configService,
        IConversacionesService svc,
        CancellationToken ct)
    {
        var options = await configService.GetInstagramConfigAsync(ct);
        if (string.IsNullOrWhiteSpace(options.AppSecret))
            return Results.Problem("Instagram App Secret no está configurado.", statusCode: StatusCodes.Status500InternalServerError);

        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawPayload = await reader.ReadToEndAsync(ct);
        var signature = request.Headers["X-Hub-Signature-256"].ToString();
        if (!IsValidMetaSignature(rawPayload, options.AppSecret, signature))
            return Results.Unauthorized();

        using var payload = JsonDocument.Parse(rawPayload);
        var result = await svc.RegisterIncomingInstagramWebhookAsync(new ConversacionWebhookRequest
        {
            Payload = payload,
            RawPayload = rawPayload,
            Headers = request.Headers.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToString(),
                StringComparer.OrdinalIgnoreCase)
        }, ct);

        return Results.Ok(result);
    }

    private static async Task<IResult> HandleFacebookVerifyAsync(
        HttpRequest request,
        IConversacionesConfigService configService,
        CancellationToken ct)
    {
        var options = await configService.GetFacebookConfigAsync(ct);
        var mode = request.Query["hub.mode"].ToString();
        var verifyToken = request.Query["hub.verify_token"].ToString();
        var challenge = request.Query["hub.challenge"].ToString();

        if (string.IsNullOrWhiteSpace(options.VerifyToken))
            return Results.Problem("Facebook VerifyToken no está configurado.", statusCode: StatusCodes.Status500InternalServerError);

        if (mode == "subscribe" && verifyToken == options.VerifyToken)
            return Results.Text(challenge, "text/plain");

        return Results.Unauthorized();
    }

    private static async Task<IResult> HandleFacebookMessageAsync(
        HttpRequest request,
        IConversacionesConfigService configService,
        IConversacionesService svc,
        CancellationToken ct)
    {
        var options = await configService.GetFacebookConfigAsync(ct);
        if (string.IsNullOrWhiteSpace(options.AppSecret))
            return Results.Problem("Facebook App Secret no está configurado.", statusCode: StatusCodes.Status500InternalServerError);

        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawPayload = await reader.ReadToEndAsync(ct);
        var signature = request.Headers["X-Hub-Signature-256"].ToString();
        if (!IsValidMetaSignature(rawPayload, options.AppSecret, signature))
            return Results.Unauthorized();

        using var payload = JsonDocument.Parse(rawPayload);
        var result = await svc.RegisterIncomingFacebookWebhookAsync(new ConversacionWebhookRequest
        {
            Payload = payload,
            RawPayload = rawPayload,
            Headers = request.Headers.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToString(),
                StringComparer.OrdinalIgnoreCase)
        }, ct);

        return Results.Ok(result);
    }

    private static async Task<IResult> HandleMercadoLibreMessageAsync(
        HttpRequest request,
        IConversacionesService svc,
        CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawPayload = await reader.ReadToEndAsync(ct);
        using var payload = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawPayload) ? "{}" : rawPayload);

        var result = await svc.RegisterIncomingMercadoLibreWebhookAsync(new ConversacionWebhookRequest
        {
            Payload = payload,
            RawPayload = rawPayload,
            Headers = request.Headers.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToString(),
                StringComparer.OrdinalIgnoreCase)
        }, ct);

        return Results.Ok(result);
    }

    private static bool IsValidMetaSignature(string rawPayload, string appSecret, string signature)
    {
        const string prefix = "sha256=";
        if (string.IsNullOrWhiteSpace(rawPayload)
            || string.IsNullOrWhiteSpace(appSecret)
            || string.IsNullOrWhiteSpace(signature)
            || !signature.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var expected = Convert.FromHexString(signature[prefix.Length..]);
            var actual = HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(appSecret.Trim()),
                Encoding.UTF8.GetBytes(rawPayload));
            return expected.Length == actual.Length
                && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
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



