namespace AlfaCore.Models;

public sealed class PuntoVentaNpListadoFiltro
{
    public string Buscar { get; init; } = string.Empty;
    public string Estado { get; init; } = string.Empty;
    public DateTime? Desde { get; init; }
    public DateTime? Hasta { get; init; }
    public string Sucursal { get; init; } = string.Empty;
    public string Unidad { get; init; } = string.Empty;
    public bool IncluirAnuladas { get; init; }
    public int Pagina { get; init; } = 1;
    public int TamanoPagina { get; init; } = 20;
}

public sealed class PuntoVentaNpResumenDto
{
    public int Id { get; init; }
    public string IdComprobante { get; init; } = string.Empty;
    public string Cuenta { get; init; } = string.Empty;
    public string Cliente { get; init; } = string.Empty;
    public DateTime? Fecha { get; init; }
    public DateTime? FechaEntrega { get; init; }
    public string Telefono { get; init; } = string.Empty;
    public decimal ImporteComercial { get; init; }
    public decimal? TotalAnticiposVigentes { get; init; }
    public bool TotalAnticiposVerificado { get; init; }
    public string EstadoAnticipos { get; init; } = PuntoVentaAnticiposEstados.Verificado;
    public string EstadoComprobante { get; init; } = string.Empty;
    public string EstadoProduccion { get; init; } = string.Empty;
}

public sealed class PuntoVentaNpPaginaDto
{
    public IReadOnlyList<PuntoVentaNpResumenDto> Items { get; init; } = [];
    public int Total { get; init; }
    public int Pagina { get; init; }
    public int TamanoPagina { get; init; }
    public int TotalPaginas => TamanoPagina <= 0 ? 0 : (int)Math.Ceiling(Total / (double)TamanoPagina);
}

public sealed class PuntoVentaNpAnticiposDto
{
    public int Id { get; init; }
    public string Tc { get; init; } = "NP";
    public string IdComprobante { get; init; } = string.Empty;
    public string Cuenta { get; init; } = string.Empty;
    public string Cliente { get; init; } = string.Empty;
    public DateTime? Fecha { get; init; }
    public DateTime? FechaEntrega { get; init; }
    public DateTime? FechaPreparacion { get; init; }
    public string Telefono { get; init; } = string.Empty;
    public string Observaciones { get; init; } = string.Empty;
    public decimal ImporteComercial { get; init; }
    public string EstadoComprobante { get; init; } = string.Empty;
    public IReadOnlyList<PuntoVentaNpCobranzaDto> Cobranzas { get; init; } = [];
    public decimal? TotalAnticiposVigentes { get; init; }
    public bool TotalAnticiposVerificado { get; init; }
    public string EstadoAnticipos { get; init; } = PuntoVentaAnticiposEstados.Verificado;
    public string EstadoProduccion { get; init; } = string.Empty;
    public IReadOnlyList<ComprobanteAccionDto> Acciones { get; init; } = [];
    public IReadOnlyList<PuntoVentaNpItemDto> Items { get; init; } = [];
}

