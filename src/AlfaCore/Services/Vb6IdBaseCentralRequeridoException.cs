namespace AlfaCore.Services;

/// <summary>
/// El VB6 no mandó (o mandó vacío) el idBaseCentral que Vb6BridgeService necesita para resolver
/// el cliente en modo SaaS. Se distingue de otros InvalidOperationException del bridge porque el
/// endpoint la traduce a un HTTP 428 propio, para que ModAlfaCore.bas la reconozca sin tener que
/// parsear el texto del mensaje y pueda ofrecerle al usuario cargar el dato con un InputBox.
/// </summary>
public sealed class Vb6IdBaseCentralRequeridoException(string message) : InvalidOperationException(message);
