using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Time;

#nullable enable

namespace Odin.Services.Registry.PayloadMove;

/// <summary>
/// The source side of a payload move: the handoff token an export mints, the transfer credential the target
/// redeems it for, and whether the target has reported the transfer complete. Kept in one Settings row per
/// identity, which is not exported, so it stays on the source. Only hashes of the secrets are stored.
/// </summary>
public class PayloadMoveSource(SystemDatabase systemDatabase)
{
    /// <summary>How long an export's handoff token can be redeemed: the gap between export and import.</summary>
    public static readonly TimeSpan HandoffLifetime = TimeSpan.FromDays(7);

    private const string KeyPrefix = "payload-move-source:";

    /// <summary>
    /// A new handoff token for the identity's export. Replaces any earlier one, so re-exporting revokes what
    /// a previous file (and a previous target) could redeem or use.
    /// </summary>
    public async Task<string> MintHandoffAsync(Guid identityId)
    {
        var token = NewSecret();
        await SaveAsync(identityId, new SourceState
        {
            HandoffHash = Hash(token),
            HandoffExpiresAt = UnixTimeUtc.Now().AddMilliseconds((long)HandoffLifetime.TotalMilliseconds),
        });
        return token;
    }

    /// <summary>
    /// Trades the handoff token for a transfer credential, once. Null if the token is wrong, expired or
    /// already redeemed (a second import of the same file).
    /// </summary>
    public async Task<string?> RedeemAsync(Guid identityId, string handoffToken)
    {
        var state = await LoadAsync(identityId);
        if (state == null || state.RedeemedAt != null || state.CompletedAt != null ||
            state.HandoffExpiresAt.milliseconds < UnixTimeUtc.Now().milliseconds ||
            !Matches(state.HandoffHash, handoffToken))
        {
            return null;
        }

        var credential = NewSecret();
        state.CredentialHash = Hash(credential);
        state.RedeemedAt = UnixTimeUtc.Now();
        await SaveAsync(identityId, state);
        return credential;
    }

    /// <summary>True if the credential is the identity's live transfer credential.</summary>
    public async Task<bool> AuthorizeAsync(Guid identityId, string? credential)
    {
        var state = await LoadAsync(identityId);
        return state is { CompletedAt: null, CredentialHash: not null } && Matches(state.CredentialHash, credential);
    }

    /// <summary>
    /// The target has everything: record it and revoke the credential, which lets the source registration be
    /// deleted. False if the credential is not the live one.
    /// </summary>
    public async Task<bool> CompleteAsync(Guid identityId, string? credential)
    {
        var state = await LoadAsync(identityId);
        if (state is not { CompletedAt: null, CredentialHash: not null } || !Matches(state.CredentialHash, credential))
        {
            return false;
        }

        state.CompletedAt = UnixTimeUtc.Now();
        state.CredentialHash = null;
        await SaveAsync(identityId, state);
        return true;
    }

    /// <summary>
    /// True while a target may still need the identity's payloads from here: a transfer was started and has not
    /// completed, or an export's handoff token can still be redeemed.
    /// </summary>
    public async Task<bool> IsTransferPendingAsync(Guid identityId)
    {
        var state = await LoadAsync(identityId);
        return state is { CompletedAt: null } &&
               (state.RedeemedAt != null || state.HandoffExpiresAt.milliseconds >= UnixTimeUtc.Now().milliseconds);
    }

    public async Task<SourceState?> LoadAsync(Guid identityId)
    {
        var record = await systemDatabase.Settings.GetAsync(KeyPrefix + identityId);
        return record == null ? null : OdinSystemSerializer.Deserialize<SourceState>(record.value);
    }

    private async Task SaveAsync(Guid identityId, SourceState state)
    {
        await systemDatabase.Settings.UpsertAsync(new SettingsRecord
        {
            key = KeyPrefix + identityId,
            value = OdinSystemSerializer.Serialize(state)
        });
    }

    private static string NewSecret() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(secret.ToUtf8ByteArray()));

    private static bool Matches(string? hash, string? secret) =>
        hash != null && secret != null &&
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), SHA256.HashData(secret.ToUtf8ByteArray()));

    public class SourceState
    {
        public string HandoffHash { get; set; } = "";
        public UnixTimeUtc HandoffExpiresAt { get; set; }
        public string? CredentialHash { get; set; }
        public UnixTimeUtc? RedeemedAt { get; set; }
        public UnixTimeUtc? CompletedAt { get; set; }
    }
}
