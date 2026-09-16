using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
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
/// The mesh connect was promoted to <see cref="PeerFlow.ConnectAllAsync"/> once the consumer count
/// made the case: seven V1 fixtures still on WebScaffold call <c>CreateConnectedHobbits</c> across 18
/// call sites. What remains here is the app registration, which stays fixture-side on purpose.
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
    /// Connects every identity to every other over <paramref name="targetDrive"/> and gives each the
    /// app the V1 original registered. The mesh connect itself lives on
    /// <see cref="PeerFlow.ConnectAllAsync"/>; what stays here is the app half, which is
    /// fixture-specific and deliberately not part of a peer-connect helper.
    /// </summary>
    public static async Task ConnectAllAsync(OwnerSession[] hobbits, TargetDrive targetDrive)
    {
        var appId = Guid.NewGuid();

        foreach (var hobbit in hobbits)
        {
            await SetupTestSampleAppAsync(hobbit, appId, targetDrive);
        }

        await PeerFlow.ConnectAllAsync(hobbits, targetDrive);
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
