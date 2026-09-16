#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Util;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Concepts;

/// <summary>
/// The caller matrix the two <c>_Universal/Concepts</c> fixtures share. Deliberately <b>not</b>
/// <see cref="CallerSpec"/>: both of their non-owner rows grant permission <i>keys only</i>, with no
/// drive grant at all, which <see cref="CallerSpec.App"/> / <see cref="CallerSpec.Guest"/> cannot
/// express — they always attach a <c>DriveGrantRequest</c>. Building one here would either create a
/// drive the original never created or grant access the original never granted.
/// </summary>
/// <remarks>
/// Each row is named for the V1 <c>IApiClientContext</c> it came from so a failure names the same
/// thing the original did. The guest row reproduces
/// <c>ConnectedIdentityLoggedInOnGuestApi(TestIdentities.Pippin.OdinId, …)</c>: a YouAuth domain
/// named for the acting identity itself. In both originals the identity that context was built
/// against <i>is</i> Pippin, so deriving the domain from the owner session keeps it identical.
/// </remarks>
public sealed record CollabCallerSpec(string Name, Func<OwnerSession, Task<IV2Caller>> Build)
{
    public override string ToString() => Name;

    /// <summary>V1 <c>OwnerClientContext</c>.</summary>
    public static CollabCallerSpec Owner() =>
        new("OwnerClientContext", o => Task.FromResult<IV2Caller>(o));

    /// <summary>V1 <c>AppPermissionsKeysOnly(TestPermissionKeyList(PermissionKeys.UseTransitWrite))</c>.</summary>
    public static CollabCallerSpec AppWithOnlyUseTransitWrite() =>
        new("AppPermissionsKeysOnly", async o => await AppSession.SetupAsync(o, new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.UseTransitWrite)
        }));

    /// <summary>
    /// V1 <c>ConnectedIdentityLoggedInOnGuestApi(Pippin, TestPermissionKeyList(PermissionKeys.ReadWhoIFollow))</c>.
    /// Unlike <c>GuestSpecifyAccessToDrive</c> (whose keys are silently dropped — see the README), this
    /// context really does put its keys on the circle, so they are carried.
    /// </summary>
    public static CollabCallerSpec ConnectedIdentityLoggedInOnGuestApi() =>
        new("ConnectedIdentityLoggedInOnGuestApi", async o => await GuestSession.SetupAsync(o,
            new PermissionSetGrantRequest
            {
                PermissionSet = new PermissionSet(PermissionKeys.ReadWhoIFollow)
            },
            new AsciiDomainName(o.Identity.DomainName)));
}

/// <summary>
/// The arrange both <c>_Universal/Concepts</c> ports share: a collaboration-channel drive on one
/// identity, a circle granting members access to it, and the connection handshake that puts them in
/// that circle.
/// </summary>
/// <remarks>
/// The <c>IsCollaborativeChannel</c> attribute is load-bearing rather than cosmetic:
/// <c>PeerFileUpdateWriter.DetermineAclAsync</c> keeps the sender's ACL on the collaboration
/// channel's copy instead of narrowing it to owner-only, which is the whole point of both fixtures.
/// <see cref="Peer.PeerFlow.ConnectAllAsync"/> is not used — it builds a mesh with no drive
/// attributes and a circle per identity, whereas these scenarios need a star (every member connects
/// to the collaboration channel only) over one attributed drive the channel alone hosts.
/// </remarks>
public static class CollabScenario
{
    public static async Task DisableAutoAcceptIntroductionsAsync(params OwnerSession[] sessions)
    {
        foreach (var session in sessions)
        {
            await session.Admin.DisableAutoAcceptIntroductions();
        }
    }

    /// <summary>
    /// Creates the collaboration-channel drive on <paramref name="collabChannel"/> and the circle
    /// members are granted on connect, then runs the request/accept handshake for each member.
    /// Returns the circle id, which the fixtures also use as a file ACL.
    /// </summary>
    public static async Task<Guid> PrepareScenarioAsync(
        OwnerSession collabChannel,
        IReadOnlyList<OwnerSession> members,
        TargetDrive collabChannelDrive,
        DrivePermission memberPermission,
        string driveName,
        bool allowAnonymousReads)
    {
        await DisableAutoAcceptIntroductionsAsync([collabChannel, .. members]);

        var createDriveResponse = await collabChannel.Admin.CreateDrive(collabChannelDrive, driveName,
            allowAnonymousReads: allowAnonymousReads,
            allowSubscriptions: true, //required for distributing push notifications
            attributes: DriveSpec.CollabAttributes);
        Assert.That(createDriveResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var circleId = Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(collabChannelDrive, memberPermission);
        await collabChannel.Admin.CreateCircle(circleId, "circle with some access", permissions);

        foreach (var member in members)
        {
            var send = await member.Connections.SendConnectionRequest(collabChannel.Identity);
            Assert.That(send.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var accept = await collabChannel.Connections.AcceptConnectionRequest(
                member.Identity, [(GuidId)circleId]);
            Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        return circleId;
    }
}
