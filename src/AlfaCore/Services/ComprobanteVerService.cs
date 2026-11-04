namespace AlfaCore.Services;

/// <summary>
/// Abre el detalle de un comprobante en un único diálogo por sesión (ComprobanteVerHost, en el layout).
/// Evita que cada fila de un listado cree su propio diálogo: el Libro diario puede tener miles de botones "Ver".
/// </summary>
public sealed class ComprobanteVerService
{
    public sealed record Pedido(string Tc, string Comprobante, int IdComplemento);

    public Pedido? Actual { get; private set; }
    public bool HayHost => Changed is not null;
    public event Action? Changed;

    public void Abrir(string tc, string comprobante, int idComplemento = 0)
    {
        Actual = new Pedido(tc.Trim(), comprobante.Trim(), idComplemento);
        Changed?.Invoke();
    }

    public void Cerrar()
    {
        Actual = null;
        Changed?.Invoke();
    }
}
