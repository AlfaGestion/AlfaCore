using System.Globalization;
using AlfaCore.Models;

namespace AlfaCore.Services;

/// <summary>Controles locales del formato ordinario RG 4597; no consulta el padrón de ARCA.</summary>
internal static class LibroIvaArcaValidator
{
    internal static string Digitos(string valor) => new(valor.Where(c => c is >= '0' and <= '9').ToArray());

    internal static bool CuitValido(string valor)
    {
        if (valor.Any(c => !(c is >= '0' and <= '9' or '-' or ' '))) return false;
        var d = Digitos(valor);
        if (d.Length != 11 || d.Distinct().Count() == 1 || d == "99999999995") return false;
        int[] pesos = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
        var verificador = 11 - Enumerable.Range(0, 10).Sum(i => (d[i] - '0') * pesos[i]) % 11;
        return verificador != 10 && (verificador == 11 ? 0 : verificador) == d[10] - '0';
    }

    internal static IReadOnlyList<string> Validar(LibroIvaResultado r, Action<LibroIvaFila?, string>? observacion = null)
    {
        var errores = new List<string>();
        if (!CuitValido(r.CuitEmisor)) errores.Add("CUIT del informante inválido o no configurado.");
        foreach (var u in r.Unidades.Where(u => !CuitValido(u.Cuit)))
            errores.Add($"U.N. {u.Codigo}: CUIT del informante inválido o no configurado ({u.Cuit}).");
        if (r.Filas.Count == 0) errores.Add("El período no tiene movimientos.");
        if (r.Filtro.Desde.Year != r.Filtro.Hasta.Year || r.Filtro.Desde.Month != r.Filtro.Hasta.Month)
            errores.Add("La exportación ARCA debe corresponder a un único mes calendario.");
        foreach (var error in errores) observacion?.Invoke(null, error);
        var cuits = r.Unidades.ToDictionary(u => u.Codigo, u => Digitos(u.Cuit), StringComparer.OrdinalIgnoreCase);
        var claves = new HashSet<string>();
        foreach (var f in r.Filas)
        {
            void Error(string texto)
            {
                errores.Add($"U.N. {f.UnidadNegocio} · {f.Tc} {f.Letra} {f.Comprobante} · {texto}");
                observacion?.Invoke(f, texto);
            }
            if (f.CodigoArca == 0) Error("Tipo de comprobante sin equivalencia ARCA; no se permite omitirlo del archivo.");
            if (f.Fecha.Date < r.Filtro.Desde.Date || f.Fecha.Date > r.Filtro.Hasta.Date) Error("Fecha fuera del período.");
            if (f.PuntoVenta is < 1 or > 9997 || f.Numero <= 0) Error("Punto de venta o número inválido.");
            if (string.IsNullOrWhiteSpace(f.RazonSocial)) Error("Falta razón social.");
            var d = Digitos(f.Cuit);
            // Un documento corto (como el «1» que se carga en consumidor final) no es DNI: el TXT lo informa como sin identificar (99 / 0).
            var generico = d.Length == 0 || d.All(c => c == '0') || d == "99999999995";
            var requiereCuit = r.Filtro.Compras || f.Letra is "A" or "M";
            if (f.ConsumidorFinal && !r.Filtro.Compras && !requiereCuit) { /* consumidor final: ARCA no controla el número */ }
            else if (requiereCuit ? !CuitValido(f.Cuit) : !generico && (d.Length == 11 ? !CuitValido(f.Cuit) : d.Length > 8))
                Error($"Documento/CUIT inválido: {f.Cuit}.");
            if (f.Cuit.Any(c => !(c is >= '0' and <= '9' or '-' or ' '))) Error("El documento contiene caracteres inválidos.");
            var clave = $"{cuits.GetValueOrDefault(f.UnidadNegocio, Digitos(r.CuitEmisor))}|{f.CodigoArca}|{f.PuntoVenta}|{f.Numero}" + (r.Filtro.Compras ? $"|{d}" : "");
            if (!claves.Add(clave)) Error("Comprobante duplicado para el mismo CUIT informante.");
            var componentes = new[] { f.NetoGravado, f.NoGravado, f.Exento, f.PercepcionIva, f.PercepcionIibb, f.OtrasPercepciones, f.ImpuestosInternos, f.Iva };
            if (Math.Abs(componentes.Sum() - f.Total) > .02m) Error($"El total {f.Total:N2} no coincide con netos, IVA y tributos ({componentes.Sum():N2}).");
            if (componentes.Append(f.Total).Concat(f.Alicuotas.SelectMany(a => new[] { a.Neto, a.Iva })).Any(v => Math.Abs(v) >= 10000000000000m))
                Error("Importe excede los 13 enteros y 2 decimales admitidos.");
            if (componentes.Any(v => v != 0 && Math.Sign(v) != Math.Sign(f.Total))) Error("Hay conceptos con signo opuesto al total; requieren revisión antes de exportar en positivo.");
            var discrimina = r.Filtro.Compras ? f.Letra is "A" or "M" : f.Letra != "C";
            if (discrimina && Math.Abs(f.Alicuotas.Sum(a => a.Neto) - f.NetoGravado) > .02m) Error("El neto no coincide con la suma de bases por alícuota.");
            if (!discrimina && f.Alicuotas.Count != 0) Error("El comprobante no debe discriminar IVA.");
            if (f.Alicuotas.Count > 9) Error("Cantidad de alícuotas fuera de formato.");
            foreach (var a in f.Alicuotas)
            {
                if (LibroIvaService.CodigoAlicuota(a.Alicuota) == 0) Error($"Alícuota no admitida: {a.Alicuota}%.");
                if (Math.Abs(Math.Round(a.Neto * a.Alicuota / 100, 2, MidpointRounding.AwayFromZero) - a.Iva) > .10m)
                    Error($"IVA inconsistente con la base de la alícuota {a.Alicuota}%.");
            }
        }
        return errores;
    }

