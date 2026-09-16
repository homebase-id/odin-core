#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// Connect two V2 in-process identities for peer flows. <see cref="ConnectAsync"/> grants the
/// sender drive permission on the recipient's copy; pass
/// <c>recipientPermissionOnSenderDrive</c> to also grant the recipient access on the sender's copy
/// (required for callbacks like the read-receipt path, which hits <c>AssertCanWriteToDrive</c> on
/// the sender's drive when the recipient calls back).
/// </summary>
/// <remarks>
/// All HTTP between the two identities flows through the in-process
/// <see cref="TestPeerHttpClientFactory"/>.
/// </remarks>
public static class PeerFlow
{
    /// <summary>
    /// One-shot setup for peer-transfer tests: creates the drive on both sides and connects them.
    /// Returns the drive so callers can inline it.
    /// </summary>
    /// <param name="drive">
    /// The drive to create on both sides. Omit for a fresh one; pass <c>spec.TargetDrive</c> when the
    /// drive identity comes from a <see cref="CallerSpec"/>, so the caller's grant lands on the same
    /// drive the test then writes to.
    /// </param>
    public static async Task<TargetDrive> CreatePeerDriveAsync(
        OwnerSession sender,
        OwnerSession recipient,
        DrivePermission senderPermissionOnRecipientDrive,
        string label = "shared",
        DrivePermission? recipientPermissionOnSenderDrive = null,
        bool allowAnonymousReads = true,
        TargetDrive? drive = null,
        Dictionary<string, string>? attributes = null)
    {
        drive ??= TargetDrive.NewTargetDrive();
        await sender.Admin.EnsureDrive(drive, $"{sender.Identity} {label}",
            allowAnonymousReads: allowAnonymousReads, attributes: attributes);
        await recipient.Admin.EnsureDrive(drive, $"{recipient.Identity} {label}",
            allowAnonymousReads: allowAnonymousReads, attributes: attributes);
        await ConnectAsync(sender, recipient, drive, senderPermissionOnRecipientDrive,
            recipientPermissionOnSenderDrive);
        return drive;
    }

    /// <summary>
    /// Connect two identities. By default only the sender is granted, on the recipient's drive.
    /// Pass <paramref name="recipientPermissionOnSenderDrive"/> to also grant the recipient on the
    /// sender's drive — the same value for a symmetric connection, a different one where the two
    /// directions genuinely differ (the read-receipt refusal tests need Read one way, Write the other).
    /// </summary>
    public static async Task ConnectAsync(
        OwnerSession sender,
        OwnerSession recipient,
        TargetDrive sharedDrive,
        DrivePermission senderPermissionOnRecipientDrive,
        DrivePermission? recipientPermissionOnSenderDrive = null)
    {
        // Circle the recipient creates: grants the sender access on the recipient's drive.
        var recipientCircleId = Guid.NewGuid();
        await EnsureCircleAsync(recipient, recipientCircleId, $"peer-recv-{recipientCircleId:N}",
            sharedDrive, senderPermissionOnRecipientDrive);

        // Optional reverse: circle the sender creates and includes when sending the request,
        // so the recipient gets access on the sender's drive once the request lands.
        GuidId[]? senderGrantedCircles = null;
        if (recipientPermissionOnSenderDrive is { } reversePermission)
        {
            var senderCircleId = Guid.NewGuid();
            await EnsureCircleAsync(sender, senderCircleId, $"peer-send-{senderCircleId:N}",
                sharedDrive, reversePermission);
            senderGrantedCircles = new GuidId[] { senderCircleId };
        }

        var senderConnections = new UniversalCircleNetworkRequestsApiClient(sender.Identity, sender.Factory);
        var sendReq = await senderConnections.SendConnectionRequest(recipient.Identity, senderGrantedCircles);
        Assert.That(sendReq.IsSuccessStatusCode, Is.True,
            $"SendConnectionRequest from {sender.Identity} to {recipient.Identity} failed: {sendReq.StatusCode}");

        var recipientConnections = new UniversalCircleNetworkRequestsApiClient(recipient.Identity, recipient.Factory);
        var accept = await recipientConnections.AcceptConnectionRequest(sender.Identity, new GuidId[] { recipientCircleId });
        Assert.That(accept.IsSuccessStatusCode, Is.True,
            $"AcceptConnectionRequest on {recipient.Identity} failed: {accept.StatusCode}");
    }

    /// <summary>
    /// Deliver what the sender has queued: drain the sender's outbox, then process each recipient's
    /// inbox for <paramref name="drive"/>. The universal peer-delivery idiom — the fast host registers
    /// the outbox background service but never starts it, so nothing moves without this.
    /// </summary>
    public static async Task DistributeAsync(
        OwnerSession sender, IEnumerable<OwnerSession> recipients, TargetDrive drive)
    {
        await sender.Sync.DrainOutboxAsync();
        foreach (var recipient in recipients)
        {
            await recipient.Sync.ProcessInboxAsync(drive);
        }
    }

    /// <inheritdoc cref="DistributeAsync(OwnerSession, IEnumerable{OwnerSession}, TargetDrive)"/>
    public static Task DistributeAsync(OwnerSession sender, OwnerSession recipient, TargetDrive drive) =>
        DistributeAsync(sender, [recipient], drive);

    private static async Task EnsureCircleAsync(
        OwnerSession owner, Guid circleId, string name, TargetDrive drive, DrivePermission permission)
    {
        var grant = TestUtils.CreatePermissionGrantRequest(drive, permission);
        var resp = await owner.Admin.CreateCircle(circleId, name, grant);
        Assert.That(resp.IsSuccessStatusCode, Is.True,
            $"CreateCircle on {owner.Identity} failed: {resp.StatusCode}");
    }
}
