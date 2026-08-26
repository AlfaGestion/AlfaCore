using System.Security.Cryptography;
using System.Text;

namespace AlfaCore.Services;

public sealed class WhatsAppEmbeddedSignupStateProtector : IWhatsAppEmbeddedSignupStateProtector
{
    public (string State, string Hash) Create()
    {
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return (state, Hash(state));
    }

    public string Hash(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("El state es obligatorio.", nameof(state));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
    }
}
