namespace AlfaCore.Services;

/// <summary>
/// La LicenciaPrincipal + las credenciales SQL del VB6 matchean más de una base en ALFA_CENTRAL
/// (mismo cliente, varias bases con las mismas credenciales — típicamente cuentas de prueba). El
/// endpoint la traduce a HTTP 300 (Multiple Choices) con el body listando "IdBase|Nombre" por
/// línea (una por candidata), para que ModAlfaCore.bas pueda ofrecer un selector en vez de que
/// alguien tenga que adivinar un número.
/// </summary>
public sealed class Vb6MultiplesBasesException(string message) : InvalidOperationException(message);
