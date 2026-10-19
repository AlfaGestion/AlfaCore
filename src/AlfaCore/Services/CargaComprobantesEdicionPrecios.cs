using System.Globalization;
using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

/// <summary>Permisos actuales y piso efectivo de precio, incluyendo descuento de renglón.</summary>
public sealed class CargaComprobantesEdicionPrecios
{
    public async Task<CargaComprobantesPoliticaPrecio> LeerAsync(SqlConnection cn, string usuario, string cliente,
        string tc, CancellationToken ct, SqlTransaction? tx = null)
    {
        var datos = await cn.QuerySingleAsync<Datos>(new CommandDefinition("""
            SELECT CONVERT(bit,ISNULL(u.V_ModificaPrecio,1)) Precio,CONVERT(bit,ISNULL(u.V_Descuentos,1)) Descuento,
                ISNULL(cond.DtoMaximo,0) Condicion,ISNULL(v.Dto_maximo,0) Vendedor
            FROM dbo.TA_USUARIOS u CROSS JOIN dbo.VT_CLIENTES cl
            JOIN dbo.V_TA_Cpra_Vta cond ON LTRIM(RTRIM(cond.IDCOND_CPRA_VTA))=LTRIM(RTRIM(cl.IDCOND_CPRA_VTA))
            LEFT JOIN dbo.V_TA_VENDEDORES v ON LTRIM(RTRIM(v.IdVendedor))=LTRIM(RTRIM(cl.IdVendedor))
            WHERE UPPER(LTRIM(RTRIM(u.NOMBRE)))=UPPER(@Usuario) AND LTRIM(RTRIM(cl.CODIGO))=@Cliente;
            """, new { Usuario = usuario, Cliente = cliente.Trim() }, tx, cancellationToken: ct));
        var cfg = (await cn.QueryAsync<Config>(new CommandDefinition("""
            SELECT UPPER(LTRIM(RTRIM(CLAVE))) Clave,
                COALESCE(NULLIF(LTRIM(RTRIM(VALOR)),''),CONVERT(nvarchar(max),ValorAux),'') Valor
            FROM dbo.TA_CONFIGURACION WHERE UPPER(LTRIM(RTRIM(CLAVE))) IN ('MAXDTO','MAXDTOPROFORMA');
            """, transaction: tx, cancellationToken: ct))).ToDictionary(x => x.Clave, x => x.Valor);
        decimal Leer(string clave)
        {
            var texto = cfg.GetValueOrDefault(clave);
            if (string.IsNullOrWhiteSpace(texto)) return 0;
            if (decimal.TryParse(texto.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var numero)) return numero;
            throw new CargaComprobantesReglaException("Revisá la configuración del descuento máximo; no es un porcentaje válido.");
        }
        var general = Leer("MAXDTO");
        return new(datos.Precio, datos.Descuento,
            ResolverMaximo(tc == "FP" ? Leer("MAXDTOPROFORMA") : general, datos.Condicion, datos.Vendedor),
            ResolverMaximo(general, datos.Condicion, datos.Vendedor));
    }

    public static decimal ResolverMaximo(decimal general, decimal condicion, decimal vendedor)
    {
        if (new[] { general, condicion, vendedor }.Any(x => x < 0 || x > 100))
            throw new CargaComprobantesReglaException("Los máximos de descuento requieren revisión (rango 0 a 100).");
        // CampoValida 19896-19908: condición cero con vendedor positivo conserva cero.
        // AnalizaPrecioForzado 21746-21772 aplica después condición/vendedor y tope general positivo.
        var maximo = condicion > 0 ? (vendedor > 0 ? Math.Min(condicion, vendedor) : condicion)
            : vendedor > 0 ? 0 : general;
        return general > 0 ? Math.Min(maximo, general) : maximo;
    }

    public static (decimal Neto, decimal Final, decimal Descuento) Aplicar(CargaComprobantesDirectaLinea linea,
        string tc, CargaComprobantesPoliticaPrecio politica, decimal referenciaNeto, decimal referenciaFinal,
        decimal tasa, int decimales)
    {
        if (tc is not ("FC" or "FP"))
            throw new CargaComprobantesReglaException("La edición manual de precios/descuentos está habilitada para facturas directas y proformas; las notas conservan su circuito de precios.");
        if ((linea.PrecioUnitarioNeto.HasValue && !politica.PuedeEditarPrecio) ||
            (linea.Descuento.HasValue && !politica.PuedeEditarDescuento))
            throw new CargaComprobantesReglaException("Tu usuario no tiene permiso para modificar el precio o el descuento solicitado.");
        var neto = linea.PrecioUnitarioNeto ?? referenciaNeto;
        var dto = linea.Descuento ?? 0;
        if (neto <= 0 || neto > 99999999.9999m || dto < 0 || dto >= 100 ||
            decimal.Round(neto,4) != neto || decimal.Round(dto,4) != dto || decimales is < 0 or > 4)
            throw new CargaComprobantesReglaException("Ingresá un precio neto positivo y un descuento entre 0 y menos de 100, con hasta cuatro decimales.");
        if (dto > (politica.MaximoDescuentoLinea ?? politica.MaximoDescuento))
            throw new CargaComprobantesReglaException("El porcentaje de descuento de la línea supera el máximo permitido.");
        // AnalizaPrecioForzado: precio neto formateado, descuento por artículo y tolerancias legacy.
        var efectivo = decimal.Round(decimal.Round(neto, decimales, MidpointRounding.ToEven) * (1 - dto / 100), 4, MidpointRounding.ToEven);
        var porCantidad = Math.Abs((referenciaNeto - efectivo) * linea.Cantidad);
        if (referenciaNeto <= 0 || (politica.MaximoDescuento != 100 && porCantidad > .1m &&
            decimal.Round(referenciaNeto * (1 - politica.MaximoDescuento / 100),1,MidpointRounding.ToEven) >
                decimal.Round(efectivo,1,MidpointRounding.ToEven) &&
            decimal.Round((referenciaNeto - efectivo) * 100 / referenciaNeto,2,MidpointRounding.ToEven) >
                decimal.Round(politica.MaximoDescuento,2,MidpointRounding.ToEven)))
            throw new CargaComprobantesReglaException($"El artículo {linea.Articulo} supera el descuento efectivo permitido ({politica.MaximoDescuento:0.####} %), incluyendo la rebaja del precio y el descuento de línea.");
        var final = linea.PrecioUnitarioNeto.HasValue
            ? ArticuloPrecioResolverService.SplitPrice(neto,tasa,false).conIva : referenciaFinal;
        return (neto, final, dto);
    }

    private sealed class Datos
    {
        public bool Precio { get; set; }
        public bool Descuento { get; set; }
        public decimal Condicion { get; set; }
        public decimal Vendedor { get; set; }
    }
    private sealed class Config { public string Clave { get; set; } = ""; public string Valor { get; set; } = ""; }
}
