namespace AlfaCore.Models;

public static class CompraIaHabilitacion
{
    public const string CodigoModulo = "COMPROBANTES_IA";

    public static bool RequiereActivacionExplicita(string? codigo)
        => string.Equals(codigo?.Trim(), CodigoModulo, StringComparison.OrdinalIgnoreCase);

    public static bool EstaActivo(string? estado, DateTime? pruebaVenceUtc, DateTime ahoraUtc)
        => string.Equals(estado?.Trim(), ClienteModuloEstados.Activo, StringComparison.OrdinalIgnoreCase)
           || (string.Equals(estado?.Trim(), ClienteModuloEstados.Prueba, StringComparison.OrdinalIgnoreCase)
               && pruebaVenceUtc.HasValue && pruebaVenceUtc.Value > ahoraUtc);
}