public sealed class PuntoVentaNpItemDto
{
    public string IdArticulo { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public decimal Cantidad { get; init; }
    public decimal Precio { get; init; }
    public decimal Importe { get; init; }
    public decimal CantidadPreparada { get; init; }
    public string Presentacion { get; init; } = string.Empty;
    public string Observaciones { get; init; } = string.Empty;
}

public sealed class PuntoVentaNpCobranzaDto
{
    public int Id { get; init; }
    public int IdComplemento { get; init; }
    public string Tc { get; init; } = string.Empty;
    public string IdComprobante { get; init; } = string.Empty;
    public string PrVenta { get; init; } = string.Empty;
    public string Observaciones { get; init; } = string.Empty;
    public string Cuenta { get; init; } = string.Empty;
    public DateTime? Fecha { get; init; }
    public decimal ImporteOriginal { get; init; }
    public bool Anulada { get; init; }
    public bool Vigente => !Anulada;
    public decimal? ImporteAplicado { get; init; }
    public decimal? ImporteDisponible { get; init; }
    public bool ImporteDisponibleVerificado { get; init; }
    public string EstadoDisponible { get; init; } = PuntoVentaAnticiposEstados.Verificado;
    public string TipoRelacion { get; init; } = PuntoVentaAnticiposTiposRelacion.CandidataNoVerificada;
    public bool RelacionVerificada { get; init; }
    public string? AdvertenciaRelacion { get; init; }
    public bool PuedeUsarseComoAnticipo => RelacionVerificada && !Anulada
        && ImporteDisponibleVerificado && ImporteDisponible.HasValue;
    public IReadOnlyList<PuntoVentaNpAplicacionDto> Aplicaciones { get; init; } = [];
}

public sealed class PuntoVentaNpAplicacionDto
{
    public string TcCobranza { get; init; } = string.Empty;
    public string IdCobranza { get; init; } = string.Empty;
    public string TcDestino { get; init; } = string.Empty;
    public string IdDestino { get; init; } = string.Empty;
    public decimal Importe { get; init; }
}

public static class PuntoVentaAnticiposEstados
{
    public const string Verificado = "VERIFICADO";
    public const string NoVerificado = "NO_VERIFICADO";
    public const string Anulado = "ANULADO";
}

public static class PuntoVentaAnticiposTiposRelacion
{
    public const string PrVenta = "RELACION_PRVENTA";
    public const string ObservacionesLegacy = "RELACION_OBSERVACIONES_LEGACY";
    public const string PrVentaYObservaciones = "RELACION_PRVENTA_Y_OBSERVACIONES";
    public const string CandidataNoVerificada = "CANDIDATA_NO_VERIFICADA";
    public const string Conflicto = "CONFLICTO_RELACION";
}

public static class PuntoVentaAnticiposCalculador
{
    public static (string Tipo, bool Verificada, string? Advertencia) ClasificarRelacion(
        string? prVenta,
        string? observaciones,
        string idNp)
    {
        var esperado = (idNp ?? string.Empty).Trim();
        var prVentaNormalizado = (prVenta ?? string.Empty).Trim();
        var referenciaObservaciones = ExtraerReferenciaLegacy(observaciones);
        var porPrVenta = esperado.Length > 0
            && string.Equals(prVentaNormalizado, esperado, StringComparison.OrdinalIgnoreCase);
        var porObservaciones = esperado.Length > 0
            && string.Equals(referenciaObservaciones, esperado, StringComparison.OrdinalIgnoreCase);
        var prVentaApuntaAOtraNp = prVentaNormalizado.Length > 0 && !porPrVenta;
        var observacionesApuntanAOtraNp = referenciaObservaciones is not null && !porObservaciones;

        if ((porPrVenta && observacionesApuntanAOtraNp) || (porObservaciones && prVentaApuntaAOtraNp))
        {
            return (PuntoVentaAnticiposTiposRelacion.Conflicto, false,
                "PRVENTA y OBSERVACIONES identifican notas de pedido diferentes.");
        }
        if (porPrVenta && porObservaciones)
            return (PuntoVentaAnticiposTiposRelacion.PrVentaYObservaciones, true, null);
        if (porPrVenta)
            return (PuntoVentaAnticiposTiposRelacion.PrVenta, true, null);
        if (porObservaciones)
            return (PuntoVentaAnticiposTiposRelacion.ObservacionesLegacy, true, null);

        return (PuntoVentaAnticiposTiposRelacion.CandidataNoVerificada, false,
            "La cobranza no tiene una relación verificable con la nota de pedido.");
    }

    private static string? ExtraerReferenciaLegacy(string? observaciones)
    {
        var texto = (observaciones ?? string.Empty).Trim();
        const string prefijo = "Cob.NP ";
        if (!texto.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
            return null;

        var referencia = texto[prefijo.Length..].Trim();
        return referencia.Length == 0 || referencia.Any(char.IsWhiteSpace) ? null : referencia;
    }

    public static (decimal? Aplicado, decimal? Disponible, bool Verificado, string Estado) Calcular(
        decimal importeOriginal,
        bool anulada,
        IReadOnlyList<PuntoVentaNpAplicacionDto> aplicaciones)
    {
        if (anulada)
            return (0m, 0m, true, PuntoVentaAnticiposEstados.Anulado);

        if (importeOriginal < 0 || aplicaciones.Any(x => x.Importe < 0 ||
            string.IsNullOrWhiteSpace(x.TcCobranza) || string.IsNullOrWhiteSpace(x.IdCobranza) ||
            string.IsNullOrWhiteSpace(x.TcDestino) || string.IsNullOrWhiteSpace(x.IdDestino)))
            return (null, null, false, PuntoVentaAnticiposEstados.NoVerificado);

        var aplicado = aplicaciones.Sum(x => x.Importe);
        if (aplicado > importeOriginal)
            return (null, null, false, PuntoVentaAnticiposEstados.NoVerificado);

        return (aplicado, importeOriginal - aplicado, true, PuntoVentaAnticiposEstados.Verificado);
    }

    public static (decimal? Total, bool Verificado, string Estado) Totalizar(
        IReadOnlyList<PuntoVentaNpCobranzaDto> cobranzas)
    {
        var vigentes = cobranzas.Where(x => x.Vigente).ToList();
        if (vigentes.Any(x => !x.RelacionVerificada || !x.ImporteDisponibleVerificado || x.ImporteDisponible is null))
            return (null, false, PuntoVentaAnticiposEstados.NoVerificado);

        return (vigentes.Sum(x => x.ImporteDisponible!.Value), true, PuntoVentaAnticiposEstados.Verificado);
    }
}
