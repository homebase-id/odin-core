using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Transit;

/// <summary>
/// Stand-in for <c>ScenarioBootstrapper.CreateConnectedHobbits</c>: give every identity the same
/// drive, one app over it and one circle granting read/write on it, then connect every pair — each
/// side granting the other its own circle.
/// </summary>
/// <remarks>
/// Deliberately local to this folder rather than on <c>Peer/PeerFlow</c>: <c>PeerFlow</c> is shared
/// framework, and only one ported fixture needs this shape so far. If a second one arrives, promoting
/// it is the right move.
/// <para>
/// The original registered an app <i>client</i> per identity as well and carried its token on a
/// <c>TestAppContext</c>; nothing in the one fixture that uses this reads a token — every call is made
/// as the owner — so that step is dropped, matching the same decision in
/// <c>Ported/Connections/CircleNetworkServiceTests.SetupTestSampleAppAsync</c>. It also returned a
/// <c>ScenarioContext</c> of app contexts and circle definitions; the sole caller discarded it, so
/// nothing is returned here.
/// </para>
/// </remarks>
internal static class HobbitScenario
{
    /// <summary>
    /// Connects every pair in <paramref name="hobbits"/> over <paramref name="targetDrive"/>. All
    /// identities get the same <paramref name="targetDrive"/> and the same app id, as the original did.
    /// </summary>
    public static async Task ConnectAllAsync(OwnerSession[] hobbits, TargetDrive targetDrive)
    {
        var appId = Guid.NewGuid();
        var circleIds = new Dictionary<string, Guid>();

        foreach (var hobbit in hobbits)
        {
            await SetupTestSampleAppAsync(hobbit, appId, targetDrive);

            var circleId = Guid.NewGuid();
            await hobbit.Admin.CreateCircle(circleId, $"Sender ({hobbit.Identity}) Circle",
                TestUtils.CreatePermissionGrantRequest(targetDrive, DrivePermission.ReadWrite));
            circleIds[hobbit.Identity] = circleId;
        }

        for (var i = 0; i < hobbits.Length; i++)
        {
            for (var j = i + 1; j < hobbits.Length; j++)
            {
                await ConnectAsync(hobbits[i], hobbits[j], circleIds);
            }
        }
    }

    private static async Task ConnectAsync(OwnerSession sender, OwnerSession recipient, IReadOnlyDictionary<string, Guid> circleIds)
    {
        var sendRequest = await sender.Connections.SendConnectionRequest(
            recipient.Identity, new List<GuidId> { circleIds[sender.Identity] });
        Assert.That(sendRequest.IsSuccessStatusCode, Is.True,
            $"SendConnectionRequest from {sender.Identity} to {recipient.Identity} failed: {sendRequest.StatusCode}");

        var accept = await recipient.Connections.AcceptConnectionRequest(
            sender.Identity, new List<GuidId> { circleIds[recipient.Identity] });
        Assert.That(accept.IsSuccessStatusCode, Is.True,
            $"AcceptConnectionRequest on {recipient.Identity} failed: {accept.StatusCode}");
    }

    /// <summary>The app drive plus an app holding full permission on it, as the original's <c>SetupTestSampleApp</c>.</summary>
    private static async Task SetupTestSampleAppAsync(OwnerSession owner, Guid appId, TargetDrive targetDrive)
    {
        await owner.Admin.CreateDrive(targetDrive, $"Test Drive name with type {targetDrive.Type}",
            allowAnonymousReads: false, ownerOnly: false);

        await owner.Admin.RegisterApp(appId, new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections, PermissionKeys.ReadConnectionRequests,
                PermissionKeys.UseTransitWrite),
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.All
                    }
                }
            }
        });
    }
}
