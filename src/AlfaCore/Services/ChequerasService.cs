using System.Data;
using System.Globalization;
using AlfaCore.Common;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public interface IChequerasService
{
    Task<ChequerasCatalogo> CatalogoAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Chequera>> ListarAsync(CancellationToken ct = default);
    /// <summary>Alta (Id 0) o modificación.</summary>
    Task<Chequera> GuardarAsync(Chequera chequera, CancellationToken ct = default);
    Task EliminarAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<BancoTabla>> BancosAsync(CancellationToken ct = default);
    /// <summary>Alta (nuevo) o cambio de descripción; el código no se modifica (lo usan cheques y chequeras).</summary>
    Task<BancoTabla> GuardarBancoAsync(BancoTabla banco, bool nuevo, CancellationToken ct = default);
    Task EliminarBancoAsync(string idBanco, CancellationToken ct = default);
}

/// <summary>
/// Chequeras de cheques propios, como «Control de Chequeras» del legacy (D010350, V_TA_CTRLCHEQUERAS.vbp, CTRL_CHEQUERAS.frm):
/// banco, serie, nro de control (talonario), rango de numeración sin superponerse con otro talonario del mismo banco y serie,
/// y las cuentas del banco (común, MEDIODEPAGO DB) y de cheques diferidos (MEDIODEPAGO CH), que quedan marcadas como cuentas
/// bancarias (TIPOVISTA BC) igual que al grabar en el legacy.
/// </summary>
public sealed class ChequerasService(ISessionService sessions, IAppUserSessionService users, IPermissionService permissions, IAppEventService events)
    : IChequerasService
{
    internal const string MenuChequeras = "D010350";
    private sealed class ReglaException(string message) : InvalidOperationException(message);

    public Task<ChequerasCatalogo> CatalogoAsync(CancellationToken ct = default) => EjecutarAsync("Catalogo", async cn => new ChequerasCatalogo
    {
        Bancos = (await cn.QueryAsync<ChequeraBanco>(new CommandDefinition(
            "SELECT LTRIM(RTRIM(IdBanco)) IdBanco, LTRIM(RTRIM(ISNULL(Descripcion,''))) Descripcion FROM dbo.V_TA_Bancos ORDER BY Descripcion;", cancellationToken: ct))).ToList(),
        Cuentas = (await cn.QueryAsync<AsientoCuenta>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(CODIGO)) Codigo, LTRIM(RTRIM(ISNULL(DESCRIPCION,''))) Descripcion, LTRIM(RTRIM(ISNULL(MedioDePago,''))) MedioDePago,
                   CONVERT(bit, 0) PideVencimiento, LTRIM(RTRIM(ISNULL(Moneda,'1'))) Moneda, LTRIM(RTRIM(ISNULL(TipoVista,''))) TipoVista
            FROM dbo.MA_CUENTAS WHERE ISNULL(TITULO,0) = 0 AND ISNULL(Dada_De_Baja,0) = 0 ORDER BY CODIGO;
            """, cancellationToken: ct))).ToList(),
        PuedeModificar = true
    }, ct);

    private const string SelectChequeras = """
        SELECT c.ID Id, LTRIM(RTRIM(c.IDBANCO)) IdBanco, LTRIM(RTRIM(ISNULL(b.Descripcion,''))) Banco, LTRIM(RTRIM(c.SERIE)) Serie, c.Talonario,
               CONVERT(bigint, c.NUMERO_DESDE) Desde, CONVERT(bigint, c.NUMERO_HASTA) Hasta,
               LTRIM(RTRIM(ISNULL(c.CuentaBanco,''))) CuentaBanco, LTRIM(RTRIM(ISNULL(c.CuentaBancoDiferido,''))) CuentaDiferido,
               ISNULL(e.Emitidos, 0) Emitidos, e.UltimoEmitido
        FROM dbo.V_TA_CTRL_CHEQUERAS c
        LEFT JOIN dbo.V_TA_Bancos b ON b.IdBanco = c.IDBANCO
        OUTER APPLY (
            SELECT COUNT(DISTINCT x.Nro) Emitidos, MAX(x.Nro) UltimoEmitido
            FROM (SELECT CONVERT(bigint, CASE WHEN SUBSTRING(a.NroComprobanteBancario, LEN(c.SERIE) + 2, 20) NOT LIKE '%[^0-9]%' AND LEN(a.NroComprobanteBancario) BETWEEN LEN(c.SERIE) + 2 AND LEN(c.SERIE) + 19 THEN SUBSTRING(a.NroComprobanteBancario, LEN(c.SERIE) + 2, 20) END) Nro
                  FROM dbo.MV_ASIENTOS a
                  WHERE a.[DEBE-HABER] = 'H' AND a.IdBanco = c.IDBANCO AND a.CUENTA IN (c.CuentaBanco, c.CuentaBancoDiferido)
                    AND LEFT(a.NroComprobanteBancario, LEN(c.SERIE) + 1) = c.SERIE + '-'
                    AND LEN(a.NroComprobanteBancario) BETWEEN LEN(c.SERIE) + 2 AND LEN(c.SERIE) + 19
                    AND SUBSTRING(a.NroComprobanteBancario, LEN(c.SERIE) + 2, 20) NOT LIKE '%[^0-9]%') x
            WHERE x.Nro BETWEEN c.NUMERO_DESDE AND c.NUMERO_HASTA) e
        """;

    public Task<IReadOnlyList<Chequera>> ListarAsync(CancellationToken ct = default) => EjecutarAsync("Listar", async cn =>
        (IReadOnlyList<Chequera>)(await cn.QueryAsync<Chequera>(new CommandDefinition(SelectChequeras + " ORDER BY b.Descripcion, c.SERIE, c.Talonario;",
            commandTimeout: 120, cancellationToken: ct))).ToList(), ct);

    /// <summary>Validaciones del legacy (CampoValida/ValidaNumeracion) que no dependen de la base.</summary>
    internal static void Validar(Chequera c)
    {
        c.IdBanco = (c.IdBanco ?? "").Trim(); c.Serie = (c.Serie ?? "").Trim().ToUpperInvariant();
        c.CuentaBanco = (c.CuentaBanco ?? "").Trim(); c.CuentaDiferido = (c.CuentaDiferido ?? "").Trim();
        if (c.IdBanco.Length == 0) throw new ReglaException("Elegí el banco.");
        if (c.Serie.Length != 1) throw new ReglaException("La serie es una letra (o un dígito).");
        if (c.Talonario < 0) throw new ReglaException("El número de control no puede ser negativo.");
        if (c.Desde <= 0 || c.Hasta <= 0) throw new ReglaException("Cargá el número desde y hasta de la chequera.");
        if (c.Desde > c.Hasta) throw new ReglaException("El número desde no puede ser mayor que el hasta.");
        if (c.Hasta > 99_999_999_999_999L) throw new ReglaException("El número de cheque es demasiado largo.");
        if (c.CuentaBanco.Length == 0 && c.CuentaDiferido.Length == 0) throw new ReglaException("Indicá la cuenta del banco (común) o la de cheques diferidos.");
        if (c.CuentaBanco.Length > 0 && c.CuentaBanco == c.CuentaDiferido) throw new ReglaException("La cuenta común y la de diferidos tienen que ser distintas.");
    }

    /// <summary>Superposición: un rango no puede caer dentro de otro talonario del mismo banco y serie, ni contenerlo.</summary>
    internal static Chequera? Superpuesta(Chequera c, IEnumerable<Chequera> otras)
        => otras.FirstOrDefault(o => o.Id != c.Id && o.IdBanco.Trim() == c.IdBanco.Trim() && o.Serie.Trim().Equals(c.Serie.Trim(), StringComparison.OrdinalIgnoreCase)
            && c.Desde <= o.Hasta && o.Desde <= c.Hasta);

    public Task<Chequera> GuardarAsync(Chequera c, CancellationToken ct = default) => EjecutarAsync("Guardar", async cn =>
    {
        Validar(c);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await cn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.V_TA_Bancos WHERE LTRIM(RTRIM(IdBanco)) = @IdBanco;",
                new { c.IdBanco }, tx, cancellationToken: ct)) == 0)
            throw new ReglaException("El banco no existe.");
        foreach (var (cuenta, medio, nombre) in new[] { (c.CuentaBanco, "DB", "común"), (c.CuentaDiferido, "CH", "de diferidos") }.Where(x => x.Item1.Length > 0))
        {
            var datos = await cn.QuerySingleOrDefaultAsync<(bool Titulo, bool Baja, string Medio, string TipoVista)?>(new CommandDefinition("""
                SELECT CONVERT(bit, ISNULL(TITULO,0)), CONVERT(bit, ISNULL(Dada_De_Baja,0)), LTRIM(RTRIM(ISNULL(MedioDePago,''))), LTRIM(RTRIM(ISNULL(TipoVista,'')))
                FROM dbo.MA_CUENTAS WHERE CODIGO = @Cuenta;
                """, new { Cuenta = cuenta }, tx, cancellationToken: ct))
                ?? throw new ReglaException($"La cuenta {nombre} {cuenta} no existe.");
            if (datos.Titulo || datos.Baja) throw new ReglaException($"La cuenta {nombre} {cuenta} es un título o está dada de baja.");
            if (datos.Medio.Length > 0 && datos.Medio != medio && datos.Medio is not ("DB" or "CH"))
                throw new ReglaException($"La cuenta {nombre} {cuenta} es de otro medio de pago ({datos.Medio}).");
        }
        var todas = (await cn.QueryAsync<Chequera>(new CommandDefinition(
            "SELECT ID Id, LTRIM(RTRIM(IDBANCO)) IdBanco, LTRIM(RTRIM(SERIE)) Serie, Talonario, CONVERT(bigint, NUMERO_DESDE) Desde, CONVERT(bigint, NUMERO_HASTA) Hasta FROM dbo.V_TA_CTRL_CHEQUERAS WITH (UPDLOCK, HOLDLOCK);",
            transaction: tx, cancellationToken: ct))).ToList();
        if (Superpuesta(c, todas) is { } o)
            throw new ReglaException($"El rango se superpone con la chequera {o.Serie} nro de control {o.Talonario} ({o.Desde} a {o.Hasta}) del mismo banco.");
        if (todas.Any(x => x.Id != c.Id && x.IdBanco == c.IdBanco && x.Serie.Equals(c.Serie, StringComparison.OrdinalIgnoreCase) && x.Talonario == c.Talonario))
            throw new ReglaException($"Ya hay una chequera de ese banco y serie con el nro de control {c.Talonario}.");
        var args = new { c.Id, IdBanco = CodigoPk.Format(c.IdBanco, 4), c.Serie, Desde = (double)c.Desde, Hasta = (double)c.Hasta, c.CuentaBanco, c.CuentaDiferido, c.Talonario };
        if (c.Id > 0)
        {
            var previa = todas.FirstOrDefault(x => x.Id == c.Id) ?? throw new ReglaException("La chequera ya no existe.");
            await cn.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.V_TA_CTRL_CHEQUERAS SET IDBANCO = @IdBanco, SERIE = @Serie, NUMERO_DESDE = @Desde, NUMERO_HASTA = @Hasta,
                    CuentaBanco = NULLIF(@CuentaBanco,''), CuentaBancoDiferido = NULLIF(@CuentaDiferido,''), Talonario = @Talonario
                WHERE ID = @Id;
                """, args, tx, cancellationToken: ct));
        }
        else
            c.Id = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                INSERT INTO dbo.V_TA_CTRL_CHEQUERAS (IDBANCO, SERIE, NUMERO_DESDE, NUMERO_HASTA, CuentaBanco, CuentaBancoDiferido, Talonario)
                VALUES (@IdBanco, @Serie, @Desde, @Hasta, NULLIF(@CuentaBanco,''), NULLIF(@CuentaDiferido,''), @Talonario);
                SELECT CONVERT(int, SCOPE_IDENTITY());
                """, args, tx, cancellationToken: ct));
        // Como el legacy al grabar la chequera: las cuentas quedan como cuentas bancarias (BC), común DB y diferidos CH.
        await cn.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.MA_CUENTAS SET TipoVista = N'BC', MedioDePago = N'DB' WHERE CODIGO = @CuentaBanco AND @CuentaBanco <> '';
            UPDATE dbo.MA_CUENTAS SET TipoVista = N'BC', MedioDePago = N'CH',
                CuentaPrincipal = CASE WHEN @CuentaBanco <> '' THEN @CuentaBanco ELSE CuentaPrincipal END
            WHERE CODIGO = @CuentaDiferido AND @CuentaDiferido <> '';
            """, args, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        await events.LogAuditAsync("Chequeras", args.Id > 0 ? "Modificacion" : "Alta", "V_TA_CTRL_CHEQUERAS", c.Id.ToString(CultureInfo.InvariantCulture),
            $"Grabó la chequera {c.Serie} {c.Desde}-{c.Hasta} del banco {c.IdBanco} (control {c.Talonario}).", args, CancellationToken.None);
        return (await cn.QueryAsync<Chequera>(new CommandDefinition(SelectChequeras + " WHERE c.ID = @Id;", new { c.Id }, cancellationToken: ct))).Single();
    }, ct);

    public Task EliminarAsync(int id, CancellationToken ct = default) => EjecutarAsync("Eliminar", async cn =>
    {
        var c = (await cn.QueryAsync<Chequera>(new CommandDefinition(SelectChequeras + " WHERE c.ID = @Id;", new { Id = id }, cancellationToken: ct))).SingleOrDefault()
            ?? throw new ReglaException("La chequera ya no existe.");
        if (c.Emitidos > 0) throw new ReglaException($"La chequera tiene {c.Emitidos} cheque(s) emitido(s): no se puede borrar.");
        await cn.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.V_TA_CTRL_CHEQUERAS WHERE ID = @Id;", new { Id = id }, cancellationToken: ct));
        await events.LogAuditAsync("Chequeras", "Eliminar", "V_TA_CTRL_CHEQUERAS", id.ToString(CultureInfo.InvariantCulture),
            $"Borró la chequera {c.Serie} {c.Desde}-{c.Hasta} de {c.Banco}.", new { c.IdBanco, c.Serie, c.Talonario, c.Desde, c.Hasta }, CancellationToken.None);
        return true;
    }, ct);

    // ── Bancos (V_TA_Bancos) ────────────────────────────────────────────────────────────────────

    private const string SelectBancos = """
        SELECT LTRIM(RTRIM(b.IdBanco)) IdBanco, LTRIM(RTRIM(ISNULL(b.Descripcion,''))) Descripcion,
               ISNULL(a.Cheques, 0) Cheques,
               (SELECT COUNT(*) FROM dbo.V_TA_CTRL_CHEQUERAS c WHERE c.IDBANCO = b.IdBanco) Chequeras,
               CASE WHEN OBJECT_ID(N'dbo.P_MA_Legajos', N'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM dbo.P_MA_Legajos l WHERE LTRIM(RTRIM(l.IdBanco)) = LTRIM(RTRIM(b.IdBanco))) END Legajos
        FROM dbo.V_TA_Bancos b
        LEFT JOIN (SELECT IdBanco, COUNT(*) Cheques FROM dbo.MV_ASIENTOS WHERE IdBanco IS NOT NULL AND IdBanco <> N'' GROUP BY IdBanco) a ON a.IdBanco = b.IdBanco
        """;

    public Task<IReadOnlyList<BancoTabla>> BancosAsync(CancellationToken ct = default) => EjecutarAsync("Bancos", async cn =>
        (IReadOnlyList<BancoTabla>)(await cn.QueryAsync<BancoTabla>(new CommandDefinition(SelectBancos + " ORDER BY b.Descripcion;", commandTimeout: 120, cancellationToken: ct))).ToList(), ct);

    /// <summary>Código: letras o dígitos, hasta 4 (los numéricos se alinean a la derecha como en el legacy). Descripción obligatoria.</summary>
    internal static void ValidarBanco(BancoTabla b)
    {
        b.IdBanco = (b.IdBanco ?? "").Trim().ToUpperInvariant(); b.Descripcion = (b.Descripcion ?? "").Trim();
        if (b.IdBanco.Length is < 1 or > 4 || !b.IdBanco.All(char.IsLetterOrDigit)) throw new ReglaException("El código del banco tiene de 1 a 4 letras o dígitos (ej. 7 Galicia, 14 Provincia).");
        if (b.IdBanco.All(char.IsDigit)) b.IdBanco = b.IdBanco.TrimStart('0') is { Length: > 0 } sinCeros ? sinCeros : "0";
        if (b.Descripcion.Length < 2) throw new ReglaException("Escribí el nombre del banco.");
        if (b.Descripcion.Length > 50) throw new ReglaException("El nombre del banco puede tener hasta 50 caracteres.");
    }

    public Task<BancoTabla> GuardarBancoAsync(BancoTabla b, bool nuevo, CancellationToken ct = default) => EjecutarAsync("GuardarBanco", async cn =>
    {
        ValidarBanco(b);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var existentes = (await cn.QueryAsync<(string IdBanco, string Descripcion)>(new CommandDefinition(
            "SELECT LTRIM(RTRIM(IdBanco)), LTRIM(RTRIM(ISNULL(Descripcion,''))) FROM dbo.V_TA_Bancos WITH (UPDLOCK, HOLDLOCK);", transaction: tx, cancellationToken: ct))).ToList();
        static string Clave(string c) => c.All(char.IsDigit) ? c.TrimStart('0') : c.ToUpperInvariant();
        var mismo = existentes.FirstOrDefault(x => Clave(x.IdBanco) == Clave(b.IdBanco));
        if (nuevo && mismo.IdBanco is not null) throw new ReglaException($"Ya existe el banco {mismo.IdBanco} ({mismo.Descripcion}).");
        if (!nuevo && mismo.IdBanco is null) throw new ReglaException("El banco ya no existe.");
        if (existentes.FirstOrDefault(x => Clave(x.IdBanco) != Clave(b.IdBanco) && x.Descripcion.Equals(b.Descripcion, StringComparison.OrdinalIgnoreCase)) is { IdBanco: not null } otro)
            throw new ReglaException($"Ya hay otro banco con ese nombre (código {otro.IdBanco}).");
        var args = new { IdBanco = nuevo ? CodigoPk.Format(b.IdBanco, 4) : mismo.IdBanco, b.Descripcion };
        await cn.ExecuteAsync(new CommandDefinition(nuevo
            ? "INSERT INTO dbo.V_TA_Bancos (IdBanco, Descripcion, FechaHora_Grabacion, Transmision) VALUES (@IdBanco, @Descripcion, GETDATE(), 0);"
            : "UPDATE dbo.V_TA_Bancos SET Descripcion = @Descripcion, FechaHora_Modificacion = GETDATE() WHERE LTRIM(RTRIM(IdBanco)) = @IdBanco;",
            args, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        await events.LogAuditAsync("Bancos", nuevo ? "Alta" : "Modificacion", "V_TA_Bancos", b.IdBanco,
            $"{(nuevo ? "Agregó" : "Modificó")} el banco {b.IdBanco} {b.Descripcion}.", args, CancellationToken.None);
        return (await cn.QueryAsync<BancoTabla>(new CommandDefinition(SelectBancos + " WHERE LTRIM(RTRIM(b.IdBanco)) = LTRIM(RTRIM(@IdBanco));", args, cancellationToken: ct))).Single();
    }, ct);

    public Task EliminarBancoAsync(string idBanco, CancellationToken ct = default) => EjecutarAsync("EliminarBanco", async cn =>
    {
        var b = (await cn.QueryAsync<BancoTabla>(new CommandDefinition(SelectBancos + " WHERE LTRIM(RTRIM(b.IdBanco)) = @IdBanco;",
            new { IdBanco = (idBanco ?? "").Trim() }, commandTimeout: 120, cancellationToken: ct))).SingleOrDefault() ?? throw new ReglaException("El banco ya no existe.");
        if (b.EnUso) throw new ReglaException($"El banco {b.Descripcion} está en uso ({b.Cheques} cheque(s), {b.Chequeras} chequera(s), {b.Legajos} legajo(s)): no se puede borrar.");
        await cn.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.V_TA_Bancos WHERE LTRIM(RTRIM(IdBanco)) = @IdBanco;", new { b.IdBanco }, cancellationToken: ct));
        await events.LogAuditAsync("Bancos", "Eliminar", "V_TA_Bancos", b.IdBanco, $"Borró el banco {b.IdBanco} {b.Descripcion}.", new { b.IdBanco, b.Descripcion }, CancellationToken.None);
        return true;
    }, ct);

    private async Task<T> EjecutarAsync<T>(string accion, Func<SqlConnection, Task<T>> operacion, CancellationToken ct)
    {
        if (!TenantDataAccessGuard.TryGetAuthorizedSession(sessions, users, out _)) throw new ReglaException("Ingresá en la base activa.");
        try
        {
            if (!await permissions.HasAccessAsync(MenuChequeras, ct)) throw new ReglaException("Tu usuario no tiene permiso para las chequeras y bancos.");
            await using var cn = new SqlConnection(sessions.GetConnectionString());
            await cn.OpenAsync(ct);
            return await operacion(cn);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (ReglaException) { throw; }
        catch (AppUserFacingException) { throw; }
        catch (Exception ex)
        {
            var codigo = await events.LogErrorAsync("Chequeras", accion, ex, "No se pudo completar la operación con la chequera.", ct: ct);
            throw new AppUserFacingException("No se pudo completar la operación con la chequera.", codigo, ex);
        }
    }
}
