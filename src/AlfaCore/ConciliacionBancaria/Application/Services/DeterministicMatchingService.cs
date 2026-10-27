using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlfaCore.ConciliacionBancaria.Application.Abstractions;
using AlfaCore.ConciliacionBancaria.Application.DTOs;
using AlfaCore.ConciliacionBancaria.Domain.Entities;
using AlfaCore.ConciliacionBancaria.Domain.Enums;

namespace AlfaCore.ConciliacionBancaria.Application.Services;

/// <summary>
/// Motor de matching de cobranzas. Genera propuestas en BK_MovimientoMatch
/// sin tocar ninguna tabla core de Alfa ni recalcular saldos.
///
/// ┌─────────────────────────────────────────────────────────────────────────┐
/// │  ALGORITMO (por movimiento clasificado como Cobranza)                  │
/// │                                                                         │
/// │  1. Resolución de cliente                                               │
/// │     a) CUIT de ContraparteCuit vs BK_AliasClienteBanco.Cuit            │
/// │     b) Nombre exacto (normalizado) vs BK_AliasClienteBanco.AliasTexto  │
/// │     c) Nombre parcial (Contains normalizado) vs AliasTexto             │
/// │     d) Búsqueda en VT_CLIENTES por CUIT o nombre (fallback DB)         │
/// │     e) ClienteProbableCodigo de la clasificación                       │
/// │     f) Sin cliente → tipo NoIdentificado                               │
/// │                                                                         │
/// │  2. Obtención de comprobantes pendientes de VE_CPTES_SALDOS_VENTAS     │
/// │     (solo si se resolvió un cliente; saldo de Alfa, no recalculado)    │
/// │                                                                         │
/// │  3. Intento de match (en orden de prioridad):                          │
/// │     a) Exacto     → importe = saldo de un comprobante (±$1)            │
/// │     b) Múltiple   → importe = suma de 2-4 comprobantes (±$1)           │
/// │     c) Parcial    → importe < saldo de un comprobante (pago parcial)   │
/// │     d) CobranzaYaRegistrada → cliente sin saldo + pago en MV_APLICACION│
/// │     e) Anticipo   → cliente identificado pero sin comprobantes         │
/// │     f) NoIdentif. → sin cliente                                        │
/// │                                                                         │
/// │  4. Scoring (0-100):                                                    │
/// │     Puntos cliente: CUIT=50, alias exacto=42, alias parcial=35,        │
/// │                     VT_CLIENTES DB=32, clasificación=28, ninguno=0     │
/// │     Puntos match:   Exacto=50, Múltiple-2=45, Múltiple-3+=40,         │
/// │                     Parcial=30, YaRegistrada=20, Anticipo=15,          │
/// │                     NoIdentif=0                                         │
/// └─────────────────────────────────────────────────────────────────────────┘
/// </summary>
public sealed class DeterministicMatchingService : IMatchingService
{
    private readonly decimal ExactTolerance;
    private readonly decimal MultipleTolerance;
    private const int     MaxDocsMultiple   = 4;        // Máximo de docs en match múltiple

    private readonly IBankStatementRepository _repository;

    public DeterministicMatchingService(IBankStatementRepository repository, decimal tolerancia)
    {
        _repository = repository;
        ExactTolerance = MultipleTolerance = tolerancia;
    }

    // =========================================================================
    // Punto de entrada
    // =========================================================================

