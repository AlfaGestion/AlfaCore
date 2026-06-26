namespace AlfaCore.Services;

public interface IPasswordVerifier
{
    bool Verify(string providedPassword, string storedPassword);
}
