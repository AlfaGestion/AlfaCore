using AlfaCore.ConciliacionBancaria.Application.Abstractions;
using AlfaCore.ConciliacionBancaria.Application.DTOs;
using AlfaCore.ConciliacionBancaria.Application.Services;
using AlfaCore.ConciliacionBancaria.Domain.Entities;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.ConciliacionBancaria.Infrastructure;

// Cada ejecución captura una conexión y transacción: nunca cambia de empresa a mitad del proceso.
public sealed class ConciliacionRepository(SqlConnection cn, SqlTransaction tx, long? movimientoId = null) : IBankStatementRepository
{
    internal const string ToleranciaCobroSql = """
        DECLARE @toleranciaCobro decimal(18,2)=(SELECT TOP(1) TRY_CAST(VALOR AS decimal(18,2))
            FROM dbo.TA_CONFIGURACION WHERE LTRIM(RTRIM(CLAVE))='CONCILIACION-TOLERANCIA');
        IF @toleranciaCobro IS NULL OR @toleranciaCobro<0 OR @toleranciaCobro>100 SET @toleranciaCobro=1;
        """;

    private CommandDefinition Cmd(string sql, object? args, CancellationToken ct)
        => new(sql, args, tx, commandTimeout: 120, cancellationToken: ct);

    public async Task<IReadOnlyCollection<BankStatementMovement>> GetMovementsByImportAsync(long importId, CancellationToken cancellationToken = default)
        => (await cn.QueryAsync<BankStatementMovement>(Cmd("""
            SELECT MovimientoId MovementId, ImportacionId ImportId, OrigenCodigo Source,
                FechaMovimiento MovementDate, DescripcionOriginal Description, Importe Amount,
                ISNULL(ContraparteNombre,'') CounterpartyName, ISNULL(ContraparteCuit,'') CounterpartyTaxId
            FROM dbo.BK_ExtractoMovimiento WHERE ImportacionId=@importId ORDER BY MovimientoId;
            """, new { importId }, cancellationToken))).AsList();

