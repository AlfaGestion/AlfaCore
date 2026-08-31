using AlfaCore.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

namespace AlfaCore.Services;

public sealed class WhatsAppAssetOwnershipStore(IConfiguration configuration, IHostEnvironment? environment = null) : IWhatsAppAssetOwnershipStore
{
    private string ConnectionString => WhatsAppEmbeddedSignupConnection.Resolve(configuration, environment);

    public async Task<bool> IsSchemaAvailableAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT CASE WHEN
                OBJECT_ID(N'dbo.WhatsAppEmbeddedOnboarding', N'U') IS NOT NULL AND
                OBJECT_ID(N'dbo.WhatsAppWabaOwnership', N'U') IS NOT NULL AND
                OBJECT_ID(N'dbo.WhatsAppPhoneOwnership', N'U') IS NOT NULL AND
                OBJECT_ID(N'dbo.WhatsAppSecureVault', N'U') IS NOT NULL
            THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;
            """;
        await using var cn = new SqlConnection(ConnectionString);
        return await cn.ExecuteScalarAsync<bool>(new CommandDefinition(sql, cancellationToken: ct));
    }

    public Task<WhatsAppAssetOwnershipDecision> ReserveWabaAsync(string wabaId, int idBase, string metaBusinessId, CancellationToken ct = default)
        => ReserveAsync("WABA", NormalizeId(wabaId, nameof(wabaId)), null, idBase, metaBusinessId, ct);

    public Task<WhatsAppAssetOwnershipDecision> ReservePhoneAsync(string phoneNumberId, string wabaId, int idBase, CancellationToken ct = default)
        => ReserveAsync("PHONE", NormalizeId(phoneNumberId, nameof(phoneNumberId)), NormalizeId(wabaId, nameof(wabaId)), idBase, string.Empty, ct);

    public async Task<WhatsAppWabaOwnership?> GetWabaOwnershipAsync(string wabaId, CancellationToken ct = default)
    {
        const string sql = "SELECT WabaId, IdBase, MetaBusinessId, FechaModificacionUtc ModifiedAtUtc FROM dbo.WhatsAppWabaOwnership WHERE WabaId=@WabaId";
        await using var cn = new SqlConnection(ConnectionString);
        return await cn.QuerySingleOrDefaultAsync<WhatsAppWabaOwnership>(new CommandDefinition(sql, new { WabaId = NormalizeId(wabaId, nameof(wabaId)) }, cancellationToken: ct));
    }

    public async Task<WhatsAppPhoneOwnership?> GetPhoneOwnershipAsync(string phoneNumberId, CancellationToken ct = default)
    {
        const string sql = "SELECT PhoneNumberId, WabaId, IdBase, FechaModificacionUtc ModifiedAtUtc FROM dbo.WhatsAppPhoneOwnership WHERE PhoneNumberId=@PhoneNumberId";
        await using var cn = new SqlConnection(ConnectionString);
        return await cn.QuerySingleOrDefaultAsync<WhatsAppPhoneOwnership>(new CommandDefinition(sql, new { PhoneNumberId = NormalizeId(phoneNumberId, nameof(phoneNumberId)) }, cancellationToken: ct));
    }

    private async Task<WhatsAppAssetOwnershipDecision> ReserveAsync(string kind, string assetId, string? wabaId, int idBase, string metaBusinessId, CancellationToken ct)
    {
        if (idBase <= 0) throw new ArgumentOutOfRangeException(nameof(idBase));
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        if (kind == "WABA")
        {
            const string select = "SELECT IdBase FROM dbo.WhatsAppWabaOwnership WITH (UPDLOCK, HOLDLOCK) WHERE WabaId=@AssetId";
            var owner = await cn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(select, new { AssetId = assetId }, tx, cancellationToken: ct));
            if (owner.HasValue) return await FinishAsync(tx, owner.Value == idBase ? WhatsAppAssetOwnershipResult.AlreadyOwnedByBase : WhatsAppAssetOwnershipResult.Conflict, owner.Value, assetId, ct);
            const string insert = "INSERT dbo.WhatsAppWabaOwnership(WabaId,IdBase,MetaBusinessId,FechaAltaUtc,FechaModificacionUtc) VALUES(@AssetId,@IdBase,@MetaBusinessId,SYSUTCDATETIME(),SYSUTCDATETIME())";
            await cn.ExecuteAsync(new CommandDefinition(insert, new { AssetId = assetId, IdBase = idBase, MetaBusinessId = metaBusinessId.Trim() }, tx, cancellationToken: ct));
        }
        else
        {
            const string selectWaba = "SELECT IdBase FROM dbo.WhatsAppWabaOwnership WITH (UPDLOCK, HOLDLOCK) WHERE WabaId=@WabaId";
            var wabaOwner = await cn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(selectWaba, new { WabaId = wabaId }, tx, cancellationToken: ct));
            if (!wabaOwner.HasValue || wabaOwner.Value != idBase)
                return await FinishAsync(tx, WhatsAppAssetOwnershipResult.Conflict, wabaOwner ?? 0, assetId, ct);

            const string select = "SELECT IdBase FROM dbo.WhatsAppPhoneOwnership WITH (UPDLOCK, HOLDLOCK) WHERE PhoneNumberId=@AssetId";
            var owner = await cn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(select, new { AssetId = assetId }, tx, cancellationToken: ct));
            if (owner.HasValue) return await FinishAsync(tx, owner.Value == idBase ? WhatsAppAssetOwnershipResult.AlreadyOwnedByBase : WhatsAppAssetOwnershipResult.Conflict, owner.Value, assetId, ct);
            const string insert = "INSERT dbo.WhatsAppPhoneOwnership(PhoneNumberId,WabaId,IdBase,FechaAltaUtc,FechaModificacionUtc) VALUES(@AssetId,@WabaId,@IdBase,SYSUTCDATETIME(),SYSUTCDATETIME())";
            await cn.ExecuteAsync(new CommandDefinition(insert, new { AssetId = assetId, WabaId = wabaId, IdBase = idBase }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return new(WhatsAppAssetOwnershipResult.Reserved, idBase, assetId);
    }

    private static async Task<WhatsAppAssetOwnershipDecision> FinishAsync(SqlTransaction tx, WhatsAppAssetOwnershipResult result, int ownerBaseId, string assetId, CancellationToken ct)
    {
        await tx.CommitAsync(ct);
        return new(result, ownerBaseId, assetId);
    }

    private static string NormalizeId(string value, string parameter)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0 || !normalized.All(static c => c is >= '0' and <= '9'))
            throw new ArgumentException("El identificador Meta debe contener únicamente dígitos.", parameter);
        return normalized;
    }
}
