using System.Globalization;
using System.Text;
using System.Text.Json;
using AlfaCore.Models;
using QRCoder;

namespace AlfaCore.Services;

/// <summary>Generación local según QRespecificaciones de ARCA, sin COM, archivos ni llamadas fiscales.</summary>
public sealed class ArcaQrService : IArcaQrService
{
    public byte[] GeneratePng(ArcaQrData data)
    {
        using var generator = new QRCodeGenerator();
        using var qr = generator.CreateQrCode(BuildUrl(data), QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(qr);
        return png.GetGraphic(8);
    }

    internal static string BuildUrl(ArcaQrData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var cuit = Digits(data.Cuit, 11, 11, "CUIT emisor");
        var autorizacion = Digits(data.Autorizacion, 14, 14, "autorización");
        if (data.Fecha == default || data.PuntoVenta is < 1 or > 99999
            || data.TipoComprobante is < 1 or > 999 || data.Numero is < 1 or > 99999999)
            throw new ArgumentException("Los datos de identificación del comprobante no son válidos para el QR.");
        if (data.Importe < 0 || data.Importe >= 10000000000000m || decimal.Round(data.Importe, 2) != data.Importe
            || data.Cotizacion <= 0 || data.Cotizacion >= 10000000000000m || decimal.Round(data.Cotizacion, 6) != data.Cotizacion)
            throw new ArgumentException("El importe o la cotización no son válidos para el QR.");
        if (data.Moneda is not ("PES" or "DOL" or "EUR") || (data.Moneda == "PES" && data.Cotizacion != 1))
            throw new ArgumentException("La moneda no está soportada o su cotización no es válida para el QR.");
        if (data.TipoAutorizacion is not ("E" or "A"))
            throw new ArgumentException("El tipo de autorización del QR debe identificar CAE o CAEA.");

        var payload = new Dictionary<string, object>
        {
            ["ver"] = 1, ["fecha"] = data.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["cuit"] = cuit, ["ptoVta"] = data.PuntoVenta, ["tipoCmp"] = data.TipoComprobante,
            ["nroCmp"] = data.Numero, ["importe"] = data.Importe, ["moneda"] = data.Moneda,
            ["ctz"] = data.Cotizacion
        };
        if (data.TipoDocumentoReceptor.HasValue)
        {
            if (data.TipoDocumentoReceptor is < 0 or > 99)
                throw new ArgumentException("El tipo de documento receptor no es válido para el QR.");
            payload["tipoDocRec"] = data.TipoDocumentoReceptor.Value;
            payload["nroDocRec"] = Digits(data.NumeroDocumentoReceptor, 1, 20, "documento receptor");
        }
        else if (!string.IsNullOrWhiteSpace(data.NumeroDocumentoReceptor))
            throw new ArgumentException("Falta el tipo de documento receptor para el QR.");
        payload["tipoCodAut"] = data.TipoAutorizacion;
        payload["codAut"] = autorizacion;
        return "https://www.arca.gob.ar/fe/qr/?p=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
    }

    private static decimal Digits(string? value, int min, int max, string field)
    {
        var normalized = (value ?? "").Replace("-", "").Replace("/", "").Replace(" ", "").Trim();
        if (normalized.Length < min || normalized.Length > max || normalized.Any(c => c is < '0' or > '9')
            || !decimal.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            throw new ArgumentException($"El campo {field} no es válido para el QR.");
        return number;
    }
}
