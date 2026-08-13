using AlfaCore.Models;
using System.Reflection;
using System.Security.Cryptography;

namespace AlfaCore.Services;

internal sealed class CargaViajesServiceSaveTarifaProxy : DispatchProxy
{
    private static readonly char[] TarifaCodeAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

    private ICargaViajesService? inner;

    public static ICargaViajesService Create(ICargaViajesService innerService)
    {
        var proxy = Create<ICargaViajesService, CargaViajesServiceSaveTarifaProxy>();
        ((CargaViajesServiceSaveTarifaProxy)(object)proxy).inner = innerService;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            throw new ArgumentNullException(nameof(targetMethod));

        args ??= [];

        if (string.Equals(targetMethod.Name, nameof(ICargaViajesService.SaveTarifaAsync), StringComparison.Ordinal))
        {
            if (args.Length == 1 && args[0] is CargaViajeTarifaSaveRequest request)
                return SaveTarifaAsyncAsync(request);
        }

        return targetMethod.Invoke(inner, args);
    }

    private async Task<string> SaveTarifaAsyncAsync(CargaViajeTarifaSaveRequest request)
    {
        if (inner is null)
            throw new InvalidOperationException("El servicio delegado no está inicializado.");

        if (request.Id <= 0)
        {
            var currentIdLista = NormalizeCode(request.IdLista);
            if (string.IsNullOrWhiteSpace(currentIdLista) || await inner.GetTarifaByIdAsync(currentIdLista) is not null)
                request.IdLista = await CreateUniqueTarifaListaAsync();
            else
                request.IdLista = currentIdLista;
        }

        return await inner.SaveTarifaAsync(request);
    }

    private async Task<string> CreateUniqueTarifaListaAsync()
    {
        if (inner is null)
            throw new InvalidOperationException("El servicio delegado no está inicializado.");

        const int maxAttempts = 512;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var candidate = CreateRandomCode();
            if (await inner.GetTarifaByIdAsync(candidate) is null)
                return candidate;
        }

        for (var suffix = 1; suffix <= 9999; suffix++)
        {
            var candidate = suffix.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            if (await inner.GetTarifaByIdAsync(candidate) is null)
                return candidate;
        }

        throw new InvalidOperationException("No se pudo generar un código de lista único para la nueva tarifa.");
    }

    private static string CreateRandomCode()
    {
        Span<char> chars = stackalloc char[4];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = TarifaCodeAlphabet[RandomNumberGenerator.GetInt32(TarifaCodeAlphabet.Length)];

        return new string(chars);
    }

    private static string NormalizeCode(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();
}
