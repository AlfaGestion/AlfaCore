namespace AlfaCore.Models;

public sealed class SessionDto
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int BaseId { get; init; }
    public string Nombre { get; set; } = string.Empty;
    public string Servidor { get; set; } = string.Empty;
    public string BaseDatos { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool TrustServerCertificate { get; set; } = true;
    public bool Activa { get; set; }

    /// <summary>
    /// Guid determinístico para el <see cref="Id"/> de una base SaaS a partir de su
    /// <see cref="BaseId"/> central. Mismo algoritmo usado por ConexionClienteService y
    /// SessionDrawer para mapear <c>BaseCentralDto</c> → <see cref="SessionDto"/>; se expone acá
    /// para que cualquier otro punto que arme una sesión "ad hoc" para una base conocida (por
    /// ejemplo, el login directo por ruta /{idweb}/{idbase} sin sesión central previa) obtenga el
    /// mismo Id que tendría esa base si se resolviera por el camino central habitual.
    /// </summary>
    public static Guid BuildGuidFromBaseId(int baseId)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.GetBytes(baseId).CopyTo(bytes);
        return new Guid(bytes);
    }
}

public sealed class SessionesData
{
    public List<SessionDto> Sessions { get; set; } = [];
}
