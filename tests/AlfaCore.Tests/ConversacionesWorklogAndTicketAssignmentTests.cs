using System.Reflection;
using AlfaCore.Models;
using AlfaCore.Services;
using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// Cubre dos flujos disparados desde Conversaciones.razor:
///
/// 1) Partes de horas (<c>SaveParteHorasAsync</c> -> <c>PartesHorasSvc.SaveAsync</c> ->
///    <c>dbo.ALFACORE_PARTES_HORAS</c>). El código confirma que <c>IdTecnico</c> ya es NULLABLE
///    en esa tabla (<c>DbNullable(normalized.IdTecnico)</c> en el INSERT/UPDATE de
///    PartesHorasService) y que <c>PartesHorasService.Validate</c> sólo exige "técnico O usuario"
///    (no "técnico Y usuario") -- un usuario logueado sin Técnico asociado ya puede guardar un
///    parte de horas con <c>IdTecnico</c> vacío, porque <c>Usuario</c> (identidad real del
///    usuario, vía <c>CurrentUserNameForAudit</c>) alcanza. La UI en Conversaciones.razor
///    (línea ~3459) ya ofrece un selector "Sin técnico" + lista de <c>_technicians</c> (la misma
///    lista tenant-filtrada por <c>GetTechniciansAsync</c>/<c>GetConnectionStringForExpectedTenant</c>
///    que usa el resto de la página) para cada línea del parte. No hizo falta ningún cambio de
///    código para esta parte: se documenta el comportamiento existente con tests reales sobre la
///    lógica pura (sin SQL) de <c>PartesHorasService</c>.
///
/// 2) Tickets desde Conversaciones (<c>CreateTicketFromSelectionAsync</c> ->
///    <c>TicketsSvc.CreateAsync</c> -> <c>dbo.TICK_TICKETS</c>). Antes del fix, el método
///    bloqueaba la creación si <c>CurrentMessageTechnicianId</c> (el Técnico del usuario logueado)
///    estaba vacío, aun cuando <c>TICK_TICKETS.IdTecnico</c> ya es nullable y el modelo ya separa
///    "creador" (<c>UsuarioAlta</c>, poblado desde <c>UsuarioAccion</c>) de "responsable"
///    (<c>IdTecnico</c>, poblado desde <c>_ticketAssignedTechnicianId</c>, con "Sin técnico" ya
///    disponible como opción en el selector). El fix quita ambos bloqueos artificiales; estos
///    tests son estructurales (leen el código fuente), igual que
///    <see cref="ConversacionesAutomationPipelineTests"/>, porque tanto Conversaciones.razor como
///    TicketsService abren su propio SqlConnection contra la base del tenant y no son inyectables
///    sin un refactor desproporcionado para esta tarea.
/// </summary>
public sealed class ConversacionesWorklogAndTicketAssignmentTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string ConversacionesRazorSource = File.ReadAllText(
        Path.Combine(RepositoryRoot, "src", "AlfaCore", "Components", "Pages", "Conversaciones.razor"));

    private static readonly string TicketsServiceSource = File.ReadAllText(
        Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "TicketsService.cs"));

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AlfaCore.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio (AlfaCore.sln).");
    }

    // ---------------------------------------------------------------------------------------
    // Partes de horas: lógica pura de PartesHorasService.Validate / Normalize por reflexión.
    // ---------------------------------------------------------------------------------------

    private static void InvokeValidate(ParteHoraSaveRequest request)
    {
        var method = typeof(PartesHorasService).GetMethod("Validate", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(PartesHorasService), "Validate");
        try
        {
            method.Invoke(null, [request]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static ParteHoraSaveRequest InvokeNormalize(ParteHoraSaveRequest request)
    {
        var method = typeof(PartesHorasService).GetMethod("Normalize", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(PartesHorasService), "Normalize");
        return (ParteHoraSaveRequest)method.Invoke(null, [request])!;
    }

    private static ParteHoraSaveRequest ValidRequest(string idTecnico, string usuario) => new()
    {
        Fecha = DateTime.Today,
        ClienteCodigo = "CLI001",
        IdTecnico = idTecnico,
        Usuario = usuario,
        TipoTrabajo = ParteHoraTipoTrabajoKeys.Soporte,
        Minutos = 60,
        Descripcion = "Tarea de prueba",
        UsuarioAccion = usuario
    };

    [Fact]
    public void Validate_UsuarioConTecnico_NoRompeNiExigeCambios()
    {
        // No-regresión: comportamiento sin cambios para un usuario que sí tiene Técnico asociado.
        var request = ValidRequest(idTecnico: "TEC01", usuario: "jperez");
        var exception = Record.Exception(() => InvokeValidate(request));
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_UsuarioSinTecnico_PeroConUsuarioIdentificado_NoBloquea()
    {
        // Un usuario sin Técnico asociado (IdTecnico vacío) puede guardar el parte de horas: la
        // identidad real es "Usuario" (CurrentUserNameForAudit), que ya está presente. IdTecnico
        // NO es obligatorio a nivel funcional -- confirma que la tabla ya soporta esta semántica.
        var request = ValidRequest(idTecnico: "", usuario: "adminBackoffice");
        var exception = Record.Exception(() => InvokeValidate(request));
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_UsuarioSinTecnicoYSinUsuario_Rechaza()
    {
        // Si NO hay ni Técnico ni Usuario identificado, no hay forma de saber quién trabajó: esto
        // sigue bloqueado (y debe seguir así) para no persistir un parte de horas huérfano.
        var request = ValidRequest(idTecnico: "", usuario: "");
        var exception = Record.Exception(() => InvokeValidate(request));
        var validationException = Assert.IsType<AppValidationException>(exception);
        Assert.Contains("técnico o usuario", validationException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_NoInventaNiAsociaTecnicoCuandoVacio()
    {
        // Normalize sólo debe trim/upper -- nunca debe rellenar un IdTecnico "ficticio" ni tocar
        // la asociación Usuario-Técnico. Un IdTecnico vacío debe seguir vacío después de Normalize.
        var request = ValidRequest(idTecnico: "  ", usuario: "adminBackoffice");
        var normalized = InvokeNormalize(request);
        Assert.Equal(string.Empty, normalized.IdTecnico);
        Assert.Equal("adminBackoffice", normalized.Usuario);
    }

    [Fact]
    public void Normalize_ConTecnicoValido_TrimeaYNormalizaSinAlterarIdentidad()
    {
        var request = ValidRequest(idTecnico: " tec01 ", usuario: "jperez");
        var normalized = InvokeNormalize(request);
        Assert.Equal("TEC01", normalized.IdTecnico);
        Assert.Equal("jperez", normalized.Usuario);
    }

    [Fact]
    public void PartesHorasService_PersistIdTecnicoAsNullable_NoNotNullWorkaround()
    {
        // Caracterización: el INSERT/UPDATE de ALFACORE_PARTES_HORAS ya pasa IdTecnico por
        // DbNullable (columna nullable). Si esto cambiara a un valor forzado no-nulo, sería una
        // señal de que se está inventando un IdTecnico artificial para evitar un NOT NULL.
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AlfaCore", "Services", "PartesHorasService.cs"));
        Assert.Contains("IdTecnico = DbNullable(normalized.IdTecnico)", source);
    }

    // ---------------------------------------------------------------------------------------
    // Tickets desde Conversaciones: tests estructurales sobre Conversaciones.razor y TicketsService.
    // ---------------------------------------------------------------------------------------

    private static string ExtractMethodBody(string source, string methodSignature)
    {
        var start = source.IndexOf(methodSignature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No se encontró el método '{methodSignature}'.");
        var braceStart = source.IndexOf('{', start);
        var depth = 0;
        var i = braceStart;
        for (; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) break;
            }
        }
        return source[braceStart..(i + 1)];
    }

    [Fact]
    public void CreateTicketFromSelectionAsync_NoBloqueaPorFaltaDeTecnicoDelCreador()
    {
        var body = ExtractMethodBody(ConversacionesRazorSource, "private async Task CreateTicketFromSelectionAsync()");
        Assert.DoesNotContain("no tiene un técnico asociado", body);
    }

    [Fact]
    public void CreateTicketFromSelectionAsync_NoExigeSeleccionarTecnicoResponsable()
    {
        var body = ExtractMethodBody(ConversacionesRazorSource, "private async Task CreateTicketFromSelectionAsync()");
        Assert.DoesNotContain("Seleccioná a qué técnico se asigna el ticket", body);
    }

    [Fact]
    public void CreateTicketFromSelectionAsync_CreadorViajaPorUsuarioAccion_IndependienteDelTecnico()
    {
        var body = ExtractMethodBody(ConversacionesRazorSource, "private async Task CreateTicketFromSelectionAsync()");
        // El creador (UsuarioAccion) y el responsable (IdTecnico) se envían como campos distintos.
        Assert.Contains("UsuarioAccion = CurrentUserNameForAudit", body);
        Assert.Contains("IdTecnico = _ticketAssignedTechnicianId", body);
    }

    [Fact]
    public void TicketAssignedTechnicianSelect_OfreceOpcionSinTecnico()
    {
        // El selector "Asignado a" ya permite dejar el ticket sin responsable; esto tiene que
        // seguir siendo una opción válida (no forzada) para no romper la independencia
        // creador/responsable.
        Assert.Contains("""<select @bind="_ticketAssignedTechnicianId" disabled="@_creatingTicket">""", ConversacionesRazorSource);
        var selectIndex = ConversacionesRazorSource.IndexOf(
            """<select @bind="_ticketAssignedTechnicianId" disabled="@_creatingTicket">""",
            StringComparison.Ordinal);
        var closeIndex = ConversacionesRazorSource.IndexOf("</select>", selectIndex, StringComparison.Ordinal);
        var selectMarkup = ConversacionesRazorSource[selectIndex..closeIndex];
        Assert.Contains("""<option value="">Sin técnico</option>""", selectMarkup);
    }

    [Fact]
    public void TicketsService_CreateAsync_SepaRaIdTecnicoDeUsuarioAlta()
    {
        // Caracterización del esquema: TICK_TICKETS ya tiene columnas separadas para el técnico
        // responsable (IdTecnico, nullable vía DbNullable) y el creador (UsuarioAlta, poblado
        // desde @UsuarioAccion, siempre presente para un usuario logueado autorizado).
        Assert.Contains("""cmd.Parameters.AddWithValue("@IdTecnico", DbNullable(request.IdTecnico));""", TicketsServiceSource);
        Assert.Contains("""cmd.Parameters.AddWithValue("@UsuarioAlta", user);""", TicketsServiceSource);
        var insertIndex = TicketsServiceSource.IndexOf("INSERT INTO dbo.TICK_TICKETS", StringComparison.Ordinal);
        Assert.True(insertIndex >= 0);
        var insertSnippet = TicketsServiceSource.Substring(insertIndex, 400);
        Assert.Contains("IdTecnico", insertSnippet);
        Assert.Contains("UsuarioAlta", insertSnippet);
    }

    [Fact]
    public void ConversacionEventoInterno_TecnicoAutorSiguePudiendoQuedarNulo()
    {
        // No-regresión: los eventos internos (p.ej. "guardó el parte de horas...",
        // "creó el ticket...") ya toleran IdTecnicoAutor vacío/nulo vía
        // ResolveTechnicianIdOrNullAsync -- no dependen de que el usuario tenga Técnico.
        var servicePath = Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs");
        var source = File.ReadAllText(servicePath);
        Assert.Contains("ResolveTechnicianIdOrNullAsync(idTecnicoAutor, ct)", source);
    }
}