    public async Task GenerateMatchesAsync(long importId, CancellationToken cancellationToken = default)
    {
        var candidates = await _repository.GetCobranzasForMatchingAsync(importId, cancellationToken);
        if (candidates.Count == 0) return;

        // Carga aliases una vez y reutiliza en memoria para todos los movimientos
        var aliases = await _repository.GetClientAliasesAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            var (clientCode, clientName, clientPoints, clientResolution) =
                await ResolveClientAsync(candidate, aliases, cancellationToken);

            IReadOnlyCollection<PendingDocumentDto> pendingDocs = Array.Empty<PendingDocumentDto>();
            if (clientCode is not null)
            {
                pendingDocs = await _repository.GetPendingDocumentsAsync(clientCode, cancellationToken);
            }

            var (matchType, applications, matchPoints) =
                await BuildMatchAsync(candidate, clientCode, pendingDocs, cancellationToken);

            var confidence = Math.Min(clientPoints + matchPoints, 99m);

            var proposal = new MovementMatchProposal
            {
                MovementId      = candidate.MovementId,
                MatchingType    = matchType,
                ClientCode      = clientCode  ?? string.Empty,
                ClientName      = clientName  ?? string.Empty,
                Confidence      = confidence,
                ProposalSummary = clientResolution=="AMBIGUOUS_CLIENTS" ? "Varios clientes posibles: compará sus comprobantes e importes y asigná el cliente manualmente." : BuildSummary(matchType, clientName, candidate.Amount, applications),
                EvidenceJson    = BuildEvidence(candidate, clientResolution, clientPoints, matchType, matchPoints, applications),
                Status          = "PENDING"
            };

            await _repository.SaveMatchAsync(proposal, applications, cancellationToken);
        }
    }

    // =========================================================================
    // 1. Resolución de cliente
    // =========================================================================

    private async Task<(string? code, string? name, decimal points, string resolution)>
        ResolveClientAsync(
            CobranzaCandidateDto candidate,
            IReadOnlyCollection<ClientAliasDto> aliases,
            CancellationToken cancellationToken)
    {
        if(candidate.ClientManuallyAssigned && !string.IsNullOrWhiteSpace(candidate.ClassifiedClientCode))
            return (candidate.ClassifiedClientCode,candidate.ClassifiedClientName,50m,"MANUAL_CLIENT");

        async Task<(string? code,string? name,decimal points,string resolution)> ResolverGrupo(
            IEnumerable<ClientSearchResultDto> origen,decimal puntos,string motivo)
        {
            var opciones=origen.DistinctBy(c=>c.ClientCode.Trim()).ToList();
            // El alias o nombre puede apuntar a una sola cuenta de un titular con varias empresas.
            foreach(var cuit in opciones.Select(c=>CleanCuit(c.Cuit ?? "")).Where(c=>c.Length==11).Distinct().ToArray())
                opciones.AddRange(await _repository.SearchClientByNameAsync("",cuit,cancellationToken));
            opciones=opciones.DistinctBy(c=>c.ClientCode.Trim()).ToList();
            if(opciones.Count==1) return (opciones[0].ClientCode,opciones[0].ClientName,puntos,motivo);
            var exactos=new List<ClientSearchResultDto>();
            foreach(var cliente in opciones)
            {
                var docs=await _repository.GetPendingDocumentsAsync(cliente.ClientCode,cancellationToken);
                if(docs.Any(d=>d.PendingBalance>0 && Math.Abs(d.PendingBalance-candidate.Amount)<=ExactTolerance))
                    exactos.Add(cliente);
            }
            if(exactos.Count==1)
                return (exactos[0].ClientCode,exactos[0].ClientName,45m,"SHARED_ID_AMOUNT_MATCH");
            return (null,null,0m,"AMBIGUOUS_CLIENTS");
        }
        Task<(string? code,string? name,decimal points,string resolution)> ResolverAliases(IEnumerable<ClientAliasDto> encontrados,decimal puntos,string motivo)
            => ResolverGrupo(encontrados.Select(a=>new ClientSearchResultDto {
                ClientCode=a.ClientCode,ClientName=NombreAlias(a),Cuit=a.ClientTaxId ?? a.Cuit }),puntos,motivo);

        var (extractedCuit,extractedName)=ExtractCuitAndNameFromDescription(candidate.Description,candidate.CounterpartyName,candidate.CounterpartyTaxId);
        var cuitClean=CleanCuit(extractedCuit ?? "");
        if(cuitClean.Length==11)
        {
            var porCuit=aliases.Where(a=>CleanCuit(a.Cuit ?? "")==cuitClean).ToList();
            if(porCuit.Count>0) return await ResolverAliases(porCuit,50m,"CUIT_MATCH");
            var clientesCuit=await _repository.SearchClientByNameAsync("",cuitClean,cancellationToken);
            if(clientesCuit.Count>0) return await ResolverGrupo(clientesCuit,50m,"DB_CUIT_MATCH");
        }
        var normalizedCounterpart=Normalize(candidate.CounterpartyName);
        if(normalizedCounterpart.Length>0)
        {
            var exactos=aliases.Where(a=>Normalize(a.AliasText)==normalizedCounterpart).ToList();
            if(exactos.Count>0) return await ResolverAliases(exactos,42m,"ALIAS_EXACT");
            var parciales=aliases.Where(a=>Normalize(a.AliasText).Length>=5 &&
                (Normalize(a.AliasText).Contains(normalizedCounterpart,StringComparison.Ordinal) ||
                    normalizedCounterpart.Contains(Normalize(a.AliasText),StringComparison.Ordinal))).ToList();
            if(parciales.Count>0) return await ResolverAliases(parciales,35m,"ALIAS_PARTIAL");
        }
        var normalizedDesc=Normalize(candidate.Description);
        var descAliases=aliases.Where(a=>Normalize(a.AliasText).Length>=5 &&
            normalizedDesc.Contains(Normalize(a.AliasText),StringComparison.Ordinal)).ToList();
        if(descAliases.Count>0) return await ResolverAliases(descAliases,30m,"DESC_PARTIAL");
        var dbMatches=await _repository.SearchClientByNameAsync(extractedName,extractedCuit,cancellationToken);
        if(dbMatches.Count>0) return await ResolverGrupo(dbMatches,dbMatches.First().MatchScore==100 ? 50m : 32m,"DB_NAME_MATCH");
        if(!string.IsNullOrWhiteSpace(candidate.ClassifiedClientCode))
            return (candidate.ClassifiedClientCode,candidate.ClassifiedClientCode,28m,"CLASSIFICATION");
        return (null,null,0m,"NOT_FOUND");
    }

    // =========================================================================
    // 2. Construcción del match
    // =========================================================================

    private async Task<(MatchingType type, List<MovementApplicationDto> apps, decimal points)>
        BuildMatchAsync(
            CobranzaCandidateDto candidate,
            string? clientCode,
            IReadOnlyCollection<PendingDocumentDto> docs,
            CancellationToken cancellationToken)
    {
        var amount = candidate.Amount;
        if (clientCode is null)
            return (MatchingType.NoIdentificado, new List<MovementApplicationDto>(), 0m);
        // Una coincidencia de cobro anterior es solo una advertencia para revisión manual.
        if (await _repository.HasRecentlyRegisteredPaymentAsync(clientCode, amount, candidate.MovementDate, cancellationToken))
            return (MatchingType.CobranzaYaRegistrada, new List<MovementApplicationDto>(), 20m);

        if (docs.Count == 0)
        {
            // Cliente identificado pero sin comprobantes pendientes:
            // verificar si ya fue ingresada la cobranza en Alfa.
            if (clientCode is not null)
            {
                var yaRegistrada = await _repository.HasRecentlyRegisteredPaymentAsync(
                    clientCode, amount, candidate.MovementDate, cancellationToken);

                if (yaRegistrada)
                    return (MatchingType.CobranzaYaRegistrada, new List<MovementApplicationDto>(), 20m);
            }

            return (MatchingType.Anticipo, new List<MovementApplicationDto>(), clientCode is not null ? 15m : 0m);
        }

        // a) Match exacto (un solo comprobante)
        var exactDoc = docs.FirstOrDefault(d =>
            Math.Abs(d.PendingBalance - amount) <= ExactTolerance);

        if (exactDoc is not null)
        {
            return (MatchingType.Exacto,
                new List<MovementApplicationDto>
                {
                    ToApplication(exactDoc, amount, isPartial: false)
                },
                50m);
        }

        // b) Match múltiple (2 comprobantes)
        var multiPair = FindMultipleMatch(amount, docs, maxDocs: 2);
        if (multiPair is not null)
        {
            return (MatchingType.Multiple,
                multiPair.Select(d => ToApplication(d, d.PendingBalance, isPartial: false))
                         .ToList(),
                45m);
        }

        // c) Match múltiple (3-4 comprobantes)
        var multiMore = FindMultipleMatch(amount, docs, maxDocs: MaxDocsMultiple);
        if (multiMore is not null)
        {
            return (MatchingType.Multiple,
                multiMore.Select(d => ToApplication(d, d.PendingBalance, isPartial: false))
                         .ToList(),
                40m);
        }

        // d) Match parcial (el importe cubre parte de un comprobante)
        //    Elegimos el comprobante con saldo más cercano por encima del importe
        var partialDoc = docs
            .Where(d => d.PendingBalance > amount)
            .OrderBy(d => d.PendingBalance - amount)
            .FirstOrDefault();

        if (partialDoc is not null)
        {
            return (MatchingType.Parcial,
                new List<MovementApplicationDto>
                {
                    ToApplication(partialDoc, amount, isPartial: true)
                },
                30m);
        }

        // El importe supera todos los saldos individuales y no hay combinación →
        // anticipo (pago adelantado o diferencia de criterio)
        return (MatchingType.Anticipo, new List<MovementApplicationDto>(), 10m);
    }

    /// <summary>
    /// Búsqueda de combinación de comprobantes cuya suma iguale al importe.
    /// Algoritmo greedy + combinatorial limitado para evitar explosión de combinaciones.
    /// </summary>
    private List<PendingDocumentDto>? FindMultipleMatch(
        decimal amount,
        IReadOnlyCollection<PendingDocumentDto> docs,
        int maxDocs)
    {
        // Ordenar por vencimiento (más antiguo primero = prioridad de cobro)
        var sorted = docs.OrderBy(d => d.DueDate ?? DateTime.MaxValue)
                         .ThenByDescending(d => d.PendingBalance)
                         .ToList();

        return FindCombination(sorted, amount, maxDocs, MultipleTolerance);
    }

    private static List<PendingDocumentDto>? FindCombination(
        List<PendingDocumentDto> docs,
        decimal target,
        int maxSize,
        decimal tolerance)
    {
        // Limitamos la búsqueda a los primeros 12 documentos para evitar
        // explosión combinatorial (C(12,4) = 495 combinaciones, manejable)
        var candidates = docs.Take(12).ToList();
        int n = candidates.Count;

        for (int size = 2; size <= Math.Min(maxSize, n); size++)
        {
            var result = CombineRecursive(candidates, target, tolerance, size, 0, new List<PendingDocumentDto>());
            if (result is not null) return result;
        }

        return null;
    }

    private static List<PendingDocumentDto>? CombineRecursive(
        List<PendingDocumentDto> docs,
        decimal target,
        decimal tolerance,
        int remaining,
        int startIndex,
        List<PendingDocumentDto> current)
    {
        if (remaining == 0)
        {
            var sum = current.Sum(d => d.PendingBalance);
            return Math.Abs(sum - target) <= tolerance ? new List<PendingDocumentDto>(current) : null;
        }

        for (int i = startIndex; i <= docs.Count - remaining; i++)
        {
            current.Add(docs[i]);
            var found = CombineRecursive(docs, target, tolerance, remaining - 1, i + 1, current);
            if (found is not null) return found;
            current.RemoveAt(current.Count - 1);
        }

        return null;
    }

    // =========================================================================
    // Helpers de construcción
    // =========================================================================

    private static MovementApplicationDto ToApplication(
        PendingDocumentDto doc,
        decimal appliedAmount,
        bool isPartial) => new()
    {
        DocumentType   = doc.DocumentType,
        Letter         = doc.Letter,
        EmissionCenter = doc.EmissionCenter,
        DocumentNumber = doc.DocumentNumber,
        Installment    = doc.Installment,
        AppliedAmount  = appliedAmount,
        PendingBalance = doc.PendingBalance,
        IsPartial      = isPartial
    };

    private static string BuildSummary(
        MatchingType type,
        string? clientName,
        decimal amount,
        List<MovementApplicationDto> apps)
    {
        var client = string.IsNullOrEmpty(clientName) ? "cliente no identificado" : clientName;
        var amountStr = amount.ToString("N2");

        return type switch
        {
            MatchingType.Exacto   =>
                $"Exacto — {client} — ${amountStr} → {apps[0].DocumentType} {apps[0].Letter} {apps[0].EmissionCenter:D4}-{apps[0].DocumentNumber:D8}",
            MatchingType.Multiple =>
                $"Múltiple — {client} — ${amountStr} → {apps.Count} comprobantes: " +
                string.Join(", ", apps.Select(a => $"{a.DocumentType}{a.Letter} {a.DocumentNumber:D8}")),
            MatchingType.Parcial  =>
                $"Parcial — {client} — ${amountStr} de ${apps[0].PendingBalance:N2} en " +
                $"{apps[0].DocumentType} {apps[0].Letter} {apps[0].EmissionCenter:D4}-{apps[0].DocumentNumber:D8}",
            MatchingType.Anticipo =>
                $"Anticipo — {client} — ${amountStr} sin comprobantes pendientes identificados",
            MatchingType.CobranzaYaRegistrada =>
                $"Ya registrada — {client} — ${amountStr} posiblemente ingresada en Alfa (revisar)",
            _ =>
                $"No identificado — ${amountStr}"
        };
    }

    private static string BuildEvidence(
        CobranzaCandidateDto candidate,
        string clientResolution,
        decimal clientPoints,
        MatchingType matchType,
        decimal matchPoints,
        List<MovementApplicationDto> apps)
    {
        var evidence = new
        {
            clientResolution,
            clientPoints,
            matchType    = matchType.ToString(),
            matchPoints,
            totalScore   = clientPoints + matchPoints,
            counterparty = new
            {
                name  = candidate.CounterpartyName,
                taxId = candidate.CounterpartyTaxId
            },
            documents = apps.Select(a => new
            {
                type    = a.DocumentType,
                letter  = a.Letter,
                center  = a.EmissionCenter,
                number  = a.DocumentNumber,
                installment = a.Installment,
                applied = a.AppliedAmount,
                pending = a.PendingBalance,
                partial = a.IsPartial
            })
        };

        return JsonSerializer.Serialize(evidence, new JsonSerializerOptions
        {
            WriteIndented = false
        });
    }

    // =========================================================================
    // Normalización de texto
    // =========================================================================

    internal static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString().Trim();
    }

    private static string CleanCuit(string cuit) =>
        ContraparteBancaria.NormalizarCuit(cuit);

    // =========================================================================
    // Extracción de CUIT y nombre desde texto de descripción
    // =========================================================================

    private static string NombreAlias(ClientAliasDto alias)
        => !string.IsNullOrWhiteSpace(alias.ClientName) && alias.ClientName.Any(char.IsLetter)
            ? alias.ClientName.Trim() : alias.AliasText.Any(char.IsLetter) ? alias.AliasText.Trim() : alias.ClientCode;

    private static (string? cuit, string name) ExtractCuitAndNameFromDescription(
        string description, string counterpartyName, string counterpartyTaxId)
    {
        var extraida=ContraparteBancaria.Extraer(description);
        return (!string.IsNullOrWhiteSpace(counterpartyTaxId) ? counterpartyTaxId : extraida.Cuit,
            !string.IsNullOrWhiteSpace(extraida.Nombre) ? extraida.Nombre : counterpartyName);
    }
}
