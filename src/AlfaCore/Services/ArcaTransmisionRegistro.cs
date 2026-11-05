using System.Data;
using System.Globalization;
using System.Text;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>
/// Registro de las importaciones y controles contra ARCA en las mismas tablas que usaba EM_ImportacionComprobantes del legacy:
/// MV_Transmisiones (una fila por corrida, Tipo 'R') y AUX_MV_CTROL_CPTE (una fila por comprobante, con los mismos mensajes).
/// El número de transmisión sale igual que en el legacy (máximo entre MV_Transmisiones del tipo y MV_ASIENTOS, más uno) y se
/// graba en MV_ASIENTOS, C_MV_Cpte y MA_CUENTAS para saber qué altas trajo cada corrida.
/// </summary>
internal static class ArcaTransmisionRegistro
{
    internal const string Tipo = "R";
    internal const string ObservacionRecibidos = "Compras ARCA Mis Comprobantes Recibidos";
    internal const string MensajeAlta = "CORRECTO - SE DIO DE ALTA EN EL SISTEMA";
    internal const string MensajeFaltaEnSistema = "EXISTE EN ARCHIVO NO EN SISTEMA";
    internal const string MensajeFaltaEnArca = "EXISTE EN SISTEMA Y NO EN ARCHIVO";
    internal const string MensajeDifiereImporte = "ERROR, DIFIERE IMPORTES";

    /// <summary>Reserva el número y deja la cabecera (como el legacy: «doy de alta la transmisión para reservar el nro»).</summary>
    internal static async Task<int> AbrirAsync(SqlConnection cn, string usuario, string archivo, string observaciones,
        DateTime? desde, DateTime? hasta, int enviados, CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        CommandDefinition Q(string sql, object? args = null) => new(sql, args, tx, commandTimeout: 60, cancellationToken: ct);
        if (await cn.ExecuteScalarAsync<int>(Q("""
            DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'ALFACORE_TRANSMISION',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; SELECT @r;
            """)) < 0) throw new InvalidOperationException("Hay otra importación reservando su número. Reintentá en unos segundos.");
        var numero = await cn.ExecuteScalarAsync<int>(Q("""
            SELECT CASE WHEN a.n > t.n THEN a.n ELSE t.n END
            FROM (SELECT ISNULL(MAX(Transmision),0)+1 AS n FROM dbo.MV_Transmisiones WHERE Tipo=@tipo) t
            CROSS JOIN (SELECT ISNULL(MAX(Transmision),0)+1 AS n FROM dbo.MV_ASIENTOS) a;
            """, new { tipo = Tipo }));
        await cn.ExecuteAsync(Q("""
            INSERT dbo.MV_Transmisiones(Tipo,Transmision,Fecha,Hora,Fecha_Desde,Fecha_Hasta,Registros_enviados,Registros_Recibidos,Responsable_Recepcion,
              Ubicacion_Archivo,Anulado,Observaciones)
            VALUES(@tipo,@numero,CONVERT(datetime,CONVERT(varchar(8),GETDATE(),112)),CONVERT(nvarchar(5),GETDATE(),108),@desde,@hasta,@enviados,0,@usuario,@archivo,0,@observaciones);
            """, new { tipo = Tipo, numero, desde, hasta, enviados, usuario = Corto(usuario, 50), archivo = Corto(archivo, 100), observaciones = Corto(observaciones, 250) }));
        await tx.CommitAsync(ct);
        return numero;
    }

