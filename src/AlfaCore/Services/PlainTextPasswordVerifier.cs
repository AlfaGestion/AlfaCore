namespace AlfaCore.Services;

public sealed class PlainTextPasswordVerifier : IPasswordVerifier
{
    public bool Verify(string providedPassword, string storedPassword)
        => string.Equals(providedPassword?.Trim(), storedPassword?.Trim(), StringComparison.Ordinal);
}
