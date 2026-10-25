using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Services.Base;

namespace Odin.Services.Security.PasswordRecovery.Shamir.ShardRequestApproval;

/// <summary>
/// As an automatic holder: a dealer's request for a shard, waiting for the owner to click the link this
/// holder emailed them.  Only the code's hash is kept.
/// </summary>
public sealed record AutomaticShardRelease
{
    public OdinId Dealer { get; init; }

    /// <summary>The recovering browser's throwaway key, which the shard is sealed to when released.</summary>
    public string RecoveryPublicKeyJwk { get; init; }

    public byte[] CodeHash { get; init; }

    /// <summary>When the code was made and emailed.</summary>
    public UnixTimeUtc Emailed { get; init; }
}

/// <summary>
/// An automatic holder's pending releases, one per shard (docs/zero-knowledge-recovery-plan.md,
/// "Building the automated deal"): every automatic holder checks the owner's email itself before it
/// releases, so whoever holds the dealer's server, or a copy of its data, can't take the shard without
/// the owner's inbox.
/// </summary>
public sealed class AutomaticShardReleases(TableKeyValueCached tblKeyValue)
{
    private static readonly SingleKeyValueStorage Storage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse("90ae30b1-aabe-49d6-ac3f-cb7765039bb3"));

    /// <summary>How long an emailed link works, unless a newer email replaces it.  A click doesn't use it up.</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromDays(7);

    /// <summary>At most one email per shard this often: a request inside it keeps the code already sent.</summary>
    public static readonly TimeSpan EmailInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Records <paramref name="dealer"/>'s request.  Returns the code to email, or null when this shard's
    /// last email is too recent to send another: its link still works, now for
    /// <paramref name="recoveryPublicKeyJwk"/>.
    /// </summary>
    public async Task<string> RequestAsync(Guid shardId, OdinId dealer, string recoveryPublicKeyJwk)
    {
        var now = UnixTimeUtc.Now();
        var existing = await Storage.GetAsync<AutomaticShardRelease>(tblKeyValue, shardId);
        if (existing != null && existing.Dealer == dealer && now - existing.Emailed < EmailInterval)
        {
            await Storage.UpsertAsync(tblKeyValue, shardId, existing with { RecoveryPublicKeyJwk = recoveryPublicKeyJwk });
            return null;
        }

        var code = ByteArrayUtil.GetRandomCryptoGuid().ToString("N");
        await Storage.UpsertAsync(tblKeyValue, shardId, new AutomaticShardRelease
        {
            Dealer = dealer,
            RecoveryPublicKeyJwk = recoveryPublicKeyJwk,
            CodeHash = Hash(code),
            Emailed = now
        });

        return code;
    }

    /// <summary>The request <paramref name="code"/> opens for <paramref name="dealer"/>; null when none still does.</summary>
    public async Task<AutomaticShardRelease> FindAsync(Guid shardId, OdinId dealer, string code)
    {
        var existing = await Storage.GetAsync<AutomaticShardRelease>(tblKeyValue, shardId);
        if (existing == null || existing.Dealer != dealer || UnixTimeUtc.Now() - existing.Emailed > CodeLifetime ||
            string.IsNullOrEmpty(code) || !CryptographicOperations.FixedTimeEquals(existing.CodeHash, Hash(code)))
        {
            return null;
        }

        return existing;
    }

    private static byte[] Hash(string code) => ByteArrayUtil.CalculateSHA256Hash(code.ToUtf8ByteArray());
}
