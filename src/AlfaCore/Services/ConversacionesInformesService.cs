using System.Net;
using System.Net.Mail;
using System.Text;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class ConversacionesInformesService(
    IConfiguration configuration,
    ISessionService sessionService,
    IAppEventService appEvents,
    IConversacionAsistenteService asistenteService,
    IConversacionesService conversacionesService) : IConversacionesInformesService
{
    private const string ModuleName = "Conversaciones";

    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    // Agregación del período: una fila por cliente (agrupando sus contactos) o por contacto sin
    // cliente. Incluye clientes con abono activo aunque no tengan actividad; excluye no-abonados sin
    // movimientos y el Chat interno. @Desde/@Hasta se pasan desde C# (evita DATEFROMPARTS por compat).
    private const string AggregateSql = """
        WITH ConvMes AS (
            SELECT c.IdConversacion, m.FechaHora, m.Direction,
                CASE WHEN LTRIM(RTRIM(ISNULL(c.ClienteCodigo, N''))) <> N'' THEN N'C:' + LTRIM(RTRIM(c.ClienteCodigo))
                     WHEN c.IdContacto IS NOT NULL THEN N'K:' + CONVERT(nvarchar(20), c.IdContacto)
                     ELSE N'S:' END AS GKey
            FROM dbo.CONV_MENSAJES m
            INNER JOIN dbo.CONV_CONVERSACIONES c ON c.IdConversacion = m.IdConversacion
            WHERE m.FechaHora >= @Desde AND m.FechaHora < @Hasta
              AND ISNULL(c.Canal, N'') <> N'INTERNO'
              AND m.Direction IN (N'ENTRANTE', N'SALIENTE')
        ),
        MsgAgg AS (
            SELECT GKey,
                COUNT(DISTINCT IdConversacion) AS CantConversaciones,
                COUNT(DISTINCT CAST(FechaHora AS date)) AS CantDias,
                COUNT(*) AS CantMensajes,
                SUM(CASE WHEN Direction = N'ENTRANTE' THEN 1 ELSE 0 END) AS CantMensajesEntrantes,
                SUM(CASE WHEN Direction = N'SALIENTE' THEN 1 ELSE 0 END) AS CantMensajesSalientes
            FROM ConvMes GROUP BY GKey
        ),
        PartesRaw AS (
            SELECT
                CASE WHEN LTRIM(RTRIM(ISNULL(p.ClienteCodigo, N''))) <> N'' THEN N'C:' + LTRIM(RTRIM(p.ClienteCodigo))
                     WHEN p.IdContacto IS NOT NULL THEN N'K:' + CONVERT(nvarchar(20), p.IdContacto)
                     ELSE N'S:' END AS GKey,
                p.Minutos, p.Facturable
            FROM dbo.ALFACORE_PARTES_HORAS p
            WHERE p.Fecha >= @Desde AND p.Fecha < @Hasta
        ),
        PartesAgg AS (
            SELECT GKey, SUM(Minutos) AS MinutosTotales,
                SUM(CASE WHEN Facturable = 1 THEN Minutos ELSE 0 END) AS MinutosFacturables
            FROM PartesRaw GROUP BY GKey
        ),
        AbonoAgg AS (
            SELECT N'C:' + LTRIM(RTRIM(a.Cuenta)) AS GKey
            FROM dbo.MA_CUENTAS_AUTOCPTES a
            WHERE ISNULL(a.Activo, 0) = 1
              AND a.FechaUltMov >= DATEADD(MONTH, -3, GETDATE())
              AND LTRIM(RTRIM(ISNULL(a.Cuenta, N''))) <> N''
            GROUP BY LTRIM(RTRIM(a.Cuenta))
        ),
        Keys AS (
            SELECT GKey FROM MsgAgg
            UNION SELECT GKey FROM PartesAgg
            UNION SELECT GKey FROM AbonoAgg
        ),
        KeysParsed AS (
            SELECT GKey,
                CASE WHEN GKey LIKE N'C:%' THEN N'CLIENTE' WHEN GKey LIKE N'K:%' THEN N'CONTACTO' ELSE N'SININDENT' END AS TipoFila,
                CASE WHEN GKey LIKE N'C:%' THEN SUBSTRING(GKey, 3, 20) ELSE N'' END AS ClienteCodigo,
                CASE WHEN GKey LIKE N'K:%' THEN CONVERT(int, SUBSTRING(GKey, 3, 20)) ELSE NULL END AS IdContacto
            FROM Keys
        )
        SELECT
            k.TipoFila,
            k.ClienteCodigo,
            k.IdContacto,
            COALESCE(NULLIF(LTRIM(RTRIM(cli.RAZON_SOCIAL)), N''), NULLIF(LTRIM(RTRIM(con.Nombre_y_Apellido)), N''), N'Sin identificar') AS NombreMostrar,
            ISNULL(LTRIM(RTRIM(cli.Clasificacion)), N'') AS ClasificacionCodigo,
            ISNULL(clsd.Descripcion, N'') AS ClasificacionDesc,
            ISNULL(catd.Descripcion, N'') AS CategoriaDesc,
            ISNULL(ab.Estado, N'SinAbono') AS EstadoAbono,
            ISNULL(ma.CantConversaciones, 0) AS CantConversaciones,
            ISNULL(ma.CantDias, 0) AS CantDias,
            ISNULL(ma.CantMensajes, 0) AS CantMensajes,
            ISNULL(ma.CantMensajesEntrantes, 0) AS CantMensajesEntrantes,
            ISNULL(ma.CantMensajesSalientes, 0) AS CantMensajesSalientes,
            ISNULL(pa.MinutosTotales, 0) AS MinutosTotales,
            ISNULL(pa.MinutosFacturables, 0) AS MinutosFacturables
        FROM KeysParsed k
        LEFT JOIN MsgAgg ma ON ma.GKey = k.GKey
        LEFT JOIN PartesAgg pa ON pa.GKey = k.GKey
        LEFT JOIN dbo.VT_CLIENTES cli ON k.ClienteCodigo <> N'' AND UPPER(LTRIM(RTRIM(cli.CODIGO))) = UPPER(k.ClienteCodigo)
        LEFT JOIN dbo.TA_CLASIFICACIONES clsd ON clsd.Codigo = cli.Clasificacion
        LEFT JOIN dbo.v_ta_categoria catd ON catd.IdCategoria = cli.IDCategoria
        LEFT JOIN dbo.MA_CONTACTOS con ON con.id = k.IdContacto
        OUTER APPLY (
            SELECT TOP (1) CASE
                    WHEN ISNULL(auto.Activo, 0) = 0 THEN N'Suspendido'
                    WHEN auto.FechaUltMov IS NULL OR auto.FechaUltMov < DATEADD(MONTH, -3, GETDATE()) THEN N'Suspendido'
                    ELSE N'Abonado' END AS Estado
            FROM dbo.MA_CUENTAS_AUTOCPTES auto
            WHERE k.ClienteCodigo <> N'' AND UPPER(LTRIM(RTRIM(auto.Cuenta))) = UPPER(k.ClienteCodigo)
            ORDER BY auto.FechaUltMov DESC
        ) ab
        WHERE ISNULL(ab.Estado, N'SinAbono') = N'Abonado'
           OR ISNULL(ma.CantMensajes, 0) > 0
           OR ISNULL(pa.MinutosTotales, 0) > 0
        ORDER BY ISNULL(ma.CantMensajes, 0) + ISNULL(pa.MinutosTotales, 0) DESC, NombreMostrar;
        """;

    public Task<ConversacionInformeMensualDto> GenerarAsync(int anio, int mes, string? usuario, CancellationToken ct = default)
        => ExecuteLoggedAsync("GenerarInforme", async token =>
        {
            if (mes < 1 || mes > 12)
                throw new ArgumentOutOfRangeException(nameof(mes), "El mes debe estar entre 1 y 12.");

            var desde = new DateTime(anio, mes, 1);
            var hasta = desde.AddMonths(1);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var filas = (await cn.QueryAsync<ConversacionInformeFilaDto>(new CommandDefinition(
                AggregateSql, new { Desde = desde, Hasta = hasta }, cancellationToken: token))).ToList();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(token);

            // Reemplazar el informe del período si ya existía.
            var idExistente = await cn.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT IdInforme FROM dbo.CONV_INFORME_MENSUAL WHERE Anio = @Anio AND Mes = @Mes;",
                new { Anio = anio, Mes = mes }, tx, cancellationToken: token));

            if (idExistente.HasValue)
            {
                await cn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM dbo.CONV_INFORME_MENSUAL_DET WHERE IdInforme = @Id; DELETE FROM dbo.CONV_INFORME_MENSUAL WHERE IdInforme = @Id;",
                    new { Id = idExistente.Value }, tx, cancellationToken: token));
            }

            var idInforme = await cn.ExecuteScalarAsync<int>(new CommandDefinition("""
                INSERT INTO dbo.CONV_INFORME_MENSUAL (Anio, Mes, FechaGeneracion, UsuarioGeneracion, Estado)
                VALUES (@Anio, @Mes, GETDATE(), @Usuario, N'GENERADO');
                SELECT CAST(SCOPE_IDENTITY() AS int);
                """, new { Anio = anio, Mes = mes, Usuario = (usuario ?? string.Empty).Trim() }, tx, cancellationToken: token));

            foreach (var f in filas)
            {
                await cn.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.CONV_INFORME_MENSUAL_DET
                    (IdInforme, TipoFila, ClienteCodigo, IdContacto, NombreMostrar, ClasificacionCodigo, ClasificacionDesc,
                     CategoriaDesc, EstadoAbono, CantConversaciones, CantDias, CantMensajes, CantMensajesEntrantes,
                     CantMensajesSalientes, MinutosTotales, MinutosFacturables)
                    VALUES
                    (@IdInforme, @TipoFila, @ClienteCodigo, @IdContacto, @NombreMostrar, @ClasificacionCodigo, @ClasificacionDesc,
                     @CategoriaDesc, @EstadoAbono, @CantConversaciones, @CantDias, @CantMensajes, @CantMensajesEntrantes,
                     @CantMensajesSalientes, @MinutosTotales, @MinutosFacturables);
                    """, new
                {
                    IdInforme = idInforme,
                    f.TipoFila,
                    ClienteCodigo = string.IsNullOrWhiteSpace(f.ClienteCodigo) ? null : f.ClienteCodigo,
                    f.IdContacto,
                    f.NombreMostrar,
                    ClasificacionCodigo = string.IsNullOrWhiteSpace(f.ClasificacionCodigo) ? null : f.ClasificacionCodigo,
                    ClasificacionDesc = string.IsNullOrWhiteSpace(f.ClasificacionDesc) ? null : f.ClasificacionDesc,
                    CategoriaDesc = string.IsNullOrWhiteSpace(f.CategoriaDesc) ? null : f.CategoriaDesc,
                    f.EstadoAbono,
                    f.CantConversaciones,
                    f.CantDias,
                    f.CantMensajes,
                    f.CantMensajesEntrantes,
                    f.CantMensajesSalientes,
                    f.MinutosTotales,
                    f.MinutosFacturables
                }, tx, cancellationToken: token));
            }

            await tx.CommitAsync(token);

            await appEvents.LogAuditAsync(ModuleName, "GenerarInforme", "CONV_INFORME_MENSUAL",
                idInforme.ToString(), "Informe mensual de conversaciones generado.",
                new { anio, mes, filas = filas.Count }, token);

            return (await GetAsync(idInforme, token))!;
        }, ct);

    public Task<IReadOnlyList<ConversacionInformeListItemDto>> ListarAsync(CancellationToken ct = default)
        => ExecuteLoggedAsync("ListarInformes", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            var items = (await cn.QueryAsync<ConversacionInformeListItemDto>(new CommandDefinition("""
                SELECT h.IdInforme, h.Anio, h.Mes, h.FechaGeneracion,
                    (SELECT COUNT(1) FROM dbo.CONV_INFORME_MENSUAL_DET d WHERE d.IdInforme = h.IdInforme) AS CantFilas
                FROM dbo.CONV_INFORME_MENSUAL h
                ORDER BY h.Anio DESC, h.Mes DESC;
                """, cancellationToken: token))).ToList();
            return (IReadOnlyList<ConversacionInformeListItemDto>)items;
        }, ct);

    public Task<ConversacionInformeMensualDto?> GetAsync(int idInforme, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetInforme", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            var cab = await cn.QuerySingleOrDefaultAsync<ConversacionInformeMensualDto>(new CommandDefinition(
                "SELECT IdInforme, Anio, Mes, FechaGeneracion, ISNULL(UsuarioGeneracion, N'') AS UsuarioGeneracion, Estado FROM dbo.CONV_INFORME_MENSUAL WHERE IdInforme = @Id;",
                new { Id = idInforme }, cancellationToken: token));
            if (cab is null)
                return null;

            cab.Filas = (await cn.QueryAsync<ConversacionInformeFilaDto>(new CommandDefinition(
                DetalleSelectSql, new { Id = idInforme }, cancellationToken: token))).ToList();
            return cab;
        }, ct);

    public Task<ConversacionInformeMensualDto?> GetByPeriodoAsync(int anio, int mes, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetInformePeriodo", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            var id = await cn.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT IdInforme FROM dbo.CONV_INFORME_MENSUAL WHERE Anio = @Anio AND Mes = @Mes;",
                new { Anio = anio, Mes = mes }, cancellationToken: token));
            return id.HasValue ? await GetAsync(id.Value, token) : null;
        }, ct);

    private const string DetalleSelectSql = """
        SELECT IdDetalle, IdInforme, TipoFila, ISNULL(ClienteCodigo, N'') AS ClienteCodigo, IdContacto,
            NombreMostrar, ISNULL(ClasificacionCodigo, N'') AS ClasificacionCodigo, ISNULL(ClasificacionDesc, N'') AS ClasificacionDesc,
            ISNULL(CategoriaDesc, N'') AS CategoriaDesc, EstadoAbono, CantConversaciones, CantDias, CantMensajes,
            CantMensajesEntrantes, CantMensajesSalientes, MinutosTotales, MinutosFacturables,
            ISNULL(ResumenBorrador, N'') AS ResumenBorrador, ISNULL(ResumenEditado, N'') AS ResumenEditado,
            ResumenGeneradoIA, EstadoEnvio, FechaEnvio, ISNULL(CanalEnvio, N'') AS CanalEnvio
        FROM dbo.CONV_INFORME_MENSUAL_DET
        WHERE IdInforme = @Id
        ORDER BY (CantMensajes + MinutosTotales) DESC, NombreMostrar;
        """;

    // ===================== Fase 2: detalle, resumen IA y envío =====================

    private const string FilaByIdSql = """
        SELECT IdDetalle, IdInforme, TipoFila, ISNULL(ClienteCodigo, N'') AS ClienteCodigo, IdContacto,
            NombreMostrar, ISNULL(ClasificacionCodigo, N'') AS ClasificacionCodigo, ISNULL(ClasificacionDesc, N'') AS ClasificacionDesc,
            ISNULL(CategoriaDesc, N'') AS CategoriaDesc, EstadoAbono, CantConversaciones, CantDias, CantMensajes,
            CantMensajesEntrantes, CantMensajesSalientes, MinutosTotales, MinutosFacturables,
            ISNULL(ResumenBorrador, N'') AS ResumenBorrador, ISNULL(ResumenEditado, N'') AS ResumenEditado,
            ResumenGeneradoIA, EstadoEnvio, FechaEnvio, ISNULL(CanalEnvio, N'') AS CanalEnvio
        FROM dbo.CONV_INFORME_MENSUAL_DET WHERE IdDetalle = @Id;
        """;

    private const string MensajesMesSql = """
        SELECT m.FechaHora, ISNULL(c.Canal, N'') AS Canal, m.Direction, ISNULL(CAST(m.Texto AS nvarchar(max)), N'') AS Texto
        FROM dbo.CONV_MENSAJES m
        INNER JOIN dbo.CONV_CONVERSACIONES c ON c.IdConversacion = m.IdConversacion
        WHERE m.FechaHora >= @Desde AND m.FechaHora < @Hasta
          AND ISNULL(c.Canal, N'') <> N'INTERNO'
          AND m.Direction IN (N'ENTRANTE', N'SALIENTE')
          AND ( (@ClienteCodigo <> N'' AND UPPER(LTRIM(RTRIM(ISNULL(c.ClienteCodigo, N'')))) = UPPER(@ClienteCodigo))
                OR (@IdContacto IS NOT NULL AND c.IdContacto = @IdContacto) )
        ORDER BY m.FechaHora, m.IdMensaje;
        """;

    private const string WhatsAppConvSql = """
        SELECT TOP (1) c.IdConversacion
        FROM dbo.CONV_CONVERSACIONES c
        WHERE ISNULL(c.Canal, N'') = N'WHATSAPP'
          AND ( (@ClienteCodigo <> N'' AND UPPER(LTRIM(RTRIM(ISNULL(c.ClienteCodigo, N'')))) = UPPER(@ClienteCodigo))
                OR (@IdContacto IS NOT NULL AND c.IdContacto = @IdContacto) )
        ORDER BY c.IdConversacion DESC;
        """;

    public Task<ConversacionInformeDetalleDto?> GetDetalleAsync(int idDetalle, CancellationToken ct = default)
        => ExecuteLoggedAsync("GetDetalleInforme", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);

            var fila = await cn.QuerySingleOrDefaultAsync<ConversacionInformeFilaDto>(new CommandDefinition(
                FilaByIdSql, new { Id = idDetalle }, cancellationToken: token));
            if (fila is null)
                return null;

            var cab = await cn.QuerySingleOrDefaultAsync<ConversacionInformeMensualDto>(new CommandDefinition(
                "SELECT IdInforme, Anio, Mes FROM dbo.CONV_INFORME_MENSUAL WHERE IdInforme = @Id;",
                new { Id = fila.IdInforme }, cancellationToken: token));
            if (cab is null)
                return null;

            var desde = new DateTime(cab.Anio, cab.Mes, 1);
            var hasta = desde.AddMonths(1);
            var args = new { Desde = desde, Hasta = hasta, ClienteCodigo = fila.ClienteCodigo ?? string.Empty, fila.IdContacto };

            var mensajes = (await cn.QueryAsync<ConversacionInformeMensajeDto>(new CommandDefinition(
                MensajesMesSql, args, cancellationToken: token))).ToList();

            var email = string.Empty;
            if (fila.TipoFila == "CLIENTE" && !string.IsNullOrWhiteSpace(fila.ClienteCodigo))
                email = await cn.ExecuteScalarAsync<string>(new CommandDefinition(
                    "SELECT TOP (1) ISNULL(MAIL, N'') FROM dbo.VT_CLIENTES WHERE UPPER(LTRIM(RTRIM(CODIGO))) = UPPER(@C);",
                    new { C = fila.ClienteCodigo }, cancellationToken: token)) ?? string.Empty;
            else if (fila.TipoFila == "CONTACTO" && fila.IdContacto.HasValue)
                email = await cn.ExecuteScalarAsync<string>(new CommandDefinition(
                    "SELECT TOP (1) ISNULL(email, N'') FROM dbo.MA_CONTACTOS WHERE id = @I;",
                    new { I = fila.IdContacto.Value }, cancellationToken: token)) ?? string.Empty;

            var idConv = await cn.ExecuteScalarAsync<long?>(new CommandDefinition(
                WhatsAppConvSql, args, cancellationToken: token));

            return new ConversacionInformeDetalleDto
            {
                Fila = fila,
                Anio = cab.Anio,
                Mes = cab.Mes,
                ClienteEmail = (email ?? string.Empty).Trim(),
                IdConversacionWhatsApp = idConv,
                Mensajes = mensajes
            };
        }, ct);

    public Task<string> GenerarResumenAsync(int idDetalle, CancellationToken ct = default)
        => ExecuteLoggedAsync("GenerarResumenInforme", async token =>
        {
            var det = await GetDetalleAsync(idDetalle, token)
                      ?? throw new InvalidOperationException("La fila del informe no existe.");

            if (det.Mensajes.Count == 0)
            {
                const string vacio = "Durante el período no registramos conversaciones con este cliente.";
                await GuardarBorradorAsync(idDetalle, vacio, token);
                return vacio;
            }

            var resumen = await asistenteService.ResumirAsync(BuildResumenInstrucciones(det), BuildConversacionesTexto(det), token);
            if (string.IsNullOrWhiteSpace(resumen))
                throw new InvalidOperationException("No se pudo generar el resumen. Verificá que OPENAI_API_KEY esté configurada en el servidor.");

            await GuardarBorradorAsync(idDetalle, resumen.Trim(), token);
            return resumen.Trim();
        }, ct);

    public Task GuardarResumenEditadoAsync(int idDetalle, string texto, CancellationToken ct = default)
        => ExecuteLoggedAsync("GuardarResumenInforme", async token =>
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.CONV_INFORME_MENSUAL_DET SET ResumenEditado = @Texto WHERE IdDetalle = @Id;",
                new { Id = idDetalle, Texto = (texto ?? string.Empty).Trim() }, cancellationToken: token));
            return true;
        }, ct);

    public Task EnviarPorEmailAsync(int idDetalle, string destinatario, CancellationToken ct = default)
        => ExecuteLoggedAsync("EnviarInformeEmail", async token =>
        {
            var to = (destinatario ?? string.Empty).Trim();
            if (to.Length == 0)
                throw new InvalidOperationException("Ingresá un email de destino.");
            _ = new MailAddress(to);

            var det = await GetDetalleAsync(idDetalle, token)
                      ?? throw new InvalidOperationException("La fila del informe no existe.");
            var texto = TextoAEnviar(det.Fila);
            if (string.IsNullOrWhiteSpace(texto))
                throw new InvalidOperationException("Generá o escribí el resumen antes de enviarlo.");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            var empresa = (await ReadConfigAsync(cn, "Nombre", token)).Trim();
            var mail = await ResolveMailConfigAsync(cn, token);

            using var message = new MailMessage
            {
                From = new MailAddress(mail.From),
                Subject = $"Resumen de atención {det.PeriodoLabel}" + (empresa.Length > 0 ? $" - {empresa}" : string.Empty),
                Body = BuildEmailHtml(det, texto, empresa),
                IsBodyHtml = true
            };
            message.To.Add(to);

            using var client = new SmtpClient(mail.Server, mail.Port)
            {
                EnableSsl = mail.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(mail.From, mail.Password)
            };
            await client.SendMailAsync(message, token);

            await MarcarEnviadoAsync(cn, idDetalle, "EMAIL", token);
            await appEvents.LogAuditAsync(ModuleName, "EnviarInformeEmail", "CONV_INFORME_MENSUAL_DET",
                idDetalle.ToString(), "Resumen mensual enviado por email.", new { to }, token);
            return true;
        }, ct);

    public Task EnviarPorWhatsAppAsync(int idDetalle, CancellationToken ct = default)
        => ExecuteLoggedAsync("EnviarInformeWhatsApp", async token =>
        {
            var det = await GetDetalleAsync(idDetalle, token)
                      ?? throw new InvalidOperationException("La fila del informe no existe.");
            if (!det.IdConversacionWhatsApp.HasValue)
                throw new InvalidOperationException("Este cliente no tiene una conversación de WhatsApp para enviarle el resumen.");
            var texto = TextoAEnviar(det.Fila);
            if (string.IsNullOrWhiteSpace(texto))
                throw new InvalidOperationException("Generá o escribí el resumen antes de enviarlo.");

            // Si la ventana de 24 h está cerrada, SendMessageAsync falla: se avisa para mandarlo por mail o con plantilla.
            await conversacionesService.SendMessageAsync(new ConversacionSendMessageRequest
            {
                IdConversacion = det.IdConversacionWhatsApp.Value,
                Texto = texto,
                MessageType = "TEXT",
                UsuarioAccion = "AlfaCore",
                SistemaAccion = "INFORME"
            }, token);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(token);
            await MarcarEnviadoAsync(cn, idDetalle, "WHATSAPP", token);
            await appEvents.LogAuditAsync(ModuleName, "EnviarInformeWhatsApp", "CONV_INFORME_MENSUAL_DET",
                idDetalle.ToString(), "Resumen mensual enviado por WhatsApp.", new { det.IdConversacionWhatsApp }, token);
            return true;
        }, ct);

    private async Task GuardarBorradorAsync(int idDetalle, string texto, CancellationToken ct)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.CONV_INFORME_MENSUAL_DET SET ResumenBorrador = @Texto, ResumenGeneradoIA = 1 WHERE IdDetalle = @Id;",
            new { Id = idDetalle, Texto = texto }, cancellationToken: ct));
    }

    private static async Task MarcarEnviadoAsync(SqlConnection cn, int idDetalle, string canal, CancellationToken ct)
    {
        await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.CONV_INFORME_MENSUAL_DET SET EstadoEnvio = N'ENVIADO', FechaEnvio = GETDATE(), CanalEnvio = @Canal WHERE IdDetalle = @Id;",
            new { Id = idDetalle, Canal = canal }, cancellationToken: ct));
    }

    private static string TextoAEnviar(ConversacionInformeFilaDto fila)
        => !string.IsNullOrWhiteSpace(fila.ResumenEditado) ? fila.ResumenEditado.Trim() : (fila.ResumenBorrador ?? string.Empty).Trim();

    private static string BuildResumenInstrucciones(ConversacionInformeDetalleDto det)
    {
        var meses = new[] { "", "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };
        var mesNombre = det.Mes >= 1 && det.Mes <= 12 ? meses[det.Mes] : det.Mes.ToString();
        var rubro = string.IsNullOrWhiteSpace(det.Fila.CategoriaDesc) ? string.Empty : $" (rubro: {det.Fila.CategoriaDesc})";
        return
            $"Sos parte del equipo de soporte técnico de software. Redactá un resumen BREVE y cordial, en español rioplatense, " +
            $"dirigido AL CLIENTE \"{det.Fila.NombreMostrar}\"{rubro}, sobre la atención que le dimos durante {mesNombre}. " +
            "Contá en 1 a 3 párrafos (o bullets cortos): qué consultas o problemas trajo, qué se resolvió y qué quedó pendiente si aplica. " +
            "Tono profesional y cercano, como para enviárselo por email/WhatsApp. No inventes datos que no estén en las conversaciones, " +
            "no incluyas información interna del equipo ni de otros clientes, y no uses tecnicismos innecesarios. " +
            "No agregues encabezados de email ni firma: solo el cuerpo del mensaje.";
    }

    private static string BuildConversacionesTexto(ConversacionInformeDetalleDto det)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Conversaciones del período {det.PeriodoLabel} con {det.Fila.NombreMostrar}:");
        sb.AppendLine();
        foreach (var m in det.Mensajes)
        {
            var quien = m.EsEntrante ? "Cliente" : "Nosotros";
            var texto = (m.Texto ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (texto.Length == 0)
                continue;
            sb.AppendLine($"[{m.FechaHora:dd/MM HH:mm}] {quien}: {texto}");
        }

        var full = sb.ToString();
        // Presupuesto de tokens: nos quedamos con el tramo final si es muy largo.
        const int max = 14000;
        return full.Length <= max ? full : full[^max..];
    }

    private static string BuildEmailHtml(ConversacionInformeDetalleDto det, string texto, string empresa)
    {
        var cuerpo = System.Net.WebUtility.HtmlEncode(texto).Replace("\n", "<br>");
        var firma = string.IsNullOrWhiteSpace(empresa) ? string.Empty : $"<p style=\"margin-top:18px;color:#475569;\">Saludos,<br>{System.Net.WebUtility.HtmlEncode(empresa)}</p>";
        return $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#0f172a;line-height:1.5;max-width:640px;">
              <h2 style="margin:0 0 12px;font-size:18px;">Resumen de atención — {det.PeriodoLabel}</h2>
              <p style="margin:0 0 10px;color:#475569;">Hola {System.Net.WebUtility.HtmlEncode(det.Fila.NombreMostrar)},</p>
              <div style="white-space:normal;">{cuerpo}</div>
              {firma}
            </div>
            """;
    }

    private static async Task<string> ReadConfigAsync(SqlConnection cn, string clave, CancellationToken ct)
        => (await cn.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT TOP (1) ISNULL(VALOR, N'') FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) = UPPER(@Clave);",
            new { Clave = clave }, cancellationToken: ct))) ?? string.Empty;

    private async Task<MailInfo> ResolveMailConfigAsync(SqlConnection cn, CancellationToken ct)
    {
        var server = Fallback(await ReadConfigAsync(cn, "EMAIL_SERVER", ct), "EMAIL_SERVER");
        var port = Fallback(await ReadConfigAsync(cn, "EMAIL_PORT", ct), "EMAIL_PORT");
        var account = Fallback(await ReadConfigAsync(cn, "EMAIL_CTA", ct), "EMAIL_CTA");
        var password = Fallback(await ReadConfigAsync(cn, "EMAIL_PASS", ct), "EMAIL_PASS");
        var ssl = Fallback(await ReadConfigAsync(cn, "EMAIL_SSL", ct), "EMAIL_SSL");

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(port)
            || string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Falta configurar el correo saliente (EMAIL_SERVER, EMAIL_PORT, EMAIL_CTA, EMAIL_PASS).");
        if (!int.TryParse(port, out var smtpPort) || smtpPort <= 0)
            throw new InvalidOperationException("EMAIL_PORT no tiene un valor válido.");

        var enableSsl = ssl.Equals("SI", StringComparison.OrdinalIgnoreCase)
            || ssl.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            || ssl.Equals("1", StringComparison.OrdinalIgnoreCase);
        return new MailInfo(server, smtpPort, account, password, enableSsl);
    }

    private string Fallback(string dbValue, string key)
        => !string.IsNullOrWhiteSpace(dbValue) ? dbValue.Trim() : (configuration[key] ?? configuration[$"PuntoVenta:{key}"] ?? string.Empty);

    private sealed record MailInfo(string Server, int Port, string From, string Password, bool EnableSsl);

    private async Task<T> ExecuteLoggedAsync<T>(string action, Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await appEvents.LogErrorAsync(ModuleName, action, ex,
                "No se pudo completar la operación de informes de conversaciones.",
                null, AppEventSeverity.Error, ct);
            throw;
        }
    }
}
