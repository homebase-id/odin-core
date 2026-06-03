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
/// sender drive permission on the recipient's copy; pass <c>bidirectional: true</c> to also grant
/// the recipient the same permission on the sender's copy (required for callbacks like the
/// read-receipt path, which hits <c>AssertCanWriteToDrive</c> on the sender's drive when the
/// recipient calls back).
/// </summary>
/// <remarks>
/// All HTTP between the two identities flows through the in-process
/// <see cref="TestPeerHttpClientFactory"/>.
/// </remarks>
public static class PeerFlow
{
    /// <summary>
    /// One-shot setup for peer-transfer tests: creates the drive on both sides and connects them.
    /// Returns the created drive so callers can inline it.
    /// </summary>
    public static async Task<TargetDrive> CreatePeerDriveAsync(
        OwnerSession sender,
        OwnerSession recipient,
        DrivePermission senderPermissionOnRecipientDrive,
        string label = "shared",
        bool bidirectional = false,
        bool allowAnonymousReads = true)
    {
        var drive = TargetDrive.NewTargetDrive();
        await sender.Admin.CreateDrive(drive, $"{sender.Identity} {label}", allowAnonymousReads: allowAnonymousReads);
        await recipient.Admin.CreateDrive(drive, $"{recipient.Identity} {label}", allowAnonymousReads: allowAnonymousReads);
        await ConnectAsync(sender, recipient, drive, senderPermissionOnRecipientDrive, bidirectional);
        return drive;
    }

    /// <summary>
    /// Connect two identities. <paramref name="bidirectional"/>=false (default) grants only the
    /// sender; =true grants both sides equivalent permission on each other's drive.
    /// </summary>
    public static async Task ConnectAsync(
        OwnerSession sender,
        OwnerSession recipient,
        TargetDrive sharedDrive,
        DrivePermission senderPermissionOnRecipientDrive,
        bool bidirectional = false)
    {
        // Circle the recipient creates: grants the sender access on the recipient's drive.
        var recipientCircleId = Guid.NewGuid();
        await EnsureCircleAsync(recipient, recipientCircleId, $"peer-recv-{recipientCircleId:N}",
            sharedDrive, senderPermissionOnRecipientDrive);

        // Optional reverse: circle the sender creates and includes when sending the request,
        // so the recipient gets access on the sender's drive once the request lands.
        GuidId[]? senderGrantedCircles = null;
        if (bidirectional)
        {
            var senderCircleId = Guid.NewGuid();
            await EnsureCircleAsync(sender, senderCircleId, $"peer-send-{senderCircleId:N}",
                sharedDrive, senderPermissionOnRecipientDrive);
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

    private static async Task EnsureCircleAsync(
        OwnerSession owner, Guid circleId, string name, TargetDrive drive, DrivePermission permission)
    {
        var grant = new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = permission }
                }
            },
            PermissionSet = new PermissionSet(new List<int>())
        };
        var resp = await owner.Admin.CreateCircle(circleId, name, grant);
        Assert.That(resp.IsSuccessStatusCode, Is.True,
            $"CreateCircle on {owner.Identity} failed: {resp.StatusCode}");
    }
}
