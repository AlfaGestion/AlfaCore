using AlfaCore.Models;

namespace AlfaCore.Components.Pages;

public partial class ConciliacionTarjetas
{
    private bool _mostrarDeshacerVarias, _aceptarDeshacerVarias;
    private TarjetasVistaDeshacer? _vistaDeshacer;
    private AppUiMessage? _errorDeshacer;
    private readonly HashSet<long> _deshacerElegidos = [];
    private string _motivoDeshacerVarias = string.Empty;
    private int _paginaDeshacer = 1;
    private int PaginasDeshacer => Math.Max(1, ((_vistaDeshacer?.Vinculos.Count ?? 0) + 49) / 50);

    private async Task AbrirDeshacerVarias()
    {
        if (_busy || _disposed || _lifetime.IsCancellationRequested || _comparacion is null) return;
        _vistaDeshacer = null; _errorDeshacer = null;
        _deshacerElegidos.Clear(); _motivoDeshacerVarias = string.Empty;
        _aceptarDeshacerVarias = false; _paginaDeshacer = 1; _feedback = null;
        _mostrarDeshacerVarias = true;
        await ConsultarDeshacerVarias();
    }

    private async Task ConsultarDeshacerVarias()
    {
        if (_busy || _comparacion is null) return;
        _errorDeshacer = null;
        var importacion = _comparacion.Importacion.Id;
        await Operar(async ct =>
        {
            _vistaDeshacer = await Servicio.VinculosParaDeshacerAsync(importacion, ct);
        }, AppUiMessage.InProgress("Consultando conciliaciones…", "Se buscan los renglones vinculados de esta importación, con permisos sobre sus unidades."), "consultar-deshacer");
        if (!_disposed && _mostrarDeshacerVarias && !_lifetime.IsCancellationRequested)
        {
            if (_vistaDeshacer is null) _errorDeshacer = _feedback ?? AppUiMessage.Info("Consulta interrumpida", "No se pudo completar la consulta. Volvé a consultar para seleccionar las conciliaciones.");
            await InvokeAsync(StateHasChanged);
        }
    }

    private void ElegirDeshacer(long id, bool valor)
    {
        if (_busy || _vistaDeshacer is null) return;
        _aceptarDeshacerVarias = false;
        if (!valor) { _deshacerElegidos.Remove(id); return; }
        if (!_vistaDeshacer.Vinculos.Any(v => v.Id == id && v.PuedeDeshacer)) return;
        if (_deshacerElegidos.Count >= 1000 && !_deshacerElegidos.Contains(id))
        {
            _feedback = AppUiMessage.Info("Seleccioná por partes", "Podés deshacer hasta 1.000 conciliaciones por operación. Se conservó la selección anterior.");
            return;
        }
        _deshacerElegidos.Add(id);
    }

    private void SeleccionarTodasDeshacer()
    {
        if (_busy || _vistaDeshacer is null) return;
        var ids = _vistaDeshacer.Vinculos.Where(v => v.PuedeDeshacer).Select(v => v.Id).ToList();
        if (ids.Count > 1000)
        {
            _feedback = AppUiMessage.Info("Seleccioná por partes", "Podés deshacer hasta 1.000 conciliaciones por operación. Se conservó la selección anterior.");
            return;
        }
        _deshacerElegidos.UnionWith(ids); _aceptarDeshacerVarias = false;
    }

    private void LimpiarDeshacer()
    {
        if (_busy) return;
        _deshacerElegidos.Clear(); _aceptarDeshacerVarias = false;
    }

    private void CerrarDeshacerVarias() { if (!_busy) _mostrarDeshacerVarias = false; }

    private async Task GuardarDeshacerVarias()
    {
        if (!_aceptarDeshacerVarias || _deshacerElegidos.Count == 0 || string.IsNullOrWhiteSpace(_motivoDeshacerVarias)) return;
        _errorDeshacer = null;
        await Operar(async ct =>
        {
            if (_vistaDeshacer is null || _comparacion?.Importacion.Id != _vistaDeshacer.Importacion) return;
            var cantidad = await Servicio.DesconciliarVariasAsync(new(_vistaDeshacer.Importacion, _vistaDeshacer.Cuenta,
                _deshacerElegidos.OrderBy(id => id).ToArray(), _motivoDeshacerVarias), ct);
            _mostrarDeshacerVarias = false; _deshacerElegidos.Clear(); _vistaDeshacer = null;
            _filtro.Estado = "Pendientes";
            await CompararCore(ct); _importaciones = await Servicio.ImportacionesAsync(_verAnuladas,ct);
            _feedback = AppUiMessage.Success("Conciliaciones deshechas", $"Se anularon {cantidad} vínculos completos. Sus movimientos quedaron pendientes; se conservó el historial sin modificar asientos.");
        }, AppUiMessage.InProgress("Deshaciendo conciliaciones…", "Se validan todos los vínculos seleccionados y se anulan juntos. Esperá el resultado."));
        if (!_disposed && _mostrarDeshacerVarias && !_lifetime.IsCancellationRequested)
        {
            _errorDeshacer = _feedback;
            await InvokeAsync(StateHasChanged);
        }
    }
}
