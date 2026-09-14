using AlfaCore.Models;

namespace AlfaCore.Services;

public interface IArcaQrService
{
    byte[] GeneratePng(ArcaQrData data);
}
