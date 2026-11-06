using System.Globalization;
using System.Text;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed partial class EstudioPaqueteService
{
    public Task<EstudioPaqueteRevision> RevisarAsync(int anio, int mes, string unidad, CancellationToken ct = default)
        => EjecutarAsync("Revisar", cn =>
        {
            var (desde, hasta) = Periodo(anio, mes);
            return RevisarEnConexionAsync(cn, desde, hasta, unidad.Trim(), ct);
        }, ct);

    private async Task<EstudioPaqueteRevision> RevisarEnConexionAsync(SqlConnection cn, DateTime desde, DateTime hasta, string unidad, CancellationToken ct)
    {
        var carpeta = arcaServidor?.Estado(sessions.GetActiveSession()?.BaseId ?? 0).Carpeta ?? "";
        var controles = new List<EstudioPaqueteControl>();
        var observaciones = new List<EstudioPaqueteObservacion>();
        foreach (var compras in new[] { false, true })
        {
            var libro = compras ? "Compras" : "Ventas";
            if (!await permissions.HasAccessAsync(compras ? LibroIvaService.MenuLibroCompras : LibroIvaService.MenuLibro, ct))
            {
                controles.Add(new(compras, "", "SIN_PERMISO", "Tu usuario no tiene permiso para revisar este libro."));
                continue;
            }
            var r = await librosIva.GetAsync(new() { Compras = compras, Desde = desde, Hasta = hasta, UnidadNegocio = unidad }, ct);
            if (r.Filas.Count == 0)
            {
                controles.Add(new(compras, "", "SIN_MOVIMIENTOS", "Sin movimientos en el libro para este período."));
                continue;
            }
            LibroIvaArcaValidator.Validar(r, (f, detalle) => observaciones.Add(new(libro, TipoObservacion(detalle),
                f?.UnidadNegocio ?? "", f?.Fecha, f?.Tc ?? "", f?.Letra ?? "", f?.PuntoVenta ?? 0, f?.Numero ?? 0,
                f?.Cuenta ?? "", f?.Cuit ?? "", f?.RazonSocial ?? "", detalle, f?.Total)));
            foreach (var grupo in r.Unidades.GroupBy(u => LibroIvaArcaValidator.Digitos(u.Cuit)))
            {
                var cuit = grupo.Key;
                var archivo = ArcaDescargaService.Listar(carpeta, cuit)
                    .FirstOrDefault(a => a.Recibidos == compras && a.Periodo == desde.ToString("yyyyMM", CultureInfo.InvariantCulture));
                if (cuit.Length != 11 || archivo is null)
                {
                    var anterior = await UltimoControlAsync(cn, compras, cuit, desde, hasta, ct);
                    controles.Add(new(compras, cuit, anterior is null ? "PENDIENTE" : "ACTUALIZAR",
                        (anterior is { } fecha ? $"Hay un control registrado el {fecha:dd/MM/yyyy HH:mm}, pero no está disponible su archivo para comprobar los datos actuales. " : "No hay un archivo del mes disponible para validar este CUIT. ") +
                        $"Abrí Control ARCA y validá Mis Comprobantes {(compras ? "Recibidos" : "Emitidos")} de {desde:MM/yyyy}."));
                    continue;
                }
                try
                {
                    var arca = ControlArcaService.Leer(await File.ReadAllBytesAsync(Path.Combine(carpeta, archivo.Nombre), ct), archivo.Nombre);
                    if (arca.Cuit != cuit || arca.Recibidos != compras || arca.Comprobantes.Any(c => c.Fecha < desde || c.Fecha >= hasta.AddDays(1)))
                        throw new InvalidOperationException("El archivo no corresponde al CUIT, libro o mes de la revisión.");
                    // Reutiliza el control oficial y lo recalcula contra la base actual; no se toma un resultado viejo como validación vigente.
                    var control = await ControlArcaService.ControlarEnConexionAsync(cn, arca, ct);
                    var novedades = control.Filas.Where(f => f.Estado != ControlArcaEstado.Coincide || f.Observacion.Length > 0).ToList();
                    foreach (var f in novedades)
                        observaciones.Add(new(libro, f.Estado == ControlArcaEstado.PosibleDuplicado ? "Posible duplicado ARCA" : "Diferencia ARCA", "", f.FechaSistema ?? f.FechaArca,
                            "", f.Letra, f.PuntoVenta, f.Numero, "", f.Cuit, f.Nombre,
                            $"{ControlArcaService.EstadoTexto(f.Estado)}. {f.Observacion}", f.TotalSistema, f.TotalArca));
                    controles.Add(new(compras, cuit, novedades.Count == 0 ? "VALIDADO" : "CON_DIFERENCIAS",
                        $"Comparado ahora con {archivo.Nombre} (archivo del {archivo.Fecha:dd/MM/yyyy HH:mm}): {novedades.Count:N0} novedad(es)." +
                        (unidad.Length > 0 ? " El control ARCA abarca todas las unidades de este CUIT." : ""), archivo.Nombre, archivo.Fecha));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    var incidente = await events.LogErrorAsync("EstudioPaquete", "RevisarArchivoArca", ex, "No se pudo revisar el archivo de Mis Comprobantes.", ct: ct);
                    controles.Add(new(compras, cuit, "ERROR", $"No se pudo revisar el archivo. Volvé a validarlo en Control ARCA. Referencia: {incidente}", archivo.Nombre, archivo.Fecha));
                }
            }
        }
        return new() { Controles = controles, Observaciones = observaciones.OrderBy(o => o.Tipo.Contains("duplicado", StringComparison.OrdinalIgnoreCase) ? 0 : o.Tipo.Contains("total") || o.Tipo.Contains("ARCA") ? 1 : 2).ToList() };
    }

    internal static string TipoObservacion(string detalle) => detalle.Contains("duplicado", StringComparison.OrdinalIgnoreCase) ? "Posible duplicado"
        : detalle.Contains("total", StringComparison.OrdinalIgnoreCase) || detalle.Contains("IVA inconsistente") ? "Diferencia de totales" : "Dato fiscal";

    private static async Task<DateTime?> UltimoControlAsync(SqlConnection cn, bool compras, string cuit, DateTime desde, DateTime hasta, CancellationToken ct)
    {
        if (cuit.Length != 11) return null;
        return await cn.ExecuteScalarAsync<DateTime?>(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.MV_Transmisiones', N'U') IS NOT NULL
                SELECT MAX(FechaFin) FROM dbo.MV_Transmisiones
                WHERE Tipo='R' AND ISNULL(Anulado,0)=0 AND FechaFin IS NOT NULL
                  AND Observaciones LIKE @Tipo
                  AND (CHARINDEX(@Cuit,ISNULL(Ubicacion_Archivo,''))>0 OR CHARINDEX(N'CUIT '+@Cuit,ISNULL(Observaciones,''))>0)
                  AND ((Fecha_Desde=@Desde AND Fecha_Hasta=@Hasta) OR CHARINDEX(@Periodo,ISNULL(Ubicacion_Archivo,''))>0);
            """, new { Tipo = $"Control ARCA Mis Comprobantes {(compras ? "Recibidos" : "Emitidos")}%", Cuit = cuit, Desde = desde, Hasta = hasta,
                Periodo = desde.ToString("yyyyMM", CultureInfo.InvariantCulture) }, cancellationToken: ct));
    }

    internal static string RevisionCsv(EstudioPaqueteRevision r)
    {
        // Las observaciones pueden contener nombres de terceros: impedir fórmulas al abrir el CSV en Excel.
        static string C(string? s)
        {
            s ??= "";
            if (s.TrimStart().StartsWith('=') || s.TrimStart().StartsWith('+') || s.TrimStart().StartsWith('-') || s.TrimStart().StartsWith('@')) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var sb = new StringBuilder("Libro;Tipo;Unidad;Fecha;TC;Letra;Punto venta;Numero;Cuenta;CUIT;Nombre;Observacion;Total sistema;Total ARCA\r\n");
        foreach (var o in r.Observaciones)
            sb.AppendLine(string.Join(";", new[] { o.Libro, o.Tipo, o.Unidad, o.Fecha?.ToString("dd/MM/yyyy"), o.Tc, o.Letra,
                o.PuntoVenta.ToString(CultureInfo.InvariantCulture), o.Numero.ToString(CultureInfo.InvariantCulture), o.Cuenta, o.Cuit, o.Nombre, o.Detalle,
                o.TotalSistema?.ToString("0.00", CultureInfo.GetCultureInfo("es-AR")), o.TotalArca?.ToString("0.00", CultureInfo.GetCultureInfo("es-AR")) }.Select(C)));
        return sb.ToString();
    }
}
