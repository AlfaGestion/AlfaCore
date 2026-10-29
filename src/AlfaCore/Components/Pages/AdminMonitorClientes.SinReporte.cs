using AlfaCore.Components.Shared.AlfaDesign;
using AlfaCore.Models;

namespace AlfaCore.Components.Pages;

public partial class AdminMonitorClientes
{
    private bool _mostrarSinReporte, _cargandoSinReporte;
    private string? _errorSinReporte;
    private string _clasificacionSinReporte = string.Empty, _buscarSinReporte = string.Empty;
    private int _paginaSinReporte = 1;
    private IReadOnlyList<MonitorClienteSinReporteDto> _clientesSinReporte = [];

    private IReadOnlyList<MonitorClienteSinReporteDto> ClientesSinReporteFiltrados => _clientesSinReporte
        .Where(c => _clasificacionSinReporte.Length == 0
            || (_clasificacionSinReporte == "__sin_clasificacion" ? c.Clasificacion.Length == 0
                : _clasificacionSinReporte.Split(',').Contains(c.Clasificacion, StringComparer.OrdinalIgnoreCase)))
        .Where(c => string.IsNullOrWhiteSpace(_buscarSinReporte)
            || Contiene(c.ClienteCodigo, _buscarSinReporte.Trim()) || Contiene(c.RazonSocial, _buscarSinReporte.Trim()))
        .ToList();

    private int PaginasSinReporte => Math.Max(1, (ClientesSinReporteFiltrados.Count + 49) / 50);

    private IReadOnlyList<AlfaSelectOption> ClasificacionesSinReporte =>
        new[] { new AlfaSelectOption(string.Empty, "Todas"), new AlfaSelectOption("1,2", "P1 y P2") }
        .Concat(_clientesSinReporte.Where(c => c.Clasificacion.Length > 0)
            .GroupBy(c => c.Clasificacion, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key)
            .Select(g => new AlfaSelectOption(g.Key, TextoClasificacion(g.Key, g.First().ClasificacionDescripcion))))
        .Append(new AlfaSelectOption("__sin_clasificacion", "Sin clasificación")).ToList();

    private static string TextoClasificacion(string codigo, string descripcion) => codigo.Length == 0
        ? "Sin clasificación" : $"{(int.TryParse(codigo, out _) ? "P" : string.Empty)}{codigo}" + (descripcion.Length == 0 ? string.Empty : $" · {descripcion}");

    private async Task AbrirSinReporteAsync()
    {
        if (AppUserSession.CurrentUser?.SuperAdmin != true) return;
        _mostrarSinReporte = true;
        await CargarSinReporteAsync();
    }

    private async Task CargarSinReporteAsync()
    {
        if (_cargandoSinReporte || AppUserSession.CurrentUser?.SuperAdmin != true) return;
        _cargandoSinReporte = true;
        _errorSinReporte = null;
        _paginaSinReporte = 1;
        _clientesSinReporte = [];
        await InvokeAsync(StateHasChanged);
        try { _clientesSinReporte = await MonitorSvc.GetClientesSinReporteAsync(); }
        catch (Exception ex) { _errorSinReporte = Texto(ex); }
        finally { _cargandoSinReporte = false; await InvokeAsync(StateHasChanged); }
    }

    private void CerrarSinReporte() => _mostrarSinReporte = false;
}
