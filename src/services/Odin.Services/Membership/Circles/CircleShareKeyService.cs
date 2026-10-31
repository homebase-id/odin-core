using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Apps;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Network;

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
public class CircleShareKeyService(
    IdentityDatabase db,
    CircleDefinitionService circleDefinitionService,
    CircleMembershipService circleMembershipService,
    CircleNetworkService circleNetworkService,
    IOdinHttpClientFactory odinHttpClientFactory)
{
    /// <summary>
    /// The first byte of every share key blob: key header version 2 (#2024), AES-GCM from the start.
    /// </summary>
    public const byte FormatVersion = 2;

    /// <summary>The bounds of the columns that keep them (Circle.appKeyLockedShareKey, CircleMember.sealedShareKey).</summary>
    public const int MaxShareKeyLength = 512;

    public const int MaxSealedShareKeyLength = 2048;

    /// <summary>The circle's share key, locked with the owning app's key; null until the app sets one.</summary>
    public async Task<byte[]> GetShareKeyAsync(GuidId circleId, IOdinContext odinContext)
    {
        await AssertCallerMayHandleShareKeysAsync(circleId, "read the share key of", odinContext);
        var circle = await db.CircleCached.GetAsync(circleId);
        return circle?.appKeyLockedShareKey;
    }

    /// <summary>
    /// Sets the circle's share key, once.  Refused when it already has one: two devices sharing with a new
    /// circle at once can't each set a different key, and the loser reads the winner's.  Replacing it is key
    /// rotation, not in the first version.
    /// </summary>
    public async Task SetShareKeyAsync(GuidId circleId, byte[] appKeyLockedShareKey, IOdinContext odinContext)
    {
        await AssertCallerMayHandleShareKeysAsync(circleId, "set the share key of", odinContext);
        AssertFormat(appKeyLockedShareKey, MaxShareKeyLength, nameof(appKeyLockedShareKey));

        if (await db.CircleCached.SetShareKeyAsync(circleId, appKeyLockedShareKey) == 0)
        {
            throw new OdinClientException($"Circle {circleId} already has a share key",
                OdinClientErrorCode.CircleShareKeyAlreadySet);
        }
    }

    /// <summary>
    /// The circle's members with no sealed copy of its key yet: new members, and those whose row was made
    /// again.  The owning app's device seals for each and stores them with <see cref="SetSealedShareKeysAsync"/>.
    /// </summary>
    public async Task<List<OdinId>> GetMembersWithoutSealedShareKeyAsync(GuidId circleId, IOdinContext odinContext)
    {
        await AssertCallerMayHandleShareKeysAsync(circleId, "list the unsealed members of", odinContext);

        var rows = await db.CircleMemberCached.GetCircleMembersAsync(circleId);
        return rows
            .Where(r => r.sealedShareKey == null)
            .Select(r => OdinSystemSerializer.Deserialize<CircleMemberStorageData>(r.data.ToStringFromUtf8Bytes()))
            .Where(d => d.DomainType == DomainType.Identity)
            .Select(d => new OdinId(d.DomainName))
            .ToList();
    }

    /// <summary>
    /// Stores members' sealed copies of the circle key, all or none.  Each must be a member, and the circle must
    /// have its key: a copy of a key the circle doesn't have can't be right.  A copy replaces an earlier one, as
    /// when the app re-seals to a member's new drive key.
    /// </summary>
    public async Task SetSealedShareKeysAsync(GuidId circleId, IReadOnlyCollection<SealedShareKey> sealedKeys,
        IOdinContext odinContext)
    {
        await AssertCallerMayHandleShareKeysAsync(circleId, "seal the share key of", odinContext);

        if (sealedKeys == null || sealedKeys.Count == 0)
        {
            throw new OdinClientException("No sealed share keys supplied", OdinClientErrorCode.ArgumentError);
        }

        foreach (var sealedKey in sealedKeys)
        {
            AssertFormat(sealedKey?.SealedKey, MaxSealedShareKeyLength, nameof(SealedShareKey.SealedKey));
        }

        var circle = await db.CircleCached.GetAsync(circleId);
        if (circle?.appKeyLockedShareKey == null)
        {
            throw new OdinClientException($"Circle {circleId} has no share key to seal",
                OdinClientErrorCode.CircleShareKeyNotSet);
        }

        await using var tx = await db.BeginStackedTransactionAsync();
        foreach (var sealedKey in sealedKeys)
        {
            var memberId = new OdinId(sealedKey.OdinId).ToHashId();
            if (await db.CircleMemberCached.SetSealedShareKeyAsync(circleId, memberId, sealedKey.SealedKey) == 0)
            {
                throw new OdinClientException($"{sealedKey.OdinId} is not a member of circle {circleId}",
                    OdinClientErrorCode.NotACircleMember);
            }
        }

        tx.Commit();
    }

    /// <summary>
    /// The calling peer's own sealed copy of a circle's key; null when they have none yet.  Served only to a
    /// connected caller the circle currently grants (its enabled circles), and never anyone else's copy: the
    /// row is looked up by the caller's identity, not by anything they send.
    /// </summary>
    public async Task<byte[]> GetCallersSealedShareKeyAsync(GuidId circleId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertCallerIsConnected();

        if (odinContext.Caller.Circles?.Any(c => c == circleId) != true)
        {
            throw new OdinSecurityException($"Caller is not a member of circle {circleId}");
        }

        var row = await db.CircleMemberCached.GetAsync(circleId, odinContext.Caller.OdinId!.Value.ToHashId());
        return row?.sealedShareKey;
    }

    /// <summary>
    /// Fetches this identity's sealed copy of <paramref name="odinId"/>'s circle key from their server; null when
    /// they have sealed none for this identity yet.
    /// </summary>
    public async Task<byte[]> GetSealedShareKeyFromPeerAsync(OdinId odinId, GuidId circleId, IOdinContext odinContext)
    {
        odinContext.PermissionsContext.AssertHasPermission(PermissionKeys.UseTransitRead);

        // The permission check above stands in for the one GetIcrAsync would make.
        var icr = await circleNetworkService.GetIcrAsync(odinId, odinContext, overrideHack: true);
        if (icr?.IsConnected() != true)
        {
            throw new OdinClientException($"Not connected to {odinId}", OdinClientErrorCode.NotAConnectedIdentity);
        }

        var token = icr.CreateClientAuthToken(odinContext.PermissionsContext.GetIcrKey());
        var client = await odinHttpClientFactory.CreateClientUsingAccessTokenAsync<IPeerCircleShareKeyHttpClient>(odinId, token);
        var response = await client.GetMySealedShareKey(circleId.Value);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new OdinSecurityException($"{odinId} refused: not a member of circle {circleId}");
        }

        if (!response.IsSuccessStatusCode || response.Content?.SealedKey == null)
        {
            throw new OdinClientException($"{odinId} returned {(int)response.StatusCode} for circle {circleId}",
                OdinClientErrorCode.RemoteServerReturnedInternalServerError);
        }

        return response.Content.SealedKey;
    }

    /// <summary>
    /// The owning app (or the owner console) of an app circle; never an owner circle, which has no app key
    /// to lock the circle key with (plan, decision 2).
    /// </summary>
    private async Task AssertCallerMayHandleShareKeysAsync(GuidId circleId, string action, IOdinContext odinContext)
    {
        await circleMembershipService.AssertCallerMayManageCircleAsync(circleId, action, odinContext);

        var circle = await circleDefinitionService.GetCircleAsync(circleId);
        if (circle == null)
        {
            throw new OdinClientException($"Circle {circleId} does not exist", OdinClientErrorCode.CircleNotFound);
        }

        if (SystemAppConstants.IsOwnerConsole(circle.AppId))
        {
            throw new OdinSecurityException($"Cannot {action} circle {circleId}: an owner circle has no share key");
        }
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
