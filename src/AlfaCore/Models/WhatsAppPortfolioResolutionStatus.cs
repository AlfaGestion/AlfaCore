namespace AlfaCore.Models;

/// <summary>
/// Clasificación explícita pedida en la auditoría de persistencia de Portfolio (2026-09-15): para un
/// número dado, ¿de dónde sale (o podría salir) "Portfolio: &lt;nombre&gt;"?
/// </summary>
public enum WhatsAppPortfolioResolutionStatus
{
    /// <summary>
    /// Sin MetaBusinessId en el número Y sin NINGUNA vía segura para reconstruirlo: ni ownership central
    /// (nunca pasó por Embedded Signup) ni una WABA legacy configurada
    /// (ConversacionWhatsAppConfigDto.BusinessAccountId vacío -- ni siquiera hay algo que preguntarle a
    /// Meta). "Agregado manualmente por Phone Number ID" NO implica Unknown por sí solo -- ver
    /// Resolvable. No hay nada que reconstruir ni que resolver: se queda así para siempre. La UI no debe
    /// mostrar ninguna línea de portfolio. Transición: ninguna automática -- sólo cambiaría si se
    /// configura una WABA legacy o el número se reconecta vía Embedded Signup.
    /// </summary>
    Unknown,

    /// <summary>
    /// Elegible para completarse SIN intervención del cliente, en alguno de tres sentidos:
    /// (a) el número no tiene MetaBusinessId pero SÍ hay ownership central (WhatsAppPhoneOwnership -&gt;
    ///     WhatsAppWabaOwnership) para reconstruirlo -- se resuelve en el próximo refresh de la lista,
    ///     sin Meta (ver WhatsAppPortfolioResolutionService.BackfillNumeroMetaIdentityAsync);
    /// (b) el número es MANUAL (sin ownership central) pero SÍ hay una WABA legacy configurada
    ///     (ConversacionWhatsAppConfigDto.BusinessAccountId) -- elegible para un intento throttled que
    ///     descubre qué Business es dueño de esa WABA (ver TryResolveLegacyWabaOwningBusinessAsync),
    ///     compartido por todos los números manuales de la base (una sola WABA legacy por token);
    /// (c) el número ya tiene MetaBusinessId (de (a) o (b)) pero el nombre todavía no está cacheado --
    ///     elegible para un intento throttled contra Meta (ver TryResolvePortfolioNameAsync).
    /// La UI muestra el fallback "Portfolio: no identificado" mientras tanto -- nunca omite la línea,
    /// porque SÍ hay o SÍ podría haber un portfolio, a diferencia de Unknown.
    /// Transición a Known: automática, en el próximo refresh de la lista donde el backfill/throttle
    /// tenga éxito -- nunca requiere que el cliente reconecte ni reonboardee nada.
    /// </summary>
    Resolvable,

    /// <summary>
    /// MetaBusinessId conocido Y nombre cacheado (CONV_WHATSAPP_BUSINESS_PORTFOLIOS.PortfolioName no
    /// vacío). La UI muestra "Portfolio: &lt;nombre&gt;". Se actualiza (mismo MetaBusinessId, nunca una
    /// fila nueva -- MERGE sobre la PK) si Meta devuelve un nombre distinto en una resolución posterior,
    /// pero eso no ocurre por render: sólo cuando el throttle vuelve a permitir un intento.
    /// </summary>
    Known,
}

/// <summary>Resolver puro: no toca DB ni Meta, sólo clasifica según lo que el llamador ya averiguó.</summary>
public static class WhatsAppPortfolioResolutionStatusExtensions
{
    /// <param name="hasReconstructiblePath">True si hay AL MENOS UNA vía segura para llegar a un
    /// MetaBusinessId sin pedirle nada al cliente: ownership central (WhatsAppPhoneOwnership) O una WABA
    /// legacy configurada (ConversacionWhatsAppConfigDto.BusinessAccountId no vacío) -- no importa cuál
    /// de las dos, ambas terminan en Resolvable con el mismo significado para el cliente.</param>
    public static WhatsAppPortfolioResolutionStatus Classify(bool hasMetaBusinessId, bool hasReconstructiblePath, bool hasCachedName)
    {
        if (hasMetaBusinessId && hasCachedName)
            return WhatsAppPortfolioResolutionStatus.Known;
        if (hasMetaBusinessId || hasReconstructiblePath)
            return WhatsAppPortfolioResolutionStatus.Resolvable;
        return WhatsAppPortfolioResolutionStatus.Unknown;
    }
}
