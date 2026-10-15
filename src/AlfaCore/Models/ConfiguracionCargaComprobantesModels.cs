namespace AlfaCore.Models;

public sealed record ConfiguracionCargaDefinicion(string Clave, string Titulo, string Grupo, string Tipo, string Alcance);
public sealed class ConfiguracionCargaValor
{
    public string Clave { get; set; } = "";
    public string Valor { get; set; } = "";
    public string Version { get; set; } = "";
    public bool Existe { get; set; }
}
public sealed class ConfiguracionCargaSnapshot
{
    public Guid SesionId { get; set; }
    public bool PuedeGuardar { get; set; }
    public List<ConfiguracionCargaValor> Valores { get; set; } = [];
}
public sealed record ConfiguracionCargaCambio(string Clave, string Valor, string Version);
public sealed record ConfiguracionCargaReferencia(string Clave, bool Dinamica);
