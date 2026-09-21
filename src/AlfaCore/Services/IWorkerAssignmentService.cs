namespace AlfaCore.Services;

/// <summary>Coordina qué servidor físico corre los workers en segundo plano cuando AlfaCore está
/// desplegado en más de un web service (para no depender de uno solo). Todas las instancias siguen
/// consultando la misma ALFA_CENTRAL, así que sin esta coordinación cada una procesaría las mismas
/// bases en paralelo. Ver dbo.ConfiguracionCentral / clave WORKERS_SERVIDOR_ACTIVO.</summary>
public interface IWorkerAssignmentService
{
    /// <summary>Identificador de este proceso: AlfaCore:NombreServidor si está configurado, si no
    /// Environment.MachineName.</summary>
    string NombreServidorLocal { get; }

    /// <summary>Servidor designado como único dueño de los workers, o null/vacío si no hay
    /// restricción (todas las instancias corren los workers).</summary>
    Task<string?> GetServidorActivoAsync(CancellationToken ct = default);

    /// <summary>True si este proceso debe ejecutar los workers en segundo plano en este ciclo.</summary>
    Task<bool> DebeEjecutarWorkersAsync(CancellationToken ct = default);

    /// <summary>Solo superadmin central. Un valor vacío/null quita la restricción.</summary>
    Task SetServidorActivoAsync(string? servidor, CancellationToken ct = default);
}
