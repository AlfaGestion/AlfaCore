using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace AlfaCore.Services;

public sealed class PuntoVentaPedidoValidator(
    IConfiguration configuration,
    ISessionService sessionService) : IPuntoVentaPedidoValidator
{
    private string ConnectionString => sessionService.GetConnectionString().Length > 0
        ? sessionService.GetConnectionString()
        : configuration.GetConnectionString("AlfaGestion")
          ?? throw new InvalidOperationException("No se configuró la cadena de conexión 'ConnectionStrings:AlfaGestion'.");

    public async Task<ValidationResult> ValidatePedidoForSaveAsync(PuntoVentaPedidoSaveRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new ValidationResult();
        var nombreCliente = (request.NombreCliente ?? string.Empty).Trim();
        var modalidad = (request.Modalidad ?? string.Empty).Trim().ToUpperInvariant();
        var direccion = (request.Direccion ?? string.Empty).Trim();
        var telefono = (request.Telefono ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(nombreCliente))
            result.Add("nombre-cliente", "El nombre del cliente es obligatorio.");
        if (!PuntoVentaPedidoModalidadKeys.All.Contains(modalidad))
            result.Add("modalidad", "La modalidad seleccionada no es válida.");

        // En Delivery, teléfono, dirección y horario de entrega son obligatorios
        // (regla de negocio: sin esos datos no se puede coordinar la entrega).
        // En Take Away el teléfono es opcional y el horario vacío significa
        // "lo retira ahora mismo".
        if (modalidad == PuntoVentaPedidoModalidadKeys.Delivery)
        {
            if (string.IsNullOrWhiteSpace(direccion))
                result.Add("direccion", "La dirección es obligatoria para pedidos de Delivery.");
            if (string.IsNullOrWhiteSpace(telefono))
                result.Add("telefono", "El teléfono es obligatorio para pedidos de Delivery.");
            if (!request.HorarioProgramado.HasValue)
                result.Add("horario", "La hora de entrega es obligatoria para pedidos de Delivery.");
        }

        if (request.IdPuntoVenta <= 0)
            result.Add("id-punto-venta", "Debe seleccionar un punto de venta.");

        // No se exige al menos un ítem acá: el pedido se crea primero con los datos
        // del cliente y los artículos se cargan después en la pantalla de Mostrador
        // (reutilizada para las 3 modalidades). Que tenga ítems se valida recién al
        // cerrarlo/facturarlo (ValidateCierreAsync).
        for (var i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];
            if (string.IsNullOrWhiteSpace(item.IdArticulo))
                result.Add($"item-{i}-articulo", "Hay un artículo del pedido sin código.");
            if (item.Cantidad <= 0)
                result.Add($"item-{i}-cantidad", $"La cantidad de \"{item.Descripcion}\" debe ser mayor a cero.");
            if (item.Precio < 0)
                result.Add($"item-{i}-precio", $"El precio de \"{item.Descripcion}\" no puede ser negativo.");
        }

        if (!result.IsValid)
            return result;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);

        var puntoVentaExiste = await cn.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.POS_PUNTOVENTA WHERE ID = @IdPuntoVenta;",
            new { request.IdPuntoVenta },
            cancellationToken: ct));
        if (puntoVentaExiste == 0)
            result.Add("id-punto-venta", "El punto de venta seleccionado no existe.");

        return result;
    }

}
