using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Util;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Membership.Circles;

/// <summary>
/// Selective Share's circle keys (docs/selective-share-plan.md, PR 2).  A circle's share key is made on a device
/// of the app that owns the circle, which stores it locked with the app's zero-knowledge key and stores a copy
/// sealed to each member's matching drive.  The server opens none of them: it keeps them, checks their size and
/// format version, and decides who may read or write which.
/// </summary>
/// <remarks>
/// Only the owning app (or the owner console) may read or set a circle's key and seal copies for its members,
/// and never on an owner circle, which has no app key to lock with (plan, decision 2).  A member reads only
/// their own copy, through the Peer API, while the circle grants them access.
/// </remarks>
public class CircleShareKeyService(IdentityDatabase db, CircleDefinitionService circleDefinitionService,
    CircleMembershipService circleMembershipService, TenantContext tenantContext)
{
    /// <summary>
    /// The first byte of every share key blob: key header version 2 (#2024), AES-GCM from the start.
    /// </summary>
    public const byte FormatVersion = 2;

    /// <summary>Copies one request may store, so one batch can't hold the identity's write transaction for long.</summary>
    public const int MaxSealedShareKeysPerRequest = 500;

    /// <summary>The circle's share key, locked with the owning app's key; null until the app sets one.</summary>
    public async Task<byte[]> GetShareKeyAsync(GuidId circleId, IOdinContext odinContext)
    {
        var circle = await GetCircleCallerMayHandleShareKeysOfAsync(circleId, "read the share key of", odinContext);
        return circle.appKeyLockedShareKey;
    }

    /// <summary>
    /// Sets the circle's share key, once.  Refused when it already has one: two devices sharing with a new
    /// circle at once can't each set a different key, and the loser reads the winner's.  Replacing it is key
    /// rotation, not in the first version.
    /// </summary>
    public async Task SetShareKeyAsync(GuidId circleId, byte[] appKeyLockedShareKey, IOdinContext odinContext)
    {
        await GetCircleCallerMayHandleShareKeysOfAsync(circleId, "set the share key of", odinContext);
        AssertFormat(appKeyLockedShareKey, TableCircle.MaxShareKeyLength, nameof(appKeyLockedShareKey));

        if (await db.CircleCached.SetShareKeyAsync(circleId, appKeyLockedShareKey) == 0)
        {
            // Nothing written: another device set the key first, or the circle was deleted meanwhile
            throw await db.CircleCached.GetAsync(circleId) == null
                ? new OdinClientException($"Circle {circleId} does not exist", OdinClientErrorCode.CircleNotFound)
                : new OdinClientException($"Circle {circleId} already has a share key", OdinClientErrorCode.CircleShareKeyAlreadySet);
        }
    }

    /// <summary>
    /// The circle's members with no sealed copy of its key yet: new members, and those whose row was made
    /// again.  The owning app's device seals for each and stores them with <see cref="SetSealedShareKeysAsync"/>.
    /// </summary>
    public async Task<List<OdinId>> GetMembersWithoutSealedShareKeyAsync(GuidId circleId, IOdinContext odinContext)
    {
        await GetCircleCallerMayHandleShareKeysOfAsync(circleId, "list the unsealed members of", odinContext);

        var rows = await db.CircleMemberCached.GetCircleMembersAsync(circleId);
        return rows
            .Where(r => r.sealedShareKey == null)
            .Select(CircleMemberStorageData.From)
            .Where(IsIdentityMember)
            .Select(d => new OdinId(d.DomainName))
            .ToList();
    }

    /// <summary>Who may hold a copy: a connected identity's row, never a YouAuth domain's.</summary>
    private static bool IsIdentityMember(CircleMemberStorageData member) => member.DomainType == DomainType.Identity;

    /// <summary>
    /// Stores members' sealed copies of the circle key, all or none.  Each must be an identity member (as
    /// <see cref="GetMembersWithoutSealedShareKeyAsync"/> lists them), and the circle must have its key: a copy
    /// of a key the circle doesn't have can't be right.  A copy replaces an earlier one, as
    /// when the app re-seals to a member's new drive key.
    /// </summary>
    public async Task SetSealedShareKeysAsync(GuidId circleId, IReadOnlyCollection<SealedShareKey> sealedKeys,
        IOdinContext odinContext)
    {
        var circle = await GetCircleCallerMayHandleShareKeysOfAsync(circleId, "seal the share key of", odinContext);

        if (sealedKeys == null || sealedKeys.Count == 0 || sealedKeys.Count > MaxSealedShareKeysPerRequest)
        {
            throw new OdinClientException($"Supply 1 to {MaxSealedShareKeysPerRequest} sealed share keys",
                OdinClientErrorCode.ArgumentError);
        }

        var members = new List<(Guid MemberId, string OdinId, byte[] SealedKey)>();
        foreach (var sealedKey in sealedKeys)
        {
            OdinValidationUtils.AssertIsValidOdinId(sealedKey?.OdinId, out var odinId);
            AssertFormat(sealedKey!.SealedKey, TableCircleMember.MaxSealedShareKeyLength, nameof(SealedShareKey.SealedKey));
            members.Add((odinId.ToHashId(), odinId.DomainName, sealedKey.SealedKey));
        }

        if (circle.appKeyLockedShareKey == null)
        {
            throw new OdinClientException($"Circle {circleId} has no share key to seal",
                OdinClientErrorCode.CircleShareKeyNotSet);
        }

        await using var tx = await db.BeginStackedTransactionAsync();
        var rows = (await db.CircleMemberCached.GetCircleMembersAsync(circleId)).ToDictionary(r => r.memberId);
        foreach (var (memberId, odinId, sealedKey) in members)
        {
            if (!rows.TryGetValue(memberId, out var row) || !IsIdentityMember(CircleMemberStorageData.From(row)) ||
                await db.CircleMemberCached.SetSealedShareKeyAsync(circleId, memberId, sealedKey) == 0)
            {
                throw new OdinClientException($"{odinId} is not a member of circle {circleId}",
                    OdinClientErrorCode.NotACircleMember);
            }
        }

        tx.Commit();
    }

    /// <summary>
    /// The calling peer's own sealed copy of a circle's key; null when they have none yet.  Served only to a
    /// connection the owner has reviewed, which the circle currently grants, and never anyone else's copy: the
    /// row is looked up by the caller's identity, not by anything they send.
    /// </summary>
    /// <remarks>
    /// An unreviewed connection is judged as a logged-in stranger wherever content is (<see cref="ReviewedSecurityTier"/>),
    /// and the circle key opens content, so it waits for the review.  The copy can be sealed before then; it is
    /// served once the review lands.
    /// </remarks>
    public async Task<byte[]> GetCallersSealedShareKeyAsync(GuidId circleId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsConnected();
        if (ReviewedSecurityTier.EffectiveLevel(tenantContext, odinContext.Caller) != SecurityGroupType.Connected)
        {
            throw new OdinSecurityException("Caller is a connection the owner has not reviewed yet");
        }

        // The caller's circles come from a peer context cached for up to an hour, which disabling a circle doesn't
        // clear, so whether it is enabled is asked now
        if (odinContext.Caller.Circles?.Contains(circleId) != true || !await circleDefinitionService.IsEnabledAsync(circleId))
        {
            throw new OdinSecurityException($"Caller is not a member of circle {circleId}");
        }

        var row = await db.CircleMemberCached.GetAsync(circleId, odinContext.Caller.OdinId!.Value.ToHashId());
        return row?.sealedShareKey;
    }

    /// <summary>
    /// The circle, when the caller is its owning app (or the owner console) and it is an app circle; never an
    /// owner circle, which has no app key to lock the circle key with (plan, decision 2).  An app is told nothing
    /// about circles it doesn't own (<see cref="CircleMembershipService.GetCircleCallerMayManageAsync"/>).
    /// </summary>
    private async Task<CircleRecord> GetCircleCallerMayHandleShareKeysOfAsync(GuidId circleId, string action,
        IOdinContext odinContext)
    {
        var circle = await circleMembershipService.GetCircleCallerMayManageAsync(circleId, action, odinContext);
        if (SystemAppConstants.IsOwnerConsole(circle.AppId))
        {
            throw new OdinSecurityException($"Cannot {action} circle {circleId}: an owner circle has no share key");
        }

        return circle;
    }

    /// <summary>Checks what the server can check of a blob it can't open: its size, and the format it knows.</summary>
    private static void AssertFormat(byte[] blob, int maxLength, string name)
    {
        if (blob == null || blob.Length < 2 || blob.Length > maxLength || blob[0] != FormatVersion)
        {
            throw new OdinClientException(
                $"{name} must be a format version {FormatVersion} share key of at most {maxLength} bytes",
                OdinClientErrorCode.InvalidShareKey);
        }
    }
}

/// <summary>One member's sealed copy of a circle's share key.</summary>
public class SealedShareKey
{
    public string OdinId { get; set; }

    public byte[] SealedKey { get; set; }
}
