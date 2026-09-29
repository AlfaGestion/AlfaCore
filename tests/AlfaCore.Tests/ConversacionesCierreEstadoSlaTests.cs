using Xunit;

namespace AlfaCore.Tests;

/// <summary>
/// El bug reportado ("timeline dice que se cerró, pero el estado sigue Abierta y aparecen eventos SLA
/// después del cierre") no era una inconsistencia entre subsistemas -- era un único mecanismo real:
/// RefreshConversationAsync/ReopenClosedConversationsWithIncomingAfterCloseAsync ya reabrían la
/// conversación automáticamente cuando llegaba un mensaje entrante después del cierre (comportamiento
/// intencional, documentado en el propio código), pero lo hacían con un UPDATE directo sin dejar
/// ningún evento en el timeline. SLA (ProcesarSeguimientosSlaAsync) ya filtraba correctamente por
/// EsCerrado=0 -- generaba eventos nuevos porque la conversación estaba genuinamente reabierta, no por
/// un bug propio de SLA.
///
/// No existe arnés de integración SQL en este proyecto para estos flujos (abren SqlConnection real).
/// Los tests de esta clase son estructurales: verifican, sobre el código fuente compilado, que el fix
/// (evento "AUTOAPERTURA" sólo cuando el estado realmente cambió, mismo patrón que el cierre manual) y
/// las invariantes pre-existentes que había que preservar siguen en el lugar correcto. Se distinguen
/// explícitamente de tests de comportamiento real.
/// </summary>
public sealed class ConversacionesCierreEstadoSlaTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ServiceSource = File.ReadAllText(
        Path.Combine(RepositoryRoot, "src", "AlfaCore", "Services", "ConversacionesService.cs"));

    // --- Cierre: DB queda cerrada + evento existe (estructural) -------------------------------

    [Fact]
    public void ChangeStatusAsync_PersisteEstadoDentroDeUnaTransaccionAntesDeCualquierEvento()
    {
        var block = BlockBetween(
            "public async Task ChangeStatusAsync(ConversacionEstadoRequest request",
            "public Task SetConversationPinAsync(");

        var updateIndex = block.IndexOf("UPDATE dbo.CONV_CONVERSACIONES", StringComparison.Ordinal);
        var commitIndex = block.IndexOf("await tx.CommitAsync(token);", StringComparison.Ordinal);
        var eventoIndex = block.IndexOf("AddInternalEventCoreAsync(", StringComparison.Ordinal);

        Assert.True(updateIndex >= 0, "No se encontró el UPDATE de estado.");
        Assert.True(commitIndex > updateIndex, "El commit de la transacción debe venir después del UPDATE.");
        Assert.True(eventoIndex > commitIndex, "El evento de timeline debe insertarse recién después de confirmar la transacción -- si el commit falla, la excepción corta el método antes de llegar al evento.");
    }

    [Fact]
    public void ChangeStatusAsync_CierreExitoso_GeneraEventoDeCierreConTextoEsperado()
    {
        Assert.Contains("cerr\\u00f3 la conversaci\\u00f3n.", ServiceSource, StringComparison.Ordinal);
        Assert.Contains("isClosed && !wasClosed", ServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeStatusAsync_CierreDosVeces_NoGeneraSegundoEvento()
    {
        // Idempotencia ya existente: el bloque de evento sólo corre si el CodigoEstado nuevo es
        // distinto del anterior -- cerrar una conversación ya cerrada (doble click, dos requests) es
        // un no-op de evento, aunque el UPDATE (idempotente) se re-ejecute.
        var block = BlockBetween(
            "public async Task ChangeStatusAsync(ConversacionEstadoRequest request",
            "public Task SetConversationPinAsync(");

        Assert.Contains(
            "if (!string.Equals(previousState.CodigoEstado, state, StringComparison.OrdinalIgnoreCase))",
            block,
            StringComparison.Ordinal);
    }

    // --- Reapertura automática por mensaje nuevo: fix del bug reportado ----------------------

    [Fact]
    public void RefreshConversationAsync_DetectaReaperturaRealViaOutputYNoPorElParametroReabrir()
    {
        var block = BlockBetween(
            "private async Task RefreshConversationAsync(long idConversacion",
            "private async Task TryFillConversationDisplayNameIfEmptyAsync(");

        Assert.Contains("OUTPUT deleted.CodigoEstado, inserted.CodigoEstado", block, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(estadoAnterior, estadoNuevo, StringComparison.OrdinalIgnoreCase)", block, StringComparison.Ordinal);

        var outputIndex = block.IndexOf("OUTPUT deleted.CodigoEstado, inserted.CodigoEstado", StringComparison.Ordinal);
        var checkIndex = block.IndexOf("if (reopenIfClosed", StringComparison.Ordinal);
        var eventoIndex = block.IndexOf("AddInternalEventCoreAsync(", StringComparison.Ordinal);

        Assert.True(checkIndex > outputIndex, "El chequeo de estado debe leer el resultado real del UPDATE (OUTPUT), no asumir que reopenIfClosed=true implica que reabrió.");
        Assert.True(eventoIndex > checkIndex, "El evento debe insertarse sólo dentro del chequeo de cambio de estado real.");
    }

    [Fact]
    public void RefreshConversationAsync_ReaperturaAutomatica_DejaEventoEnElTimeline()
    {
        var block = BlockBetween(
            "private async Task RefreshConversationAsync(long idConversacion",
            "private async Task TryFillConversationDisplayNameIfEmptyAsync(");

        Assert.Contains("reabrió la conversación automáticamente", block, StringComparison.Ordinal);
        Assert.Contains("\"AUTOAPERTURA\"", block, StringComparison.Ordinal);
    }

    [Fact]
    public void ReopenClosedConversationsWithIncomingAfterCloseAsync_SweepMasivo_TambienDejaEventoPorConversacion()
    {
        var block = BlockBetween(
            "private async Task ReopenClosedConversationsWithIncomingAfterCloseAsync(SqlConnection cn",
            "private async Task UpdateMessageDeliveryAsync(");

        Assert.Contains("OUTPUT inserted.IdConversacion INTO @Reopened", block, StringComparison.Ordinal);
        Assert.Contains("foreach (var reopenedId in reopenedIds)", block, StringComparison.Ordinal);
        Assert.Contains("\"AUTOAPERTURA\"", block, StringComparison.Ordinal);
    }

    [Fact]
    public void HistorySync_NuncaReabreConversacionesCerradas()
    {
        // Documentado y preservado: un mensaje de history (importado, viejo) nunca debe reabrir una
        // conversación que un agente ya cerró -- sólo el mensaje "en vivo" (ordinary inbound / canales)
        // pasa reopenIfClosed:true.
        var historyCall = ServiceSource.IndexOf(
            "RefreshConversationAsync(conversationId, NormalizeIncomingTimestamp(incoming.Timestamp), incoming.Text, token, reopenIfClosed: false);",
            StringComparison.Ordinal);

        Assert.True(historyCall >= 0, "No se encontró la llamada de RefreshConversationAsync para history con reopenIfClosed:false.");
    }

    // --- SLA: ya filtraba correctamente por EsCerrado, no se tocó -----------------------------

    [Fact]
    public void ProcesarSeguimientosSlaAsync_FiltraPorEstadoAbiertoReal()
    {
        var block = BlockBetween(
            "public async Task<int> ProcesarSeguimientosSlaAsync(CancellationToken ct",
            "foreach (var cand in candidatos)");

        Assert.Contains("INNER JOIN dbo.CONV_ESTADOS e ON e.CodigoEstado = c.CodigoEstado", block, StringComparison.Ordinal);
        Assert.Contains("AND ISNULL(e.EsCerrado, 0) = 0", block, StringComparison.Ordinal);
    }

    // --- Tenant: el fix reusa AddInternalEventCoreAsync / ConnectionString existentes ---------

    [Fact]
    public void FixDeReapertura_NoIntroduceConexionNiIdBaseNuevos()
    {
        var block = BlockBetween(
            "private async Task ReopenClosedConversationsWithIncomingAfterCloseAsync(SqlConnection cn",
            "private async Task UpdateMessageDeliveryAsync(");

        Assert.DoesNotContain("idBase", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlConnectionStringBuilder", block, StringComparison.Ordinal);
        Assert.DoesNotContain("GetActiveSession()", block, StringComparison.Ordinal);
    }

    private static string BlockBetween(string start, string end)
    {
        var startIndex = ServiceSource.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"No se encontro inicio: {start}");
        var endIndex = string.IsNullOrEmpty(end)
            ? ServiceSource.Length
            : ServiceSource.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"No se encontro fin: {end}");
        return ServiceSource[startIndex..endIndex];
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "AlfaCore.sln"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}
