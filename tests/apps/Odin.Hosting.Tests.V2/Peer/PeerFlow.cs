#nullable enable
using System;
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
/// Connect two V2 in-process identities for peer flows. The recipient creates a circle granting
/// the named drive permission, the sender requests connection, the recipient accepts into the
/// circle. After <see cref="ConnectAsync"/> returns the two identities are mutually-connected and
/// the sender holds <paramref name="senderPermissionOnRecipientDrive"/> on the recipient's
/// <paramref name="sharedDrive"/>.
/// </summary>
/// <remarks>
/// Caller is responsible for creating <c>sharedDrive</c> on the recipient's side before invoking
/// (the sender doesn't need a local copy of the drive for outbound file transfer to work — the
/// transit subsystem builds it from the upload metadata). All HTTP between sender and recipient
/// flows through the in-process <see cref="TestPeerHttpClientFactory"/>.
/// </remarks>
public static class PeerFlow
{
    /// <summary>
    /// One-shot setup for peer-transfer tests: creates <paramref name="sharedDrive"/> on both sides
    /// and connects <paramref name="sender"/> to <paramref name="recipient"/> with
    /// <paramref name="senderPermissionOnRecipientDrive"/>. Returns the created drive so callers can
    /// inline it: <c>var drive = await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Write);</c>.
    /// </summary>
    public static async Task<TargetDrive> CreatePeerDriveAsync(
        OwnerSession sender,
        OwnerSession recipient,
        DrivePermission senderPermissionOnRecipientDrive,
        string label = "shared")
    {
        var drive = TargetDrive.NewTargetDrive();
        await sender.Admin.CreateDrive(drive, $"{sender.Identity} {label}");
        await recipient.Admin.CreateDrive(drive, $"{recipient.Identity} {label}");
        await ConnectAsync(sender, recipient, drive, senderPermissionOnRecipientDrive);
        return drive;
    }

    /// <summary>
    /// Bi-directional variant of <see cref="ConnectAsync"/>: both sender and recipient end up with
    /// the given drive permission on each other's copy of <paramref name="sharedDrive"/>. Useful
    /// for flows that require the recipient to call back into the sender (e.g. read receipts hit
    /// <c>PeerDriveIncomingTransferService.MarkFileAsReadAsync</c>, which AssertCanWriteToDrive on
    /// the sender's drive — one-way grants get a 403 there).
    /// </summary>
    public static async Task ConnectBidirectionalAsync(
        OwnerSession a,
        OwnerSession b,
        TargetDrive sharedDrive,
        DrivePermission permission)
    {
        var aCircleId = Guid.NewGuid();
        var bCircleId = Guid.NewGuid();

        var permGrant = new PermissionSetGrantRequest
        {
            Drives = new System.Collections.Generic.List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = sharedDrive,
                        Permission = permission
                    }
                }
            },
            PermissionSet = new PermissionSet(new System.Collections.Generic.List<int>())
        };

        var createA = await a.Admin.CreateCircle(aCircleId, $"peer-a-{aCircleId:N}", permGrant);
        Assert.That(createA.IsSuccessStatusCode, Is.True, $"CreateCircle on a failed: {createA.StatusCode}");

        var createB = await b.Admin.CreateCircle(bCircleId, $"peer-b-{bCircleId:N}", permGrant);
        Assert.That(createB.IsSuccessStatusCode, Is.True, $"CreateCircle on b failed: {createB.StatusCode}");

        var aConnections = new UniversalCircleNetworkRequestsApiClient(a.Identity, a.Factory);
        var sendReq = await aConnections.SendConnectionRequest(b.Identity, new GuidId[] { aCircleId });
        Assert.That(sendReq.IsSuccessStatusCode, Is.True,
            $"SendConnectionRequest from {a.Identity} to {b.Identity} failed: {sendReq.StatusCode}");

        var bConnections = new UniversalCircleNetworkRequestsApiClient(b.Identity, b.Factory);
        var accept = await bConnections.AcceptConnectionRequest(a.Identity, new GuidId[] { bCircleId });
        Assert.That(accept.IsSuccessStatusCode, Is.True,
            $"AcceptConnectionRequest on {b.Identity} failed: {accept.StatusCode}");
    }

    public static async Task<Guid> ConnectAsync(
        OwnerSession sender,
        OwnerSession recipient,
        TargetDrive sharedDrive,
        DrivePermission senderPermissionOnRecipientDrive)
    {
        var circleId = Guid.NewGuid();
        var createCircle = await recipient.Admin.CreateCircle(circleId, $"peer-{circleId:N}",
            new PermissionSetGrantRequest
            {
                Drives = new System.Collections.Generic.List<DriveGrantRequest>
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive
                        {
                            Drive = sharedDrive,
                            Permission = senderPermissionOnRecipientDrive
                        }
                    }
                },
                PermissionSet = new PermissionSet(new System.Collections.Generic.List<int>())
            });
        Assert.That(createCircle.IsSuccessStatusCode, Is.True,
            $"CreateCircle on recipient failed: {createCircle.StatusCode}");

        var senderConnections = new UniversalCircleNetworkRequestsApiClient(sender.Identity, sender.Factory);
        var sendReq = await senderConnections.SendConnectionRequest(recipient.Identity);
        Assert.That(sendReq.IsSuccessStatusCode, Is.True,
            $"SendConnectionRequest from {sender.Identity} to {recipient.Identity} failed: {sendReq.StatusCode}");

        var recipientConnections = new UniversalCircleNetworkRequestsApiClient(recipient.Identity, recipient.Factory);
        var accept = await recipientConnections.AcceptConnectionRequest(sender.Identity, new GuidId[] { circleId });
        Assert.That(accept.IsSuccessStatusCode, Is.True,
            $"AcceptConnectionRequest on {recipient.Identity} failed: {accept.StatusCode}");

        return circleId;
    }
}
