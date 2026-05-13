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

// NOTE: the class is named "Peer" inside the namespace "…V2.Peer". C# handles this — within the
// namespace you write `Peer.ConnectAsync(...)` and the compiler resolves to the class, not the
// namespace. From outside, `using Odin.Hosting.Tests.V2.Peer;` brings the namespace in scope and
// `Peer.ConnectAsync` still resolves correctly. If the collision ever does cause friction,
// rename this class to `PeerFlow` — it's a one-shot find-replace.

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
public static class Peer
{
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
