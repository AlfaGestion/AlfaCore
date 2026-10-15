namespace AlfaCore.Services;

/// <summary>Condición funcional recuperable: conserva la carga y permite corregirla.</summary>
public sealed class CargaComprobantesReglaException(string message) : InvalidOperationException(message);