    internal static void ValidarFormato(LibroIvaResultado r, string comprobantes, string alicuotas)
    {
        var cab = comprobantes.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var det = alicuotas.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var compras = r.Filtro.Compras;
        if (cab.Length != r.Filas.Count || cab.Any(l => l.Length != (compras ? 325 : 266)) || det.Any(l => l.Length != (compras ? 84 : 62)))
            throw new InvalidOperationException("El TXT no cumple las longitudes o cantidades del diseño ARCA.");
        static decimal Importe(string l, int inicio) => decimal.Parse(l.AsSpan(inicio, 15), NumberStyles.None, CultureInfo.InvariantCulture) / 100;
        var indice = 0;
        for (var i = 0; i < cab.Length; i++)
        {
            var c = cab[i]; var f = r.Filas[i];
            if (Importe(c, compras ? 104 : 108) != Math.Round(Math.Abs(f.Total), 2, MidpointRounding.AwayFromZero))
                throw new InvalidOperationException("El total serializado no coincide con el libro.");
            var cantidad = int.Parse(c.AsSpan(compras ? 237 : 241, 1), CultureInfo.InvariantCulture);
            var baseNeta = 0m; var iva = 0m;
            for (var j = 0; j < cantidad; j++)
            {
                if (indice >= det.Length) throw new InvalidOperationException("Faltan alícuotas en el TXT.");
                var a = det[indice++];
                if (a[..28] != c.Substring(8, 28) || compras && a.Substring(28, 22) != c.Substring(52, 22))
                    throw new InvalidOperationException("Las claves u orden de cabeceras y alícuotas no coinciden.");
                baseNeta += Importe(a, compras ? 50 : 28);
                iva += Importe(a, compras ? 69 : 47);
            }
            if (cantidad > 0 && (Math.Abs(baseNeta - Math.Abs(f.NetoGravado)) > .02m || Math.Abs(iva - Math.Abs(f.Iva)) > .02m))
                throw new InvalidOperationException("Los netos o el IVA del TXT no coinciden con el libro.");
        }
        if (indice != det.Length) throw new InvalidOperationException("Hay alícuotas sin cabecera en el TXT.");
    }
}

internal sealed class LibroIvaArcaValidationException(string mensaje, IReadOnlyList<string> errores) : InvalidOperationException(mensaje)
{
    public IReadOnlyList<string> Errores { get; } = errores;
}