    internal static Task CerrarAsync(SqlConnection cn, int transmision, int recibidos, string log, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.MV_Transmisiones SET Registros_Recibidos=@recibidos, FechaFin=GETDATE(), [LOG]=@log WHERE Tipo=@tipo AND Transmision=@transmision;
            """, new { tipo = Tipo, transmision, recibidos, log }, cancellationToken: ct));

    internal static Task GrabarControlAsync(SqlConnection cn, int transmision, string tc, string sucursal, string numero, string letra,
        decimal importe, string cuenta, DateTime? fecha, string nombre, string mensaje, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            INSERT dbo.AUX_MV_CTROL_CPTE(TC,SUCURSAL,NUMERO,LETRA,IMPORTE,CUENTA,NOMBRE,FECHA,MENSAJE,TRANSMISION)
            VALUES(@tc,@sucursal,@numero,@letra,@importe,@cuenta,@nombre,@fecha,@mensaje,@transmision);
            """, new
        {
            tc = Corto(tc, 4), sucursal = Corto(sucursal, 4), numero = Corto(numero, 8), letra = Corto(letra, 1), importe, cuenta = Corto(cuenta, 15),
            nombre = Corto(nombre, 150), fecha, mensaje = Corto(mensaje, 250), transmision
        }, cancellationToken: ct));

    /// <summary>Marca con la transmisión a los proveedores que se dieron de alta durante la importación (MA_CUENTAS.Transmision).</summary>
    internal static Task MarcarCuentaAsync(SqlConnection cn, int transmision, string codigo, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("UPDATE dbo.MA_CUENTAS SET Transmision=@transmision WHERE LTRIM(RTRIM(CODIGO))=@codigo AND Transmision IS NULL;",
            new { transmision, codigo = codigo.Trim() }, cancellationToken: ct));

    /// <summary>Mensaje del legacy (AUX_MV_CTROL_CPTE.MENSAJE) para una fila de la comparación; null si coincide (no se registra).</summary>
    internal static string? MensajeControl(ControlArcaEstado estado) => estado switch
    {
        ControlArcaEstado.FaltaEnSistema => MensajeFaltaEnSistema,
        ControlArcaEstado.FaltaEnArca => MensajeFaltaEnArca,
        ControlArcaEstado.DifiereImporte => MensajeDifiereImporte,
        ControlArcaEstado.DifiereFecha => "ERROR, DIFIERE FECHA",
        ControlArcaEstado.OtroPeriodo => "EXISTE EN SISTEMA EN OTRO PERIODO",
        _ => null
    };

    /// <summary>Registra una comparación (la descarga automática diaria): cabecera y una fila por cada comprobante que no coincide.</summary>
    internal static async Task<int> RegistrarControlAsync(SqlConnection cn, string usuario, string archivo, ControlArcaResultado r, CancellationToken ct)
    {
        var tipo = r.Recibidos ? "Recibidos" : "Emitidos";
        var transmision = await AbrirAsync(cn, usuario, archivo, $"Control ARCA Mis Comprobantes {tipo} (automático)", r.Desde, r.Hasta, r.ComprobantesArca, ct);
        var registros = 0;
        foreach (var f in r.Filas)
        {
            if (MensajeControl(f.Estado) is not { } mensaje) continue;
            var tc = r.Recibidos ? ControlArcaImportacionService.Tc(f.Clase) : f.Clase + "V";
            await GrabarControlAsync(cn, transmision, tc, f.PuntoVenta.ToString("0000", CultureInfo.InvariantCulture), f.Numero.ToString("00000000", CultureInfo.InvariantCulture),
                f.Letra, f.TotalArca ?? f.TotalSistema ?? 0, "", f.FechaArca ?? f.FechaSistema, f.Nombre, mensaje, ct);
            registros++;
        }
        var log = new StringBuilder($"ARCA {r.ComprobantesArca} · sistema {r.ComprobantesSistema} · coinciden {r.Cantidad(ControlArcaEstado.Coincide)}");
        foreach (var e in new[] { ControlArcaEstado.FaltaEnSistema, ControlArcaEstado.FaltaEnArca, ControlArcaEstado.DifiereImporte, ControlArcaEstado.DifiereFecha, ControlArcaEstado.OtroPeriodo })
            if (r.Cantidad(e) > 0) log.Append($" · {MensajeControl(e)}: {r.Cantidad(e)}");
        await CerrarAsync(cn, transmision, 0, log.ToString(), ct);
        return transmision;
    }

    private static string Corto(string? s, int n) { s = (s ?? "").Trim(); return s.Length > n ? s[..n] : s; }
}
