namespace AlfaCore.Models;

public sealed class SaldoVentasFilaDto
{
    public string Cuenta { get; init; } = "";
    public string RazonSocial { get; init; } = "";
    public decimal Saldo { get; init; }
    public decimal SaldoGlobal { get; init; }
    public decimal LimiteCredito { get; init; }
    public decimal CreditoDisponible { get; init; }
    public decimal Exceso { get; init; }
}

public sealed class SaldosVentasResultDto
{
    public IReadOnlyList<SaldoVentasFilaDto> Items { get; init; } = [];
    public int TotalRegistros { get; init; }
    public decimal SaldoTotal { get; init; }
    public decimal SaldoGlobalTotal { get; init; }
    public decimal LimiteTotal { get; init; }
    public decimal DisponibleTotal { get; init; }
    public decimal ExcesoTotal { get; init; }
    public int PaginaActual { get; init; }
    public int TamanioPagina { get; init; }
    public int TotalPaginas => TamanioPagina>0 ? (int)Math.Ceiling((double)TotalRegistros/TamanioPagina) : 0;
}
