namespace AlfaCore.Configuration;

public sealed class AlfaCoreEsLocalOptions
{
    public const string SectionName = "AlfaCoreEsLocal";

    public bool Enabled { get; set; }

    public bool ShouldDisableUnrelatedHostedServices(string environmentName)
        => Enabled && string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase);
}
