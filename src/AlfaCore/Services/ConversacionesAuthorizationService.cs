using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class ConversacionesAuthorizationService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppUserSessionService appUserSession) : IConversacionesAuthorizationService
{
    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<bool> CanManageAsync(CancellationToken ct = default)
    {
        var currentUser = appUserSession.CurrentUser;
        if (currentUser is null)
            return false;
        if (currentUser.SuperAdmin)
            return true;

        var usuario = currentUser.UserName.Trim();
        var sistema = currentUser.SystemCode.Trim();
        if (usuario.Length == 0 || sistema.Length == 0)
            return false;

        const string sql = """
            SELECT CASE WHEN
                (
                    OBJECT_ID(N'dbo.TA_USUARIOS', N'U') IS NOT NULL
                    AND COL_LENGTH(N'dbo.TA_USUARIOS', N'Administrador') IS NOT NULL
                    AND EXISTS (
                        SELECT 1
                        FROM dbo.TA_USUARIOS u
                        WHERE UPPER(LTRIM(RTRIM(ISNULL(u.NOMBRE, N'')))) = UPPER(@Usuario)
                          AND UPPER(LTRIM(RTRIM(ISNULL(u.SISTEMA, N'')))) = UPPER(@Sistema)
                          AND ISNULL(u.Administrador, 0) <> 0
                    )
                )
                OR
                (
                    OBJECT_ID(N'dbo.CONV_ADMINISTRADORES', N'U') IS NOT NULL
                    AND EXISTS (
                        SELECT 1
                        FROM dbo.CONV_ADMINISTRADORES a
                        WHERE UPPER(LTRIM(RTRIM(ISNULL(a.Usuario, N'')))) = UPPER(@Usuario)
                          AND UPPER(LTRIM(RTRIM(ISNULL(a.Sistema, N'')))) = UPPER(@Sistema)
                    )
                )
            THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Usuario", usuario);
        cmd.Parameters.AddWithValue("@Sistema", sistema);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    public async Task EnsureCanManageAsync(CancellationToken ct = default)
    {
        if (!await CanManageAsync(ct))
        {
            throw new UnauthorizedAccessException(
                "No tenés permiso para modificar la configuración de Conversaciones.");
        }
    }

    public async Task EnsureCanAttendConversationAsync(long idConversacion, CancellationToken ct = default)
    {
        if (idConversacion <= 0)
            throw new ArgumentOutOfRangeException(nameof(idConversacion));

        var currentUser = appUserSession.CurrentUser;

        // Webhooks, workers y automatizaciones no tienen una sesión interactiva. Sus acciones se
        // autorizan por su propio flujo técnico y no deben quedar bloqueadas por permisos de UI.
        if (currentUser is null || currentUser.SuperAdmin)
            return;

        var usuario = currentUser.UserName.Trim();
        var sistema = currentUser.SystemCode.Trim();
        if (usuario.Length == 0 || sistema.Length == 0)
            throw BuildAttendDeniedException();

        const string sql = """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM dbo.CONV_CONVERSACIONES c
                WHERE c.IdConversacion = @IdConversacion
                  AND (
                      c.Canal <> N'WHATSAPP'
                      OR c.IdNumeroWhatsApp IS NULL
                      OR (
                          OBJECT_ID(N'dbo.CONV_ADMINISTRADORES', N'U') IS NOT NULL
                          AND EXISTS (
                              SELECT 1
                              FROM dbo.CONV_ADMINISTRADORES a
                              WHERE UPPER(LTRIM(RTRIM(ISNULL(a.Usuario, N'')))) = UPPER(@Usuario)
                                AND UPPER(LTRIM(RTRIM(ISNULL(a.Sistema, N'')))) = UPPER(@Sistema)
                          )
                      )
                      OR (
                          OBJECT_ID(N'dbo.CONV_WHATSAPP_NUMERO_USUARIOS', N'U') IS NOT NULL
                          AND NOT EXISTS (
                              SELECT 1
                              FROM dbo.CONV_WHATSAPP_NUMERO_USUARIOS cualquierUsuario
                              WHERE cualquierUsuario.IdNumero = c.IdNumeroWhatsApp
                          )
                      )
                      OR (
                          OBJECT_ID(N'dbo.CONV_WHATSAPP_NUMERO_USUARIOS', N'U') IS NOT NULL
                          AND EXISTS (
                              SELECT 1
                              FROM dbo.CONV_WHATSAPP_NUMERO_USUARIOS numeroUsuario
                              WHERE numeroUsuario.IdNumero = c.IdNumeroWhatsApp
                                AND UPPER(LTRIM(RTRIM(ISNULL(numeroUsuario.Usuario, N'')))) = UPPER(@Usuario)
                                AND UPPER(LTRIM(RTRIM(ISNULL(numeroUsuario.Sistema, N'')))) = UPPER(@Sistema)
                          )
                      )
                  )
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdConversacion", idConversacion);
        cmd.Parameters.AddWithValue("@Usuario", usuario);
        cmd.Parameters.AddWithValue("@Sistema", sistema);
        if (await cmd.ExecuteScalarAsync(ct) is not true)
            throw BuildAttendDeniedException();
    }

    public async Task EnsureCanUseWhatsAppNumeroAsync(int idNumero, CancellationToken ct = default)
    {
        if (idNumero <= 0)
            throw new ArgumentOutOfRangeException(nameof(idNumero));

        var currentUser = appUserSession.CurrentUser;
        if (currentUser is null || currentUser.SuperAdmin)
            return;

        var usuario = currentUser.UserName.Trim();
        var sistema = currentUser.SystemCode.Trim();
        if (usuario.Length == 0 || sistema.Length == 0)
            throw BuildAttendDeniedException();

        const string sql = """
            SELECT CASE WHEN
                EXISTS (
                    SELECT 1
                    FROM dbo.CONV_ADMINISTRADORES a
                    WHERE UPPER(LTRIM(RTRIM(ISNULL(a.Usuario, N'')))) = UPPER(@Usuario)
                      AND UPPER(LTRIM(RTRIM(ISNULL(a.Sistema, N'')))) = UPPER(@Sistema)
                )
                OR NOT EXISTS (
                    SELECT 1
                    FROM dbo.CONV_WHATSAPP_NUMERO_USUARIOS cualquierUsuario
                    WHERE cualquierUsuario.IdNumero = @IdNumero
                )
                OR EXISTS (
                    SELECT 1
                    FROM dbo.CONV_WHATSAPP_NUMERO_USUARIOS numeroUsuario
                    WHERE numeroUsuario.IdNumero = @IdNumero
                      AND UPPER(LTRIM(RTRIM(ISNULL(numeroUsuario.Usuario, N'')))) = UPPER(@Usuario)
                      AND UPPER(LTRIM(RTRIM(ISNULL(numeroUsuario.Sistema, N'')))) = UPPER(@Sistema)
                )
            THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;
            """;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdNumero", idNumero);
        cmd.Parameters.AddWithValue("@Usuario", usuario);
        cmd.Parameters.AddWithValue("@Sistema", sistema);
        if (await cmd.ExecuteScalarAsync(ct) is not true)
            throw BuildAttendDeniedException();
    }

    private static UnauthorizedAccessException BuildAttendDeniedException()
        => new("No tenés permiso para ver ni atender conversaciones de este número de WhatsApp.");
}
