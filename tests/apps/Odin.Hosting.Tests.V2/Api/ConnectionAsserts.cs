using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// Assertions about a connection record, in the same spirit as <see cref="DriveAsserts"/>: shared
/// test vocabulary rather than a client facade.
/// </summary>
/// <remarks>
/// The drive-grant check below is the closing assertion of every <c>PrepareScenario</c> the ported
/// peer fixtures carried, and four of them had spelled it out inline.
/// </remarks>
public static class ConnectionAsserts
{
    /// <summary>
    /// <paramref name="owner"/> holds a connection record for <paramref name="connectedIdentity"/>
    /// carrying exactly one circle grant that grants <paramref name="permission"/> on
    /// <paramref name="targetDrive"/>.
    /// </summary>
    public static async Task AssertHasCircleGrantForDrive(
        OwnerSession owner,
        OdinId connectedIdentity,
        TargetDrive targetDrive,
        DrivePermission permission)
    {
        var expectedPermissionedDrive = new PermissionedDrive
        {
            Drive = targetDrive,
            Permission = permission
        };

        var response = await owner.Connections.GetConnectionInfo(connectedIdentity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.AccessGrant.CircleGrants,
            Has.Exactly(1).Matches<RedactedCircleGrant>(
                cg => cg.DriveGrants.Any(dg => dg.PermissionedDrive == expectedPermissionedDrive)),
            $"{owner.Identity} should hold one circle grant for {connectedIdentity} on {targetDrive.Alias} at {permission}");
    }
}
