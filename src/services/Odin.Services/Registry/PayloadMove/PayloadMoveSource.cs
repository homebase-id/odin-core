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
            HandoffExpiresAt = UnixTimeUtc.Now().AddSeconds((long)HandoffLifetime.TotalSeconds),
        });
        return token;
    }

    /// <summary>
    /// Trades the handoff token for a transfer credential, once. Null if the token is wrong, expired or
    /// already redeemed (a second import of the same file).
    /// </summary>
    public async Task<string?> RedeemAsync(Guid identityId, string handoffToken)
    {
        var (state, stored) = await LoadWithStoredValueAsync(identityId);
        if (state == null || state.RedeemedAt != null || state.CompletedAt != null ||
            state.HandoffExpiresAt < UnixTimeUtc.Now() ||
            !Matches(state.HandoffHash, handoffToken))
        {
            return null;
        }

        var credential = NewSecret();
        state.CredentialHash = Hash(credential);
        state.RedeemedAt = UnixTimeUtc.Now();

        // Two concurrent redemptions must not both succeed: only the one whose write finds the row unchanged
        return await SaveIfUnchangedAsync(identityId, stored!, state) ? credential : null;
    }

    /// <summary>True if the credential is the identity's live transfer credential.</summary>
    public async Task<bool> AuthorizeAsync(Guid identityId, string? credential)
    {
        return IsLive(await LoadAsync(identityId), credential);
    }

    /// <summary>
    /// The target has everything: record it and revoke the credential, which lets the source registration be
    /// deleted. False if the credential is not the live one.
    /// </summary>
    public async Task<bool> CompleteAsync(Guid identityId, string? credential)
    {
        var (state, stored) = await LoadWithStoredValueAsync(identityId);
        if (!IsLive(state, credential))
        {
            return false;
        }

        state!.CompletedAt = UnixTimeUtc.Now();
        state.CredentialHash = null;

        // A re-export may have minted a new handoff since the read; do not write the old state over it
        return await SaveIfUnchangedAsync(identityId, stored!, state);
    }

    /// <summary>
    /// True while a target may still need the identity's payloads from here: a transfer was started and has not
    /// completed, or an export's handoff token can still be redeemed.
    /// </summary>
    public async Task<bool> IsTransferPendingAsync(Guid identityId)
    {
        return IsPending(await LoadAsync(identityId));
    }

    public static bool IsPending(SourceState? state) =>
        state is { CompletedAt: null } && (state.RedeemedAt != null || state.HandoffExpiresAt >= UnixTimeUtc.Now());

    private static bool IsLive(SourceState? state, string? credential) =>
        state is { CompletedAt: null, CredentialHash: not null } && Matches(state.CredentialHash, credential);

    public async Task<SourceState?> LoadAsync(Guid identityId)
    {
        return (await LoadWithStoredValueAsync(identityId)).state;
    }

    private async Task<(SourceState? state, string? stored)> LoadWithStoredValueAsync(Guid identityId)
    {
        var record = await systemDatabase.Settings.GetAsync(KeyPrefix + identityId);
        return record == null ? (null, null) : (OdinSystemSerializer.Deserialize<SourceState>(record.value), record.value);
    }

    private Task<bool> SaveIfUnchangedAsync(Guid identityId, string stored, SourceState state)
    {
        return systemDatabase.Settings.UpdateIfUnchangedAsync(KeyPrefix + identityId, stored, OdinSystemSerializer.Serialize(state));
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
