namespace AlfaCore.Models;

/// <summary>A pesar del nombre (quedó de cuando solo existía Comprobantes IA), esta clase centraliza
/// qué módulos requieren activación explícita por cliente+base incluso para clientes legacy -- hoy
/// Comprobantes IA y Conversaciones. Sin esto, GetClienteModulosAsync considera "activo" cualquier
/// módulo para un cliente legacy sin que nadie lo haya contratado explícitamente.</summary>
public static class CompraIaHabilitacion
{
    public const string CodigoModulo = "COMPROBANTES_IA";

    private static readonly HashSet<string> CodigosConActivacionExplicita = new(StringComparer.OrdinalIgnoreCase)
    {
        CodigoModulo,
        ConversacionesHabilitacion.CodigoModulo
    };

    public static bool RequiereActivacionExplicita(string? codigo)
        => codigo is not null && CodigosConActivacionExplicita.Contains(codigo.Trim());

    public static bool EstaActivo(string? estado, DateTime? pruebaVenceUtc, DateTime ahoraUtc)
        => string.Equals(estado?.Trim(), ClienteModuloEstados.Activo, StringComparison.OrdinalIgnoreCase)
           || (string.Equals(estado?.Trim(), ClienteModuloEstados.Prueba, StringComparison.OrdinalIgnoreCase)
               && pruebaVenceUtc.HasValue && pruebaVenceUtc.Value > ahoraUtc);
}
