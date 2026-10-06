namespace AlfaCore.Models;

/// <summary>Consumo de IA de una función en un período.</summary>
public sealed class IaConsumoFuncionDto
{
    public string Funcion { get; set; } = string.Empty;
    public long Llamadas { get; set; }
    public decimal CostoUsd { get; set; }
    public long Creditos { get; set; }
    public long LlamadasSinPrecio { get; set; }
}

/// <summary>
/// Plan de créditos IA del cliente (módulo IA_CREDITOS con plan de tipo CREDITOS). Si el cliente no
/// tiene uno asignado, aplica el plan por defecto (IA_CONFIG.PLAN_DEFAULT_CODIGO) con
/// <see cref="PorDefecto"/> = true y sin <see cref="IdClienteModulo"/>.
/// </summary>
public sealed class IaPlanCreditosDto
{
    public int IdClienteModulo { get; set; }
    public string IdCliente { get; set; } = string.Empty;
    public int IdPlan { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string PlanNombre { get; set; } = string.Empty;
    public bool PorDefecto { get; set; }
    public decimal Precio { get; set; }
    public string Moneda { get; set; } = string.Empty;
    public int CreditosIncluidos { get; set; }
    public bool PermiteExcedentes { get; set; }
    /// <summary>Precio por cada 1.000 créditos que superan los incluidos.</summary>
    public decimal PrecioExcedente { get; set; }
}

/// <summary>Plan de créditos del catálogo (para elegir el plan por defecto o pedir un cambio de plan).</summary>
public sealed class IaPlanOpcionDto
{
    public int IdPlan { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public string Moneda { get; set; } = string.Empty;
    public int CreditosIncluidos { get; set; }
    public bool PermiteExcedentes { get; set; }
    /// <summary>Precio por cada 1.000 créditos adicionales.</summary>
    public decimal PrecioExcedente { get; set; }
    public bool Activo { get; set; }
    public bool VisibleCatalogo { get; set; }
}

/// <summary>Configuración de los créditos IA (IA_CONFIG), editable en Administrar → Consumo IA.</summary>
public sealed class IaConfigCreditosDto
{
    public decimal UsdPorCredito { get; set; } = 0.001m;
    /// <summary>Código del plan que aplica a los clientes sin plan asignado; vacío = ninguno.</summary>
    public string PlanDefaultCodigo { get; set; } = string.Empty;
    /// <summary>Tope automático de los planes con excedentes = incluidos × factor; 0 = sin tope automático.</summary>
    public decimal TopeFactorExcedentes { get; set; } = 2m;
    public int AvisoPorcentaje { get; set; } = 80;
    /// <summary>Id del módulo IA_CREDITOS (para abrir la pantalla de planes); 0 si no existe.</summary>
    public int IdModulo { get; set; }
    public List<IaPlanOpcionDto> Planes { get; set; } = [];
}

/// <summary>Planes que el cliente puede pedir desde Asistente IA → General.</summary>
public sealed class IaPlanesClienteDto
{
    public int? IdPlanActual { get; set; }
    public List<IaPlanOpcionDto> Planes { get; set; } = [];
    public IaSolicitudPlanDto? Pendiente { get; set; }
}

/// <summary>Pedido de cambio de plan de créditos (IA_SOLICITUD_PLAN).</summary>
public sealed class IaSolicitudPlanDto
{
    public int Id { get; set; }
    public string IdCliente { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public int IdPlan { get; set; }
    public string PlanNombre { get; set; } = string.Empty;
    public int PlanCreditos { get; set; }
    public string PlanActual { get; set; } = string.Empty;
    public int PlanActualCreditos { get; set; }
    public string SolicitadoPor { get; set; } = string.Empty;
    public DateTime SolicitadoUtc { get; set; }
    /// <summary>El plan pedido incluye menos créditos que el actual.</summary>
    public bool EsBaja => PlanCreditos < PlanActualCreditos;
}

public static class IaTopeOrigenes
{
    /// <summary>Tope cargado a mano para el cliente en Consumo IA.</summary>
    public const string Manual = "MANUAL";
    /// <summary>Tope automático según el plan (incluidos, o incluidos × factor si tiene excedentes).</summary>
    public const string Plan = "PLAN";
}

/// <summary>Consumo del mes de la base activa, para mostrarle al cliente.</summary>
public sealed class IaConsumoBaseDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public long CreditosBase { get; set; }
    public long CreditosCliente { get; set; }
    public decimal CostoUsdBase { get; set; }
    public IaPlanCreditosDto? Plan { get; set; }
    public IaTopeEstadoDto? Tope { get; set; }
    public List<IaConsumoFuncionDto> PorFuncion { get; set; } = [];
}

/// <summary>Estado del tope mensual de créditos IA del cliente (IA_TOPE_CREDITOS).</summary>
public sealed class IaTopeEstadoDto
{
    public string IdCliente { get; set; } = string.Empty;
    /// <summary><see cref="IaTopeOrigenes"/>: cargado a mano o automático según el plan.</summary>
    public string Origen { get; set; } = IaTopeOrigenes.Manual;
    public int TopeCreditos { get; set; }
    public int AvisoPorcentaje { get; set; } = 80;
    public long CreditosUsados { get; set; }
    public int Porcentaje { get; set; }
    /// <summary>Se alcanzó el tope: el asistente deja de responder y pasa a una persona.</summary>
    public bool Alcanzado { get; set; }
    /// <summary>Se superó el porcentaje de aviso (sin llegar al tope).</summary>
    public bool EnAviso { get; set; }
}

/// <summary>Fila de consumo por cliente para Administrar → Consumo IA.</summary>
public sealed class IaConsumoClienteDto
{
    public string IdCliente { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public int Bases { get; set; }
    public long Llamadas { get; set; }
    public decimal CostoUsd { get; set; }
    public long Creditos { get; set; }
    public long LlamadasSinPrecio { get; set; }
    public IaPlanCreditosDto? Plan { get; set; }
    public long CreditosExcedentes { get; set; }
    public decimal ImporteEstimado { get; set; }
    /// <summary>Tope vigente (manual o del plan); null = sin tope.</summary>
    public int? TopeCreditos { get; set; }
    public int AvisoPorcentaje { get; set; } = 80;
    /// <summary><see cref="IaTopeOrigenes"/> del tope vigente; vacío si no hay tope.</summary>
    public string TopeOrigen { get; set; } = string.Empty;
    /// <summary>Se cargó a mano "sin tope" (anula el tope automático del plan).</summary>
    public bool SinTopeManual { get; set; }
    public List<IaConsumoFuncionDto> PorFuncion { get; set; } = [];
}

public sealed class IaConsumoCentralDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public decimal UsdPorCredito { get; set; }
    public decimal CostoUsdTotal { get; set; }
    public long CreditosTotal { get; set; }
    public long LlamadasSinPrecio { get; set; }
    public long LlamadasSinCliente { get; set; }
    public List<IaConsumoClienteDto> Clientes { get; set; } = [];
}

/// <summary>Comparación del costo medido contra lo que factura OpenAI (API de costos de la organización).</summary>
public sealed class IaConciliacionDto
{
    public bool Disponible { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public decimal CostoOpenAiUsd { get; set; }
    public decimal CostoMedidoUsd { get; set; }
    public decimal DiferenciaUsd => CostoOpenAiUsd - CostoMedidoUsd;
}

public sealed class IaCargosResultadoDto
{
    public int Generados { get; set; }
    public int YaExistentes { get; set; }
    public int SinImporte { get; set; }
    public List<string> Detalle { get; set; } = [];
}
