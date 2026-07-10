using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IViajePreviewStateService
{
    void Set(CargaViajePreviewDto preview);
    CargaViajePreviewDto? ConsumeFor(int viajeId);
    void Clear();
}

public sealed class ViajePreviewStateService : IViajePreviewStateService
{
    private CargaViajePreviewDto? _preview;

    public void Set(CargaViajePreviewDto preview)
    {
        _preview = preview;
    }

    public CargaViajePreviewDto? ConsumeFor(int viajeId)
    {
        if (_preview?.Viaje.Id != viajeId)
            return null;

        var preview = _preview;
        _preview = null;
        return preview;
    }

    public void Clear()
    {
        _preview = null;
    }
}
