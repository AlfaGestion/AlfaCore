namespace AlfaCore.Models;

public sealed class CatalogoPedidoEmailRequestDto
{
    public CatalogoPedidoResultDto Pedido { get; set; } = new();
    public string EmailDestino { get; set; } = string.Empty;
    public string NombreEmpresa { get; set; } = string.Empty;
    public string? LogoUrlAbsoluta { get; set; }
    public IReadOnlyDictionary<string, string> ImagenesPorArticulo { get; set; } = new Dictionary<string, string>();
}

public sealed class CatalogoPedidoEmailResultDto
{
    public bool Enviado { get; set; }
    public string? MensajeError { get; set; }
}
