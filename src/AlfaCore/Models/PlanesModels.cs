namespace AlfaCore.Models;

/// <summary>
/// Catálogo de valores posibles de <c>dbo.Planes.TipoFacturacion</c> (mismo CHECK constraint que
/// define el modelo de datos — ver
/// docs/base-datos/sql-referencia/planes_cargos_pagos_modelo_inicial.sql). Define cómo
/// <see cref="ICentralAdminService.ContratarPlanAsync"/>/<see cref="IBillingService"/> calculan el
/// próximo cobro: MENSUAL/ANUAL/DIAS son recurrentes (llevan <c>FechaProximoCobro</c>);
/// GRATIS/PAGO_UNICO/POR_USO/PAQUETE_USOS/CREDITOS no generan cobro automático en esta fase.
/// </summary>
public static class PlanTipoFacturacion
{
    public const string Gratis = "GRATIS";
    public const string Mensual = "MENSUAL";
    public const string Anual = "ANUAL";
    public const string Dias = "DIAS";
    public const string PagoUnico = "PAGO_UNICO";
    public const string PorUso = "POR_USO";
    public const string PaqueteUsos = "PAQUETE_USOS";
    public const string Creditos = "CREDITOS";

    public static readonly IReadOnlyList<string> Todos =
    [
        Gratis, Mensual, Anual, Dias, PagoUnico, PorUso, PaqueteUsos, Creditos
    ];
}

/// <summary>Monedas soportadas hoy por Planes/Cargos/Pagos (CHECK constraint en las 3 tablas).</summary>
public static class MonedaValores
{
    public const string Ars = "ARS";
    public const string Usd = "USD";

    public static readonly IReadOnlyList<string> Todas = [Ars, Usd];
}

/// <summary>
/// Plan de comercialización de un módulo — ver <c>dbo.Planes</c>. Nota: los campos de agentes
/// (<see cref="CantidadAgentesIncluida"/>/<see cref="PrecioPorAgenteExcedente"/>) existen en el
/// modelo para poder cargarse desde la pantalla de administración, pero todavía no hay ningún
/// servicio que valide el cupo real de agentes contratados — queda pendiente de una investigación
/// aparte (ver docs/gestion/CONTINUIDAD_MODULOS_ADMINISTRAR.md).
/// </summary>
public sealed class PlanDto
{
    public int Id { get; init; }
    public int IdModulo { get; init; }
    public string Codigo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public string? Descripcion { get; init; }
    public string TipoFacturacion { get; init; } = PlanTipoFacturacion.Mensual;
    public decimal Precio { get; init; }
    public string Moneda { get; init; } = MonedaValores.Ars;
    public int DiasPrueba { get; init; }
    public int DiasGracia { get; init; }
    public int? CantidadIncluida { get; init; }
    public bool PermiteExcedentes { get; init; }
    public decimal? PrecioExcedente { get; init; }
    public int? CantidadAgentesIncluida { get; init; }
    public decimal? PrecioPorAgenteExcedente { get; init; }
    public bool RenovacionAutomaticaDefault { get; init; } = true;
    public bool Activo { get; init; } = true;
    public bool VisibleCatalogo { get; init; } = true;
    public DateTime CreadoUtc { get; init; }
    public DateTime? ModificadoUtc { get; init; }
}

/// <summary>Filtros de listado de planes — incluye paginación server-side (regla 27 de CODEX_RULES.md).</summary>
public sealed class PlanesFilters
{
    public int? IdModulo { get; set; }
    public bool? Activo { get; set; }
    public string Texto { get; set; } = string.Empty;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>
/// Formato corto de <see cref="PlanDto.TipoFacturacion"/> para mostrar el precio de un plan en
/// las landing pages públicas y en el selector de Verify.razor (ej. "ARS 150/mes"). Vive acá
/// (no en un componente Razor) porque se usa en 3 pantallas distintas — evita repetir el mismo
/// switch en cada una.
/// </summary>
public static class PlanDisplayHelper
{
    public static string FormatPeriodoCorto(string tipoFacturacion) => tipoFacturacion switch
    {
        PlanTipoFacturacion.Mensual => "/mes",
        PlanTipoFacturacion.Anual => "/año",
        PlanTipoFacturacion.Dias => "/ciclo",
        PlanTipoFacturacion.PagoUnico => " (pago único)",
        _ => string.Empty
    };
}

/// <summary>Alta o edición de un plan (Id nulo = alta).</summary>
public sealed class CrearPlanRequest
{
    public int? Id { get; set; }
    public int IdModulo { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string TipoFacturacion { get; set; } = PlanTipoFacturacion.Mensual;
    public decimal Precio { get; set; }
    public string Moneda { get; set; } = MonedaValores.Ars;
    public int DiasPrueba { get; set; }
    public int DiasGracia { get; set; }
    public int? CantidadIncluida { get; set; }
    public bool PermiteExcedentes { get; set; }
    public decimal? PrecioExcedente { get; set; }
    public int? CantidadAgentesIncluida { get; set; }
    public decimal? PrecioPorAgenteExcedente { get; set; }
    public bool RenovacionAutomaticaDefault { get; set; } = true;
    public bool VisibleCatalogo { get; set; } = true;
}
