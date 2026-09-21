namespace AlfaCore.Models;

/// <summary>Mismo criterio que CompraIaHabilitacion pero para Conversaciones: requiere activación
/// explícita por cliente (ClienteModulos) y selección de bases (ConversacionesWorkerHabilitado en
/// ALFA_CENTRAL.dbo.bases), incluso para clientes legacy -- antes Conversaciones se consideraba
/// activo automáticamente para cualquier cliente legacy, lo que hacía que los jobs de fondo
/// intentaran conectarse a bases dadas de baja hace tiempo.</summary>
public static class ConversacionesHabilitacion
{
    public const string CodigoModulo = "CONVERSACIONES";
}
