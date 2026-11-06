using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Historial de acciones de un comprobante (V_MV_CpteAcciones): A alta, M modificación, B baja, IM impresión / PDF.</summary>
internal static class ComprobanteAcciones
{
    internal const string Alta = "A";
    internal const string Impresion = "IM";

    internal static Task RegistrarAsync(SqlConnection cn, SqlTransaction? tx, string tc, string idComprobante, string tipo, string usuario, string proceso,
        CancellationToken ct, string comentario = "AlfaCore", int? asiento = null, string? periodo = null, string? tipoReg = null, DateTime? fecha = null, string? cuenta = null)
        => cn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.V_MV_CpteAcciones(TC,IDCOMPROBANTE,IDCOMPLEMENTO,TIPO_ACCION,FECHAHORA,USUARIO,PC,SYSTEMUSER,Periodo,Tipo_Reg,Fecha,Mes_Operativo,NUMEROASIENTO,Proceso,Cuenta)
            VALUES(@tc,@id,0,@tipo,GETDATE(),@usuario,@pc,@comentario,@periodo,@tipoReg,@fecha,@mes,@asiento,@proceso,@cuenta);
            """, new
        {
            tc = tc.Trim(), id = idComprobante.Trim(), tipo, usuario = Corto(usuario, 255), pc = Corto(Environment.MachineName, 255), comentario = Corto(comentario, 255),
            periodo, tipoReg, fecha, mes = fecha is { } f ? (int?)f.Month : null, asiento, proceso = Corto(proceso, 150), cuenta
        }, tx, commandTimeout: 30, cancellationToken: ct));

    /// <summary>Para acciones que no deben frenar la operación principal (imprimir): si no se puede registrar, se ignora.</summary>
    internal static async Task RegistrarSiPuedeAsync(SqlConnection cn, string tc, string idComprobante, string tipo, string usuario, string proceso, CancellationToken ct)
    {
        try { await RegistrarAsync(cn, null, tc, idComprobante, tipo, usuario, proceso, ct); }
        catch (SqlException) { }
    }

    private static string Corto(string? texto, int max) => (texto ?? string.Empty).Length <= max ? texto ?? string.Empty : texto![..max];

    internal static string Nombre(string tipo) => tipo.Trim().ToUpperInvariant() switch
    {
        "A" or "IN" => "Alta",
        "M" or "MO" => "Modificación",
        "B" or "BA" => "Baja",
        "IM" => "Impresión / PDF",
        "MAIL" => "Mail",
        var otro => otro.Length == 0 ? "—" : otro
    };
}
