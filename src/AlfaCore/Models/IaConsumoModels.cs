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

/// <summary>Plan de créditos IA del cliente (módulo IA_CREDITOS con plan de tipo CREDITOS).</summary>
public sealed class IaPlanCreditosDto
{
    public int IdClienteModulo { get; set; }
    public string IdCliente { get; set; } = string.Empty;
    public string PlanNombre { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public string Moneda { get; set; } = string.Empty;
    public int CreditosIncluidos { get; set; }
    public bool PermiteExcedentes { get; set; }
    public decimal PrecioExcedente { get; set; }
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
    public List<IaConsumoFuncionDto> PorFuncion { get; set; } = [];
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
