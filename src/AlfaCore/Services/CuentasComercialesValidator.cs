using AlfaCore.Models;
using Microsoft.Data.SqlClient;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace AlfaCore.Services;

public sealed class CuentasComercialesValidator(
    IConfiguration configuration,
    ISessionService sessionService) : ICuentasComercialesValidator
{
    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<ValidationResult> ValidateForSaveAsync(CuentaComercialTipo tipo, CuentaComercialSaveRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new ValidationResult();
        var isNew = string.IsNullOrWhiteSpace(request.CodigoOriginal);
        var codigo = (request.Codigo ?? string.Empty).Trim();
        var razonSocial = (request.RazonSocial ?? string.Empty).Trim();
        var contacto = (request.Contacto ?? string.Empty).Trim();
        var calle = (request.Calle ?? string.Empty).Trim();
        var numero = (request.Numero ?? string.Empty).Trim();
        var piso = (request.Piso ?? string.Empty).Trim();
        var departamento = (request.Departamento ?? string.Empty).Trim();
        var codigoPostal = (request.CodigoPostal ?? string.Empty).Trim();
        var localidad = (request.Localidad ?? string.Empty).Trim();
        var provincia = (request.ProvinciaCodigo ?? string.Empty).Trim();
        var pais = (request.PaisCodigo ?? string.Empty).Trim();
        var telefono = (request.Telefono ?? string.Empty).Trim();
        var fax = (request.Fax ?? string.Empty).Trim();
        var mail = (request.Mail ?? string.Empty).Trim();
        var web = (request.Web ?? string.Empty).Trim();
        var documentoTipo = (request.DocumentoTipoCodigo ?? string.Empty).Trim();
        var numeroDocumento = NormalizeDigits(request.NumeroDocumento);
        var iva = (request.IvaCodigo ?? string.Empty).Trim();
        var observaciones = (request.Observaciones ?? string.Empty).Trim();
        var condicion = (request.CondicionComercialCodigo ?? string.Empty).Trim();
        var idLista = (request.ListaPrecioCodigo ?? string.Empty).Trim();
        var vendedor = (request.VendedorCodigo ?? string.Empty).Trim();
        var cuentaPrincipal = (request.CuentaPrincipal ?? string.Empty).Trim();
        var codigoImputacion = (request.CodigoImputacion ?? string.Empty).Trim();
        var codigoOpcional = (request.CodigoOpcional ?? string.Empty).Trim();
        var moneda = (request.MonedaCodigo ?? string.Empty).Trim();
        var clasificacion = (request.ClasificacionCodigo ?? string.Empty).Trim();
        var lugarEntrega = (request.LugarEntrega ?? string.Empty).Trim();
        var datosPago = (request.DatosPago ?? string.Empty).Trim();
        var manual = (request.Manual ?? string.Empty).Trim();
        var notaCuenta = (request.NotaCuenta ?? string.Empty).Trim();
        var situacionIb = (request.SituacionIb ?? string.Empty).Trim();
        var convenioIb = (request.ConvenioIb ?? string.Empty).Trim();
        var conceptoIb = (request.ConceptoIb ?? string.Empty).Trim();
        var nroConstanciaIb = (request.NroConstanciaIb ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(codigo))
            result.Add("codigo", "No se pudo determinar el código de la cuenta.");
        else if (codigo.Length > 15)
            result.Add("codigo", "El código no puede superar los 15 caracteres.");

        if (string.IsNullOrWhiteSpace(razonSocial))
            result.Add("razon-social", $"La razón social del {GetSingular(tipo).ToLowerInvariant()} es obligatoria.");
        else if (razonSocial.Length > 50)
            result.Add("razon-social", "La razón social no puede superar los 50 caracteres.");

        if (contacto.Length > 70)
            result.Add("contacto", "El contacto no puede superar los 70 caracteres.");

        if (string.IsNullOrWhiteSpace(calle))
            result.Add("calle", "La calle es obligatoria.");
        else if (calle.Length > 50)
            result.Add("calle", "La calle no puede superar los 50 caracteres.");

        if (numero.Length > 6)
            result.Add("numero", "La altura no puede superar los 6 caracteres.");

        if (piso.Length > 2)
            result.Add("piso", "El piso no puede superar los 2 caracteres.");

        if (departamento.Length > 2)
            result.Add("departamento", "El departamento no puede superar los 2 caracteres.");

        if (codigoPostal.Length > 10)
            result.Add("codigo-postal", "El código postal no puede superar los 10 caracteres.");

        if (string.IsNullOrWhiteSpace(localidad))
            result.Add("localidad", "La localidad es obligatoria.");
        else if (localidad.Length > 50)
            result.Add("localidad", "La localidad no puede superar los 50 caracteres.");

        if (string.IsNullOrWhiteSpace(provincia))
            result.Add("provincia", "La provincia es obligatoria.");
        else if (provincia.Length > 4)
            result.Add("provincia", "La provincia no puede superar los 4 caracteres.");

        if (string.IsNullOrWhiteSpace(pais))
            result.Add("pais", "El país es obligatorio.");
        else if (pais.Length > 4)
            result.Add("pais", "El país no puede superar los 4 caracteres.");

        if (telefono.Length > 50)
            result.Add("telefono", "El teléfono no puede superar los 50 caracteres.");

        if (fax.Length > 50)
            result.Add("fax", "El fax no puede superar los 50 caracteres.");

        if (mail.Length > 250)
            result.Add("mail", "El email no puede superar los 250 caracteres.");
        else if (!string.IsNullOrWhiteSpace(mail) && !IsValidEmail(mail))
            result.Add("mail", "El email no tiene un formato válido.");

        if (web.Length > 250)
            result.Add("web", "La web no puede superar los 250 caracteres.");

        if (documentoTipo.Length > 4)
            result.Add("tipo-documento", "El tipo de documento no puede superar los 4 caracteres.");

        if (string.IsNullOrWhiteSpace(numeroDocumento))
            result.Add("numero-documento", "El CUIT / número de documento es obligatorio.");
        else if (numeroDocumento.Length > 13)
            result.Add("numero-documento", "El CUIT / número de documento no puede superar los 13 caracteres.");
        else if (ShouldValidateCuit(documentoTipo) && !IsValidCuit(numeroDocumento))
            result.Add("numero-documento", "El CUIT informado no es válido.");

        if (iva.Length > 4)
            result.Add("iva", "La condición de IVA no puede superar los 4 caracteres.");

        if (observaciones.Length > 200)
            result.Add("observaciones", "Las observaciones no pueden superar los 200 caracteres.");

        if (condicion.Length > 4)
            result.Add("condicion-comercial", "La condición comercial no puede superar los 4 caracteres.");

        if (idLista.Length > 4)
            result.Add("lista-precio", "La lista de precios no puede superar los 4 caracteres.");

        if (vendedor.Length > 4)
            result.Add("vendedor", "El vendedor no puede superar los 4 caracteres.");

        if (cuentaPrincipal.Length > 15)
            result.Add("cuenta-principal", "La cuenta principal no puede superar los 15 caracteres.");

        if (codigoImputacion.Length > 15)
            result.Add("codigo-imputacion", "La cuenta de imputación no puede superar los 15 caracteres.");

        if (codigoOpcional.Length > 15)
            result.Add("codigo-opcional", "El código opcional no puede superar los 15 caracteres.");

        if (moneda.Length > 4)
            result.Add("moneda", "La moneda no puede superar los 4 caracteres.");

        if (clasificacion.Length > 4)
            result.Add("clasificacion", "La clasificación no puede superar los 4 caracteres.");

        if (lugarEntrega.Length > 200)
            result.Add("lugar-entrega", "El lugar de entrega no puede superar los 200 caracteres.");

        if (datosPago.Length > 150)
            result.Add("datos-pago", "Los datos de pago no pueden superar los 150 caracteres.");

        if (notaCuenta.Length > 4000)
            result.Add("nota-cuenta", "La nota ampliada no puede superar los 4000 caracteres.");

        if (situacionIb.Length > 2)
            result.Add("situacion-ib", "La situación IB no puede superar los 2 caracteres.");

        if (convenioIb.Length > 2)
            result.Add("convenio-ib", "El convenio IB no puede superar los 2 caracteres.");

        if (conceptoIb.Length > 2)
            result.Add("concepto-ib", "El concepto IB no puede superar los 2 caracteres.");

        if (nroConstanciaIb.Length > 13)
            result.Add("nro-constancia-ib", "El número de constancia no puede superar los 13 caracteres.");

        if (request.LimiteCredito.HasValue && request.LimiteCredito.Value < 0)
            result.Add("limite-credito", "El límite de crédito no puede ser negativo.");

        if (tipo == CuentaComercialTipo.Proveedor)
            ValidateDescuentosProveedor(request.DescuentosCondicion ?? [], result);

        if (!result.IsValid)
            return result;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        if (isNew)
        {
            if (await ExistsByCodeAsync(cn, codigo, ct))
                result.Add("codigo", $"Ya existe una cuenta con el código {codigo}.");
        }
        else
        {
            if (!await ExistsByCodeAsync(cn, request.CodigoOriginal.Trim(), ct))
            {
                result.Add(string.Empty, $"El {GetSingular(tipo).ToLowerInvariant()} seleccionado ya no existe en la base activa.");
                return result;
            }

            if (!string.Equals(request.CodigoOriginal.Trim(), codigo, StringComparison.OrdinalIgnoreCase))
                result.Add("codigo", "En esta primera versión el código no se puede modificar.");
        }

        if (!string.IsNullOrWhiteSpace(provincia) && !await ExistsInTableAsync(cn, "TA_ESTADOS", "CODIGO", provincia, ct))
            result.Add("provincia", "La provincia seleccionada no existe.");

        if (!string.IsNullOrWhiteSpace(pais) && !await ExistsInTableAsync(cn, "TA_PAISES", "CODIGO", pais, ct))
            result.Add("pais", "El país seleccionado no existe.");

        if (!string.IsNullOrWhiteSpace(documentoTipo) && !await ExistsInTableAsync(cn, "TA_TIPODOCUMENTO", "CODIGO", documentoTipo, ct))
            result.Add("tipo-documento", "El tipo de documento seleccionado no existe.");

        if (!string.IsNullOrWhiteSpace(iva) && !await ExistsInTableAsync(cn, "TA_CONDIVA", "CODIGO", iva, ct))
            result.Add("iva", "La condición de IVA seleccionada no existe.");

        if (!string.IsNullOrWhiteSpace(condicion) && !await ExistsCondicionAsync(cn, tipo, condicion, ct))
            result.Add("condicion-comercial", "La condición comercial seleccionada no existe o no aplica a este módulo.");

        if (!string.IsNullOrWhiteSpace(vendedor) && !await ExistsInTableAsync(cn, "V_TA_VENDEDORES", "IdVendedor", vendedor, ct))
            result.Add("vendedor", "El vendedor seleccionado no existe.");

        if (!string.IsNullOrWhiteSpace(idLista))
        {
            if (!await ExistsPriceListAsync(cn, tipo, idLista, ct))
                result.Add("lista-precio", "La lista de precios seleccionada no existe para este módulo.");
            else if (tipo == CuentaComercialTipo.Proveedor && await PriceListAssignedToOtherProviderAsync(cn, codigo, idLista, ct))
                result.Add("lista-precio", "La lista indicada ya está asociada a otro proveedor.");
        }

        if (!string.IsNullOrWhiteSpace(moneda) && !await ExistsInTableAsync(cn, "TA_MONEDAS", "CODIGO", moneda, ct))
            result.Add("moneda", "La moneda seleccionada no existe.");

        if (!string.IsNullOrWhiteSpace(clasificacion) && !await ExistsInTableAsync(cn, "TA_CLASIFICACIONES", "Codigo", clasificacion, ct))
            result.Add("clasificacion", "La clasificación seleccionada no existe.");

        if (!string.IsNullOrWhiteSpace(codigoImputacion))
            await ValidateCuentaImputacionAsync(cn, codigoImputacion, result, ct);

        if (tipo == CuentaComercialTipo.Proveedor)
            await ValidateCondicionesProveedorAsync(cn, request.DescuentosCondicion ?? [], result, ct);

        return result;
    }

    private static void ValidateDescuentosProveedor(IEnumerable<CuentaComercialCondicionDescuentoDto> descuentos, ValidationResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in descuentos.Where(static x => x is not null))
        {
            var codigo = (item.CondicionCodigo ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(codigo))
            {
                result.Add("descuentos", "Cada descuento debe indicar una condición comercial.");
                continue;
            }

            if (codigo.Length > 4)
                result.Add("descuentos", $"La condición {codigo} no puede superar los 4 caracteres.");

            if (!seen.Add(codigo))
                result.Add("descuentos", $"La condición {codigo} está repetida en los descuentos del proveedor.");

            ValidatePorcentaje(item.Descuento1, codigo, "1", result);
            ValidatePorcentaje(item.Descuento2, codigo, "2", result);
            ValidatePorcentaje(item.Descuento3, codigo, "3", result);
            ValidatePorcentaje(item.Descuento4, codigo, "4", result);
            ValidatePorcentaje(item.Descuento5, codigo, "5", result);
        }
    }

    private static void ValidatePorcentaje(decimal? value, string codigo, string orden, ValidationResult result)
    {
        if (!value.HasValue)
            return;

        if (value.Value is < 0 or > 100)
            result.Add("descuentos", $"El descuento {orden} de la condición {codigo} debe estar entre 0 y 100.");
    }

    private static async Task ValidateCondicionesProveedorAsync(SqlConnection cn, IEnumerable<CuentaComercialCondicionDescuentoDto> descuentos, ValidationResult result, CancellationToken ct)
    {
        foreach (var item in descuentos.Where(static x => x is not null))
        {
            var codigo = (item.CondicionCodigo ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(codigo))
                continue;

            if (!await ExistsCondicionAsync(cn, CuentaComercialTipo.Proveedor, codigo, ct))
                result.Add("descuentos", $"La condición {codigo} no existe o no aplica a proveedores.");
        }
    }

    private static async Task ValidateCuentaImputacionAsync(SqlConnection cn, string codigoImputacion, ValidationResult result, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                ISNULL(TITULO, 0),
                ISNULL(BLOQUEO, 0),
                ISNULL(TipoVista, ''),
                ISNULL(MedioDePago, '')
            FROM dbo.MA_CUENTAS
            WHERE UPPER(LTRIM(RTRIM(CODIGO))) = @Codigo;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Codigo", codigoImputacion.Trim().ToUpperInvariant());
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct))
        {
            result.Add("codigo-imputacion", "La cuenta de imputación no existe en MA_CUENTAS.");
            return;
        }

        if (GetBool(rd, 0))
            result.Add("codigo-imputacion", "La cuenta de imputación no puede ser una cuenta título.");

        if (GetBool(rd, 1))
            result.Add("codigo-imputacion", "La cuenta de imputación está bloqueada.");

        if (!string.IsNullOrWhiteSpace(GetString(rd, 2)))
            result.Add("codigo-imputacion", "La cuenta de imputación no puede ser un cliente, proveedor o caja/banco.");

        if (!string.IsNullOrWhiteSpace(GetString(rd, 3)))
            result.Add("codigo-imputacion", "La cuenta de imputación no puede estar reservada para medios de pago.");
    }

    private static async Task<bool> ExistsByCodeAsync(SqlConnection cn, string codigo, CancellationToken ct)
        => await ExistsInTableAsync(cn, "MA_CUENTAS", "CODIGO", codigo, ct);

    private static async Task<bool> ExistsInTableAsync(SqlConnection cn, string tableName, string fieldName, string value, CancellationToken ct)
    {
        var sql = $"""
            SELECT COUNT(1)
            FROM dbo.{tableName}
            WHERE UPPER(LTRIM(RTRIM({fieldName}))) = @Value;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Value", value.Trim().ToUpperInvariant());
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }

    private static async Task<bool> ExistsCondicionAsync(SqlConnection cn, CuentaComercialTipo tipo, string codigo, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.V_TA_Cpra_Vta
            WHERE UPPER(LTRIM(RTRIM(IDCond_Cpra_Vta))) = @Codigo
              AND (
                    ISNULL(HabilitadoPara, '') = ''
                    OR HabilitadoPara LIKE '%' + @Flag + '%'
                  );
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Codigo", codigo.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@Flag", tipo == CuentaComercialTipo.Cliente ? "V" : "C");
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }

    private static async Task<bool> ExistsPriceListAsync(SqlConnection cn, CuentaComercialTipo tipo, string idLista, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.V_MA_PreciosCab
            WHERE UPPER(LTRIM(RTRIM(IdLista))) = @IdLista
              AND UPPER(LTRIM(RTRIM(ISNULL(TipoLista, '')))) = @TipoLista;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdLista", idLista.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@TipoLista", tipo == CuentaComercialTipo.Cliente ? "V" : "C");
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }

    private static async Task<bool> PriceListAssignedToOtherProviderAsync(SqlConnection cn, string codigoActual, string idLista, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.VT_PROVEEDORES
            WHERE UPPER(LTRIM(RTRIM(IdLista))) = @IdLista
              AND UPPER(LTRIM(RTRIM(CODIGO))) <> @Codigo;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@IdLista", idLista.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@Codigo", codigoActual.Trim().ToUpperInvariant());
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ShouldValidateCuit(string documentoTipo)
        => string.IsNullOrWhiteSpace(documentoTipo) || string.Equals(documentoTipo.Trim(), "1", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDigits(string? value)
        => Regex.Replace(value ?? string.Empty, "[^0-9]", string.Empty);

    private static bool IsValidCuit(string cuit)
    {
        var digits = NormalizeDigits(cuit);
        if (digits.Length != 11)
            return false;

        var factors = new[] { 5, 4, 3, 2, 7, 6, 5, 4, 3, 2 };
        var sum = 0;
        for (var i = 0; i < 10; i++)
            sum += (digits[i] - '0') * factors[i];

        var mod = 11 - (sum % 11);
        var check = mod switch
        {
            11 => 0,
            10 => 9,
            _ => mod
        };

        return check == digits[10] - '0';
    }

    private static string GetString(SqlDataReader rd, int index)
        => rd.IsDBNull(index) ? string.Empty : Convert.ToString(rd.GetValue(index)) ?? string.Empty;

    private static bool GetBool(SqlDataReader rd, int index)
        => !rd.IsDBNull(index) && Convert.ToBoolean(rd.GetValue(index));

    private static string GetSingular(CuentaComercialTipo tipo)
        => tipo == CuentaComercialTipo.Cliente ? "Cliente" : "Proveedor";
}
