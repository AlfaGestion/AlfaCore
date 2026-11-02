using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Alta, edición y baja de unidades de negocio (Archivos → Tablas), igual que Tablas.frm del VB6.</summary>
public interface IUnidadesNegocioService
{
    Task<IReadOnlyList<UnidadNegocioFila>> ListarAsync(CancellationToken ct = default);
    Task<UnidadNegocioEdicion> GetAsync(string codigo, CancellationToken ct = default);
    Task<UnidadNegocioCatalogos> GetCatalogosAsync(CancellationToken ct = default);
    Task GuardarAsync(UnidadNegocioEdicion unidad, bool esNueva, CancellationToken ct = default);
    Task EliminarAsync(string codigo, CancellationToken ct = default);
}

public sealed class UnidadesNegocioService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents) : IUnidadesNegocioService
{
    private const string Modulo = "Tablas";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        return cn;
    }

    public async Task<IReadOnlyList<UnidadNegocioFila>> ListarAsync(CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ct);
        var filas = await cn.QueryAsync<(string Codigo, string Descripcion, bool UsaEfc, string Cuit, string Pdv, string Iva)>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(u.Codigo)), ISNULL(LTRIM(RTRIM(u.Descripcion)),''), ISNULL(u.USAEFC,0), ISNULL(LTRIM(RTRIM(u.CUIT)),''),
                   ISNULL(LTRIM(RTRIM(u.PDV)),''), ISNULL(LTRIM(RTRIM(i.DESCRIPCION)),'')
            FROM dbo.V_TA_UnidadNegocio u
            LEFT JOIN dbo.TA_CONFIGURACION c ON UPPER(LTRIM(RTRIM(c.CLAVE))) = N'CONDICIONIVA_UN_' + LTRIM(RTRIM(u.Codigo))
            LEFT JOIN dbo.TA_CONDIVA i ON LTRIM(RTRIM(CONVERT(nvarchar(20), i.CODIGO))) = LTRIM(RTRIM(c.VALOR))
            ORDER BY CASE WHEN ISNUMERIC(u.Codigo) = 1 THEN 0 ELSE 1 END, RIGHT(REPLICATE(' ', 8) + LTRIM(RTRIM(u.Codigo)), 8);
            """, cancellationToken: ct));
        return filas.Select(f => new UnidadNegocioFila(f.Codigo, f.Descripcion, f.UsaEfc, f.Cuit, f.Pdv, f.Iva)).ToList();
    }

    public async Task<UnidadNegocioEdicion> GetAsync(string codigo, CancellationToken ct = default)
    {
        codigo = (codigo ?? string.Empty).Trim();
        await using var cn = await AbrirAsync(ct);
        var u = await cn.QuerySingleOrDefaultAsync<UnidadNegocioEdicion>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(Codigo)) Codigo, ISNULL(LTRIM(RTRIM(Descripcion)),'') Descripcion,
                   ISNULL(LTRIM(RTRIM(DepositosAsociados)),'') Deposito, ISNULL(LTRIM(RTRIM(IdDepositoDif)),'') DepositoDiferencias,
                   ISNULL(LTRIM(RTRIM(MotivoParaDiferencias)),'') MotivoDiferencias, ISNULL(ModificaDiferencias,0) ModificaDiferencias,
                   ISNULL(CONVERT(nvarchar(20), SUCURSALCUENTA),'') CuentaAsociada, ISNULL(LTRIM(RTRIM(GRUPO)),'') Grupo,
                   ISNULL(USAEFC,0) UsaFacturacionElectronica, ISNULL(LTRIM(RTRIM(CUIT)),'') Cuit, ISNULL(LTRIM(RTRIM(RAZON_SOCIAL)),'') RazonSocial,
                   ISNULL(LTRIM(RTRIM(PDV)),'') PuntoVenta, ISNULL(LTRIM(RTRIM(RUTA_CRT)),'') RutaCrt, ISNULL(LTRIM(RTRIM(RUTA_KEY)),'') RutaKey,
                   ISNULL(LTRIM(RTRIM(RUTA_REPORTE)),'') RutaReportes
            FROM dbo.V_TA_UnidadNegocio WHERE LTRIM(RTRIM(Codigo)) = @Codigo;
            """, new { Codigo = codigo }, cancellationToken: ct))
            ?? throw new AppUserFacingException($"No se encontró la unidad de negocio '{codigo}'.", "UNEGOCIO_NO_EXISTE");

        var cfg = (await cn.QueryAsync<(string Clave, string Valor, string Aux)>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))), ISNULL(LTRIM(RTRIM(VALOR)),''), ISNULL(CONVERT(nvarchar(max), ValorAux),'')
            FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN @Claves;
            """, new { Claves = Claves(codigo).Values.ToArray() }, cancellationToken: ct)))
            .ToDictionary(x => x.Clave, x => x, StringComparer.OrdinalIgnoreCase);
        string Valor(string campo) => cfg.TryGetValue(Claves(codigo)[campo], out var v) ? v.Valor : string.Empty;

        u.Domicilio = Valor("Domicilio");
        u.CondicionIva = Valor("CondicionIva");
        u.InicioActividades = Valor("InicioActividades");
        u.IngresosBrutos = Valor("IngresosBrutos");
        // La forma de pago va en ValorAux (CfgAux del VB6); se acepta VALOR por si quedó ahí.
        u.FormaDePago = cfg.TryGetValue(Claves(codigo)["FormaDePago"], out var fp) ? (fp.Aux.Length > 0 ? fp.Aux : fp.Valor) : string.Empty;
        u.AgenteRetencionPercepcionIibb = Valor("AgenteRetPerc").Equals("SI", StringComparison.OrdinalIgnoreCase);
        u.AgenteRetencionGanancias = Valor("AgenteRetGan").Equals("SI", StringComparison.OrdinalIgnoreCase);
        return u;
    }

    public async Task<UnidadNegocioCatalogos> GetCatalogosAsync(CancellationToken ct = default)
    {
        await using var cn = await AbrirAsync(ct);
        async Task<IReadOnlyList<UnidadNegocioOpcion>> Lista(string tabla, string sql)
        {
            if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT CASE WHEN OBJECT_ID(@T) IS NULL THEN 0 ELSE 1 END;", new { T = tabla }, cancellationToken: ct)) == 0)
                return [];
            return (await cn.QueryAsync<UnidadNegocioOpcion>(new CommandDefinition(sql, cancellationToken: ct))).ToList();
        }
        return new UnidadNegocioCatalogos
        {
            Depositos = await Lista("dbo.V_TA_DEPOSITO",
                "SELECT LTRIM(RTRIM(IdDeposito)) Codigo, ISNULL(LTRIM(RTRIM(Descripcion)),'') Descripcion FROM dbo.V_TA_DEPOSITO ORDER BY Descripcion;"),
            MotivosStock = await Lista("dbo.V_TA_MotivoStock",
                "SELECT LTRIM(RTRIM(IdMotivoStock)) Codigo, ISNULL(LTRIM(RTRIM(Descripcion)),'') Descripcion FROM dbo.V_TA_MotivoStock ORDER BY Descripcion;"),
            CondicionesIva = await Lista("dbo.TA_CONDIVA",
                "SELECT LTRIM(RTRIM(CONVERT(nvarchar(20), CODIGO))) Codigo, ISNULL(LTRIM(RTRIM(DESCRIPCION)),'') Descripcion FROM dbo.TA_CONDIVA ORDER BY DESCRIPCION;"),
            Grupos = (await cn.QueryAsync<string>(new CommandDefinition(
                "SELECT DISTINCT LTRIM(RTRIM(GRUPO)) FROM dbo.V_TA_UnidadNegocio WHERE LTRIM(RTRIM(ISNULL(GRUPO,''))) NOT IN ('','0') ORDER BY 1;", cancellationToken: ct))).ToList()
        };
    }

    public async Task GuardarAsync(UnidadNegocioEdicion unidad, bool esNueva, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(unidad);
        var codigo = (unidad.Codigo ?? string.Empty).Trim();
        if (codigo.Length == 0) throw new AppUserFacingException("El código es obligatorio.", "UNEGOCIO_CODIGO");
        if (codigo.Length > 8) throw new AppUserFacingException("El código admite hasta 8 caracteres.", "UNEGOCIO_CODIGO");
        if (string.IsNullOrWhiteSpace(unidad.Descripcion)) throw new AppUserFacingException("La descripción es obligatoria.", "UNEGOCIO_DESCRIPCION");
        var cuit = new string((unidad.Cuit ?? string.Empty).Where(char.IsDigit).ToArray());
        if (cuit.Length is > 0 and not 11) throw new AppUserFacingException("El CUIT debe tener 11 dígitos.", "UNEGOCIO_CUIT");
        if (unidad.UsaFacturacionElectronica && (cuit.Length == 0 || string.IsNullOrWhiteSpace(unidad.RazonSocial) || string.IsNullOrWhiteSpace(unidad.PuntoVenta)))
            throw new AppUserFacingException("Para usar factura electrónica la unidad necesita CUIT, razón social y punto de venta.", "UNEGOCIO_EFC");
        int? cuenta = null;
        if (!string.IsNullOrWhiteSpace(unidad.CuentaAsociada))
            cuenta = int.TryParse(unidad.CuentaAsociada.Trim(), out var c) ? c : throw new AppUserFacingException("La cuenta asociada debe ser numérica.", "UNEGOCIO_CUENTA");
        var pdv = (unidad.PuntoVenta ?? string.Empty).Trim();
        if (pdv.Length > 0 && pdv.All(char.IsDigit)) pdv = pdv.PadLeft(4, '0');

        await using var cn = await AbrirAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var existe = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.V_TA_UnidadNegocio WHERE LTRIM(RTRIM(Codigo)) = @Codigo;", new { Codigo = codigo }, tx, cancellationToken: ct)) > 0;
        if (esNueva && existe) throw new AppUserFacingException($"Ya existe la unidad de negocio '{codigo}'.", "UNEGOCIO_DUPLICADA");
        if (!esNueva && !existe) throw new AppUserFacingException($"No se encontró la unidad de negocio '{codigo}'.", "UNEGOCIO_NO_EXISTE");

        var p = new
        {
            Codigo = codigo,
            // Como el VB6 (FmtLeerCodigo 4): los códigos cortos se guardan alineados a la derecha.
            CodigoGuardado = codigo.Length < 4 ? codigo.PadLeft(4) : codigo,
            Descripcion = unidad.Descripcion.Trim(),
            Deposito = Nulo(unidad.Deposito), DepositoDif = Nulo(unidad.DepositoDiferencias), MotivoDif = Nulo(unidad.MotivoDiferencias),
            unidad.ModificaDiferencias, Cuenta = cuenta, Grupo = Nulo(unidad.Grupo),
            UsaEfc = unidad.UsaFacturacionElectronica, Cuit = Nulo(cuit), Razon = Nulo(unidad.RazonSocial), Pdv = Nulo(pdv),
            Crt = Nulo(unidad.RutaCrt), Key = Nulo(unidad.RutaKey), Reporte = Nulo(unidad.RutaReportes)
        };
        await cn.ExecuteAsync(new CommandDefinition(esNueva
            ? """
              INSERT INTO dbo.V_TA_UnidadNegocio (Codigo, Descripcion, DepositosAsociados, IdDepositoDif, MotivoParaDiferencias, ModificaDiferencias,
                     SUCURSALCUENTA, GRUPO, USAEFC, CUIT, RAZON_SOCIAL, PDV, RUTA_CRT, RUTA_KEY, RUTA_REPORTE, Transmision)
              VALUES (@CodigoGuardado, @Descripcion, @Deposito, @DepositoDif, @MotivoDif, @ModificaDiferencias,
                     @Cuenta, @Grupo, @UsaEfc, @Cuit, @Razon, @Pdv, @Crt, @Key, @Reporte, 0);
              """
            : """
              UPDATE dbo.V_TA_UnidadNegocio SET Descripcion = @Descripcion, DepositosAsociados = @Deposito, IdDepositoDif = @DepositoDif,
                     MotivoParaDiferencias = @MotivoDif, ModificaDiferencias = @ModificaDiferencias, SUCURSALCUENTA = @Cuenta, GRUPO = @Grupo,
                     USAEFC = @UsaEfc, CUIT = @Cuit, RAZON_SOCIAL = @Razon, PDV = @Pdv, RUTA_CRT = @Crt, RUTA_KEY = @Key, RUTA_REPORTE = @Reporte
              WHERE LTRIM(RTRIM(Codigo)) = @Codigo;
              """, p, tx, cancellationToken: ct));

        var claves = Claves(codigo);
        await GuardarCfgAsync(cn, tx, claves["AgenteRetPerc"], unidad.AgenteRetencionPercepcionIibb ? "SI" : "NO", null, ct);
        await GuardarCfgAsync(cn, tx, claves["AgenteRetGan"], unidad.AgenteRetencionGanancias ? "SI" : "NO", null, ct);
        // Sin condición elegida el VB6 graba "1" (Responsable Inscripto).
        await GuardarCfgAsync(cn, tx, claves["CondicionIva"], string.IsNullOrWhiteSpace(unidad.CondicionIva) ? "1" : unidad.CondicionIva.Trim(), null, ct);
        await GuardarCfgAsync(cn, tx, claves["FormaDePago"], string.Empty, (unidad.FormaDePago ?? string.Empty).Trim(), ct);
        await GuardarCfgAsync(cn, tx, claves["InicioActividades"], (unidad.InicioActividades ?? string.Empty).Trim(), null, ct);
        await GuardarCfgAsync(cn, tx, claves["IngresosBrutos"], (unidad.IngresosBrutos ?? string.Empty).Trim(), null, ct);
        await GuardarCfgAsync(cn, tx, claves["Domicilio"], (unidad.Domicilio ?? string.Empty).Trim(), null, ct);
        await tx.CommitAsync(ct);

        await appEvents.LogAuditAsync(Modulo, esNueva ? "CrearUnidadNegocio" : "GuardarUnidadNegocio", "V_TA_UnidadNegocio", codigo,
            esNueva ? "Se creó la unidad de negocio." : "Se actualizó la unidad de negocio.",
            new { codigo, unidad.Descripcion, unidad.UsaFacturacionElectronica, Cuit = cuit, PuntoVenta = pdv, unidad.CondicionIva }, ct);
    }

    public async Task EliminarAsync(string codigo, CancellationToken ct = default)
    {
        codigo = (codigo ?? string.Empty).Trim();
        await using var cn = await AbrirAsync(ct);
        // No se borra una unidad con comprobantes: dejaría movimientos sin unidad.
        var usos = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT CASE WHEN OBJECT_ID('dbo.V_MV_CPTE') IS NOT NULL
                        AND EXISTS (SELECT 1 FROM dbo.V_MV_CPTE WHERE LTRIM(RTRIM(UNEGOCIO)) = @Codigo) THEN 1 ELSE 0 END;
            """, new { Codigo = codigo }, cancellationToken: ct));
        if (usos > 0)
            throw new AppUserFacingException($"La unidad de negocio '{codigo}' tiene comprobantes: no se puede borrar.", "UNEGOCIO_EN_USO");
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var filas = await cn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.V_TA_UnidadNegocio WHERE LTRIM(RTRIM(Codigo)) = @Codigo;", new { Codigo = codigo }, tx, cancellationToken: ct));
        if (filas == 0) throw new AppUserFacingException($"No se encontró la unidad de negocio '{codigo}'.", "UNEGOCIO_NO_EXISTE");
        await cn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN @Claves;", new { Claves = Claves(codigo).Values.ToArray() }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        await appEvents.LogAuditAsync(Modulo, "EliminarUnidadNegocio", "V_TA_UnidadNegocio", codigo, "Se eliminó la unidad de negocio.", null, ct);
    }

    /// <summary>Claves de TA_CONFIGURACION que el VB6 usa por unidad (en mayúsculas, con el código sin espacios).</summary>
    internal static IReadOnlyDictionary<string, string> Claves(string codigo)
    {
        var s = "_UN_" + codigo.Trim().ToUpperInvariant();
        return new Dictionary<string, string>
        {
            ["Domicilio"] = "DOMICILIO" + s,
            ["CondicionIva"] = "CONDICIONIVA" + s,
            ["InicioActividades"] = "INICIO_ACTIVIDADES" + s,
            ["IngresosBrutos"] = "INGRESOS_BRUTOS" + s,
            ["FormaDePago"] = "FORMADEPAGO" + s,
            ["AgenteRetPerc"] = "AGENTE_RETPERC" + s,
            ["AgenteRetGan"] = "AGENTE_RETGAN" + s
        };
    }

    private static Task GuardarCfgAsync(SqlConnection cn, SqlTransaction tx, string clave, string valor, string? valorAux, CancellationToken ct)
        => cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.TA_CONFIGURACION SET VALOR = @Valor, ValorAux = @ValorAux, FechaHora_Modificacion = GETDATE() WHERE UPPER(LTRIM(RTRIM(CLAVE))) = @Clave;
            IF @@ROWCOUNT = 0 INSERT INTO dbo.TA_CONFIGURACION (GRUPO, CLAVE, VALOR, ValorAux, FechaHora_Grabacion) VALUES (N'DATOS', @Clave, @Valor, @ValorAux, GETDATE());
            """, new { Clave = clave, Valor = valor, ValorAux = valorAux }, tx, cancellationToken: ct));

    private static string? Nulo(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
