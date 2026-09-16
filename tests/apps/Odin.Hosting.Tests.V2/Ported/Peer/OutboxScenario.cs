using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// The <c>PrepareScenario</c> the four ported outbox fixtures each carried their own copy of: the
/// shared drive on both identities, a circle on the recipient granting the sender one permission on
/// it, the connection handshake, and the check that the grant actually landed on the recipient's
/// connection record.
/// </summary>
/// <remarks>
/// The four copies were byte-identical apart from
/// <c>OutboxProcessingTestsMultipleRecipients</c>, which sent the connection request before the
/// recipient had created its drive and circle and accepted afterwards. That ordering is not
/// expressible through <see cref="PeerFlow.CreatePeerDriveAsync"/>, which creates both drives and the
/// circle first; the end state is the same, and the multiple-recipients fixture's live test asserts
/// nothing about the intermediate state.
/// </remarks>
internal static class OutboxScenario
{
    /// <summary>
    /// Drive on both sides, circle on the recipient granting <paramref name="drivePermission"/> to the
    /// sender, connected. The drive is created with anonymous reads off, as every original did.
    /// </summary>
    public static async Task PrepareAsync(
        OwnerSession sender,
        OwnerSession recipient,
        TargetDrive targetDrive,
        DrivePermission drivePermission)
    {
        await PeerFlow.CreatePeerDriveAsync(sender, recipient, drivePermission,
            label: "target drive",
            allowAnonymousReads: false,
            drive: targetDrive);

        await AssertRecipientGrantedSenderAsync(sender, recipient, targetDrive, drivePermission);
    }

    /// <summary>
    /// The originals' closing check on <c>PrepareScenario</c>: the recipient holds an ICR for the
    /// sender carrying exactly one circle grant for the drive at that permission.
    /// </summary>
    public static async Task AssertRecipientGrantedSenderAsync(
        OwnerSession sender,
        OwnerSession recipient,
        TargetDrive targetDrive,
        DrivePermission drivePermission)
    {
        var expectedPermissionedDrive = new PermissionedDrive
        {
            Drive = targetDrive,
            Permission = drivePermission
        };

        var response = await recipient.Connections.GetConnectionInfo(sender.Identity);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.AccessGrant.CircleGrants,
            Has.Exactly(1).Matches<RedactedCircleGrant>(
                cg => cg.DriveGrants.Any(dg => dg.PermissionedDrive == expectedPermissionedDrive)));
    }
}
