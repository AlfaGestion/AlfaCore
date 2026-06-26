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
}

public sealed class SessionesData
{
    public List<SessionDto> Sessions { get; set; } = [];
}
