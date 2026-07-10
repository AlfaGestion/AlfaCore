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

        DotEnvLoader.LoadIfPresent(builder.Environment.ContentRootPath);
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
        builder.Services.AddScoped<INotificacionesPushService, NotificacionesPushService>();
        builder.Services.AddScoped<ICalendarioService, CalendarioService>();
        builder.Services.AddScoped<IReunionesPublicasService, ReunionesPublicasService>();
        builder.Services.AddScoped<ITicketsService, TicketsService>();
        builder.Services.AddScoped<IPartesHorasService, PartesHorasService>();
        builder.Services.AddScoped<ITareasService, TareasService>();
        builder.Services.AddScoped<IInterfacesService, InterfacesService>();
        builder.Services.AddScoped<IInterfacesConfigService, InterfacesConfigService>();
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
        builder.Services.AddSingleton<UsuariosPasswordCodec>();
        builder.Services.AddSingleton<Vb6BridgeTicketStore>();
        builder.Services.AddScoped<IVb6BridgeService, Vb6BridgeService>();
        builder.Services.AddScoped<IAuditoriaService, AuditoriaService>();
        builder.Services.AddScoped<IGestionDashboardService, GestionDashboardService>();
        builder.Services.AddScoped<IPuntoVentaService, PuntoVentaService>();
        builder.Services.AddScoped<IPuntoVentaCartStateService, PuntoVentaCartStateService>();
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
        builder.Services.AddSingleton<CargaViajesLiquidacionExcelExporter>();
        builder.Services.AddSingleton<CargaViajesTarifasExcelExporter>();
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

        app.MapGet("/carga-viajes/liquidacion/descargar-excel", async (
            HttpRequest request,
            ICargaViajesService cargaViajesSvc,
            CargaViajesLiquidacionExcelExporter exporter,
            CancellationToken ct) =>
        {
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
        });

        async Task<IResult> DescargarTarifasExcel(
            HttpRequest request,
            ICargaViajesService cargaViajesSvc,
            CargaViajesTarifasExcelExporter exporter,
            CancellationToken ct)
        {
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
            var idBaseRaw = request.Query["idBase"].ToString();
            var idBase = int.TryParse(idBaseRaw, out var parsedIdBase) && parsedIdBase > 0
                ? parsedIdBase
                : (int?)null;
            var download = string.Equals(request.Query["download"].ToString(), "1", StringComparison.OrdinalIgnoreCase);
            var preview = string.Equals(request.Query["preview"].ToString(), "1", StringComparison.OrdinalIgnoreCase);
            var adjunto = await svc.GetAttachmentForServeAsync(idAdjunto, idBase, includeDownloadName: download, ct);
            if (adjunto is null || !File.Exists(adjunto.RutaLocal))
                return Results.NotFound();

            var mime = NormalizeAttachmentMime(adjunto.MimeType, adjunto.NombreArchivo);
            var fileInfo = new FileInfo(adjunto.RutaLocal);
            var lastModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero);
            var entityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{fileInfo.Length:x}-{fileInfo.LastWriteTimeUtc.Ticks:x}\"");

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

            return Results.File(
                adjunto.RutaLocal,
                contentType: mime,
                fileDownloadName: download ? (string.IsNullOrWhiteSpace(adjunto.NombreDescarga) ? adjunto.NombreArchivo : adjunto.NombreDescarga) : null,
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



