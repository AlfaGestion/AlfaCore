using System.Text;
using AlfaCore.ConciliacionBancaria.Application.Abstractions;
using AlfaCore.ConciliacionBancaria.Domain.Entities;
using AlfaCore.ConciliacionBancaria.Domain.Enums;

namespace AlfaCore.ConciliacionBancaria.Application.Services;

/// <summary>
/// Motor de clasificación determinístico basado en reglas almacenadas en BK_ReglaInterpretacion.
///
/// Algoritmo:
///   1. Carga todas las reglas activas (ordenadas por Prioridad ASC).
///   2. Para cada movimiento de la importación, evalúa las reglas en orden.
///   3. La primera regla que hace match determina la categoría y confianza.
///   4. Si ninguna regla hace match, clasifica como NoIdentificado con confianza 0.
///   5. Guarda todos los resultados en BK_MovimientoClasificado.
///
/// Criterios de match por regla:
///   - SourceCode: si la regla tiene origen, el movimiento debe ser de ese origen.
///   - AmountSign: +1 = solo créditos, -1 = solo débitos, null = cualquiera.
///   - TextPattern: Contains en la descripción (normalizado, sin tildes, minúsculas).
/// </summary>
public sealed class DeterministicClassificationService : IClassificationService
{
    private readonly IBankStatementRepository _repository;

    public DeterministicClassificationService(IBankStatementRepository repository)
    {
        _repository = repository;
    }

    public async Task ClassifyImportAsync(long importId, CancellationToken cancellationToken = default)
    {
        var movements = await _repository.GetMovementsByImportAsync(importId, cancellationToken);
        if (movements.Count == 0) return;

        var rules = await _repository.GetActiveRulesAsync(cancellationToken);

        var classifications = new List<ClassifiedMovement>(movements.Count);

        foreach (var movement in movements)
        {
            var classification = Classify(movement, rules);
            classifications.Add(classification);
        }

        await _repository.SaveClassificationsAsync(classifications, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Lógica de clasificación de un movimiento
    // -------------------------------------------------------------------------

    private static ClassifiedMovement Classify(
        BankStatementMovement movement,
        IReadOnlyCollection<InterpretationRule> rules)
    {
        var normalizedDescription = Normalize(movement.Description);
        var sourceCode            = movement.Source.ToString();

        foreach (var rule in rules)
        {
            if (!MatchesSource(rule, sourceCode))      continue;
            if (!MatchesSign(rule, movement.Amount))   continue;
            if (!MatchesText(rule, normalizedDescription)) continue;

            // La regla hace match
            return new ClassifiedMovement
            {
                MovementId         = movement.MovementId,
                Category           = rule.TargetCategory,
                Confidence         = ComputeConfidence(rule),
                RuleCode           = rule.RuleCode,
                ProbableClientCode = rule.FixedClientCode,
                Reasoning          = $"Regla '{rule.RuleCode}': {rule.Description}"
            };
        }

        // Ninguna regla hizo match → clasificación por defecto según signo del importe
        return FallbackClassification(movement);
    }

    private static bool MatchesSource(InterpretationRule rule, string sourceCode) =>
        rule.SourceCode is null ||
        string.Equals(rule.SourceCode, sourceCode, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesSign(InterpretationRule rule, decimal amount)
    {
        if (rule.AmountSign is null) return true;
        return rule.AmountSign > 0 ? amount > 0 : amount < 0;
    }

    private static bool MatchesText(InterpretationRule rule, string normalizedDescription)
    {
        if (rule.TextPattern is null) return true;
        return normalizedDescription.Contains(Normalize(rule.TextPattern), StringComparison.Ordinal);
    }

    private static decimal ComputeConfidence(InterpretationRule rule)
    {
        // Regla con texto específico: alta confianza. Solo por signo: confianza media.
        if (rule.TextPattern is not null) return 90m;
        return 60m;
    }

    private static ClassifiedMovement FallbackClassification(BankStatementMovement movement)
    {
        // Sin regla matching: asignamos categoría probable por signo con confianza baja
        var category = movement.Amount >= 0
            ? MovementCategory.Cobranza
            : MovementCategory.Gasto;

        return new ClassifiedMovement
        {
            MovementId = movement.MovementId,
            Category   = category,
            Confidence = 30m,
            RuleCode   = "FALLBACK",
            Reasoning  = movement.Amount >= 0
                ? "Sin regla específica. Crédito → Cobranza probable."
                : "Sin regla específica. Débito → Gasto probable."
        };
    }

    // -------------------------------------------------------------------------
    // Normalización de texto para comparación
    // -------------------------------------------------------------------------

    private static string Normalize(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