    public async Task<IReadOnlyCollection<InterpretationRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default)
        => (await cn.QueryAsync<InterpretationRule>(Cmd("""
            SELECT ReglaId RuleId, ReglaCodigo RuleCode, Descripcion Description, OrigenCodigo SourceCode,
                PatronTexto TextPattern, ImporteSigno AmountSign, CategoriaObjetivo TargetCategory,
                ClienteFijoCodigo FixedClientCode, Prioridad Priority
            FROM dbo.BK_ReglaInterpretacion WHERE Activa=1 ORDER BY Prioridad,ReglaId;
            """, null, cancellationToken))).AsList();

    public async Task SaveClassificationsAsync(IReadOnlyCollection<ClassifiedMovement> classifications, CancellationToken cancellationToken = default)
    {
        foreach (var c in classifications)
            await cn.ExecuteAsync(Cmd("""
                INSERT INTO dbo.BK_MovimientoClasificado
                (MovimientoId,TipoMovimiento,Confianza,ReglaCodigo,ClienteProbableCodigo,ClienteProbableNombre,Motivo)
                VALUES(@MovementId,@Category,@Confidence,@RuleCode,@ProbableClientCode,@ProbableClientName,@Reasoning);
                """, new { c.MovementId, Category = c.Category.ToString(), c.Confidence, c.RuleCode,
                c.ProbableClientCode, c.ProbableClientName, c.Reasoning }, cancellationToken));
    }

    public async Task<IReadOnlyCollection<CobranzaCandidateDto>> GetCobranzasForMatchingAsync(long importId, CancellationToken cancellationToken = default)
        => (await cn.QueryAsync<CobranzaCandidateDto>(Cmd("""
            SELECT m.MovimientoId MovementId,m.FechaMovimiento MovementDate,m.Importe Amount,
                m.DescripcionOriginal Description,ISNULL(m.ContraparteNombre,'') CounterpartyName,
                ISNULL(m.ContraparteCuit,'') CounterpartyTaxId,c.ClienteProbableCodigo ClassifiedClientCode,
                c.ClienteProbableNombre ClassifiedClientName,
                CONVERT(bit,CASE WHEN c.ReglaCodigo='CLIENTE_MANUAL' THEN 1 ELSE 0 END) ClientManuallyAssigned,
                c.Confianza ClassificationConfidence
            FROM dbo.BK_ExtractoMovimiento m JOIN dbo.BK_MovimientoClasificado c ON c.MovimientoId=m.MovimientoId
            WHERE m.ImportacionId=@importId AND c.TipoMovimiento='Cobranza'
              AND c.ClasificacionId=(SELECT MAX(c2.ClasificacionId) FROM dbo.BK_MovimientoClasificado c2 WHERE c2.MovimientoId=m.MovimientoId)
              AND (@movimientoId IS NULL OR m.MovimientoId=@movimientoId)
              AND NOT EXISTS(SELECT 1 FROM dbo.BK_MovimientoMatch p WHERE p.MovimientoId=m.MovimientoId AND p.EstadoPropuesta<>'RECLASIFICADO')
            ORDER BY m.MovimientoId;
            """, new { importId, movimientoId }, cancellationToken))).AsList();

    public async Task<IReadOnlyCollection<ClientAliasDto>> GetClientAliasesAsync(CancellationToken cancellationToken = default)
        => (await cn.QueryAsync<ClientAliasDto>(Cmd("""
            SELECT LTRIM(RTRIM(a.ClienteCodigo)) ClientCode,a.AliasTexto AliasText,a.Cuit,a.CbuCvu,c.NUMERO_DOCUMENTO ClientTaxId,
                ISNULL(LTRIM(RTRIM(c.RAZON_SOCIAL)),'') ClientName
            FROM dbo.BK_AliasClienteBanco a
            JOIN dbo.VT_CLIENTES c ON LTRIM(RTRIM(c.CODIGO))=LTRIM(RTRIM(a.ClienteCodigo))
            WHERE a.Activo=1 AND LTRIM(RTRIM(a.AliasTexto))<>''
                AND ISNULL(c.Dada_De_Baja,0)=0 AND ISNULL(c.TITULO,0)=0 ORDER BY a.AliasId DESC;
            """, null, cancellationToken))).AsList();

    public async Task<IReadOnlyCollection<PendingDocumentDto>> GetPendingDocumentsAsync(string clientCode, CancellationToken cancellationToken = default)
        => (await cn.QueryAsync<PendingDocumentDto>(Cmd("""
            SELECT LTRIM(RTRIM(CUENTA)) ClientCode,NOMBRE ClientName,LTRIM(RTRIM(TC)) DocumentType,
                LTRIM(RTRIM(LETRA)) Letter,CONVERT(int,SUCURSAL) EmissionCenter,
                CONVERT(bigint,NUMERO) DocumentNumber,CUOTA Installment,
                CONVERT(decimal(18,2),SALDO) PendingBalance,VENCIMIENTO DueDate
            FROM dbo.VE_CPTES_SALDOS_VENTAS
            WHERE LTRIM(RTRIM(CUENTA))=@clientCode AND SALDO>0
            ORDER BY VENCIMIENTO,TC,SUCURSAL,NUMERO,LETRA,CUOTA;
            """, new { clientCode = clientCode.Trim() }, cancellationToken))).AsList();

    public async Task<IReadOnlyCollection<ClientSearchResultDto>> SearchClientByNameAsync(string name, string? taxId, CancellationToken cancellationToken = default)
    {
        var cuit = ContraparteBancaria.NormalizarCuit(taxId);
        if (cuit.Length == 11)
        {
            var exact = (await cn.QueryAsync<ClientSearchResultDto>(Cmd("""
                SELECT LTRIM(RTRIM(CODIGO)) ClientCode,RAZON_SOCIAL ClientName,NUMERO_DOCUMENTO Cuit,100 MatchScore
                FROM dbo.VT_CLIENTES WHERE NUMERO_DOCUMENTO LIKE @patron
                    AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0 ORDER BY CODIGO;
                """, new { patron=ContraparteBancaria.PatronCuit(cuit) }, cancellationToken)))
                .Where(c=>ContraparteBancaria.NormalizarCuit(c.Cuit)==cuit).ToList();
            if (exact.Count > 0) return exact;
        }
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length>=3).Take(2).ToArray();
        if (words.Length == 0) return [];
        static string Escape(string s) => s.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
        return (await cn.QueryAsync<ClientSearchResultDto>(Cmd("""
            SELECT TOP (5) LTRIM(RTRIM(CODIGO)) ClientCode,RAZON_SOCIAL ClientName,NUMERO_DOCUMENTO Cuit,70 MatchScore
            FROM dbo.VT_CLIENTES WHERE RAZON_SOCIAL LIKE @w1 AND RAZON_SOCIAL LIKE @w2
                AND ISNULL(Dada_De_Baja,0)=0 AND ISNULL(TITULO,0)=0 ORDER BY RAZON_SOCIAL,CODIGO;
            """, new { w1 = "%" + Escape(words[0]) + "%", w2 = words.Length>1 ? "%"+Escape(words[1])+"%" : "%" }, cancellationToken))).AsList();
    }

    public Task<bool> HasRecentlyRegisteredPaymentAsync(string clientCode, decimal amount, DateTime movementDate, CancellationToken cancellationToken = default)
        => cn.ExecuteScalarAsync<bool>(Cmd(ToleranciaCobroSql + """
            SELECT CONVERT(bit,CASE WHEN EXISTS(
                SELECT 1 FROM dbo.VE_COBRANZAS_REALIZADAS c
                WHERE LTRIM(RTRIM(c.CUENTA))=@clientCode AND ABS(ABS(c.IMPORTE)-@amount)<=@toleranciaCobro
                  AND c.FECHA>=@desde AND c.FECHA<@hasta
            ) THEN 1 ELSE 0 END);
            """, new { clientCode = clientCode.Trim(), amount, desde = movementDate.Date.AddDays(-15), hasta = movementDate.Date.AddDays(16) }, cancellationToken));

    public async Task SaveMatchAsync(MovementMatchProposal match, IReadOnlyCollection<MovementApplicationDto> applications, CancellationToken cancellationToken = default)
    {
        var id = await cn.ExecuteScalarAsync<long>(Cmd("""
            INSERT INTO dbo.BK_MovimientoMatch(MovimientoId,TipoMatch,ClienteCodigo,ClienteNombre,Confianza,ResumenPropuesta,EvidenciaJson,EstadoPropuesta)
            OUTPUT INSERTED.MatchId VALUES(@MovementId,@MatchingType,@ClientCode,@ClientName,@Confidence,@ProposalSummary,@EvidenceJson,@Status);
            """, new { match.MovementId, MatchingType = match.MatchingType.ToString(), match.ClientCode,
                match.ClientName, match.Confidence, match.ProposalSummary, match.EvidenceJson, match.Status }, cancellationToken));
        foreach (var a in applications)
            await cn.ExecuteAsync(Cmd("""
                INSERT INTO dbo.BK_MovimientoAplicacion(MatchId,TipoComprobante,Letra,CentroEmisor,NumeroComprobante,Cuota,ImporteAplicado,ImportePendienteComprobante,EsParcial)
                VALUES(@id,@DocumentType,@Letter,@EmissionCenter,@DocumentNumber,@Installment,@AppliedAmount,@PendingBalance,@IsPartial);
                """, new { id, a.DocumentType,a.Letter,a.EmissionCenter,a.DocumentNumber,a.Installment,a.AppliedAmount,a.PendingBalance,a.IsPartial }, cancellationToken));
    }
}
