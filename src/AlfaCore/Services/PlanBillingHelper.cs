using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>
/// Cálculo de la próxima fecha de cobro según <see cref="PlanTipoFacturacion"/> — compartido entre
/// <see cref="CentralAdminService.ContratarPlanAsync"/> (primer cobro, al contratar) y
/// <see cref="BillingService.RegistrarPagoManualAsync"/> (avance de período tras un pago). Ver
/// docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md, Fase 3.
/// </summary>
internal static class PlanBillingHelper
{
    /// <summary>
    /// Duración en días para <see cref="PlanTipoFacturacion.Dias"/> cuando el plan no cargó
    /// <c>CantidadIncluida</c>. El modelo de datos (planes_cargos_pagos_modelo_inicial.sql) no
    /// define un campo dedicado a "cantidad de días del ciclo" para este tipo de facturación —
    /// decisión tomada en la Fase 3: se reusa <c>Planes.CantidadIncluida</c> con este valor como
    /// último recurso si queda sin cargar.
    /// </summary>
    public const int DiasCicloPorDefecto = 30;

    /// <summary>
    /// Devuelve la próxima fecha de cobro a partir de <paramref name="baseUtc"/>, o <c>null</c> si
    /// el tipo de facturación no genera cobro automático recurrente (GRATIS/PAGO_UNICO/POR_USO/
    /// PAQUETE_USOS/CREDITOS — ninguno de los módulos del piloto los usa todavía).
    /// </summary>
    public static DateTime? CalcularProximoCobro(string tipoFacturacion, DateTime baseUtc, int? cantidadIncluidaDias)
        => tipoFacturacion switch
        {
            PlanTipoFacturacion.Mensual => baseUtc.AddMonths(1),
            PlanTipoFacturacion.Anual => baseUtc.AddYears(1),
            PlanTipoFacturacion.Dias => baseUtc.AddDays(cantidadIncluidaDias is > 0 ? cantidadIncluidaDias.Value : DiasCicloPorDefecto),
            _ => null
        };

    /// <summary>
    /// Si el tipo de facturación genera cobro automático recurrente (a diferencia de
    /// GRATIS/PAGO_UNICO/POR_USO/PAQUETE_USOS/CREDITOS). Usado para decidir si corresponde
    /// programar un próximo cobro al registrar un pago.
    /// </summary>
    public static bool EsRecurrente(string tipoFacturacion)
        => tipoFacturacion is PlanTipoFacturacion.Mensual or PlanTipoFacturacion.Anual or PlanTipoFacturacion.Dias;
}
