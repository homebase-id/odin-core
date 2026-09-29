using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of <c>_Universal/Peer/PeerNotificationTests.cs</c>. Sam sends a file to Frodo over transit
/// with <see cref="TransitOptions.UseAppNotification"/> set; the notification must end up in Frodo's
/// notification list with the sender, app id, type id, silent flag and tag id the sender chose.
/// </summary>
/// <remarks>
/// Checked port. Carried defects from the original, left alone:
/// <list type="bullet">
/// <item>The original's case source declared <c>HttpStatusCode.NotFound</c> as the expected status,
/// but the test body never read that parameter — nothing in the fixture asserts a status code
/// against it. With only one live row the matrix is a plain <c>[Test]</c>; the row and its unasserted
/// expectation are preserved as comments above the test so the expectation stays visible.</item>
/// <item>The <c>IApiClientContext</c> was used only as a <c>TargetDrive</c> carrier:
/// <c>Initialize</c>/<c>GetFactory</c> were never called on it, and the acting client was built from
/// a hand-made <c>AppApiClientFactory</c>. The port therefore creates the drive directly and builds
/// the acting app caller explicitly, because both identities must share one <c>appId</c> — which
/// <see cref="AppSession.SetupAsync"/> supports via <c>knownAppId</c>.</item>
/// <item>The original registered an app client for Frodo too, then commented it out; only Sam's app
/// client is used. Frodo just needs the app registered.</item>
/// </list>
/// <c>Task.Delay(500)</c> becomes an explicit <c>DrainOutboxAsync</c> + <c>ProcessInboxAsync</c>: the
/// fast host never starts the outbox background service, so the passive wait would never complete.
/// </remarks>
[TestFixture]
public class PeerNotificationTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    // The original's single live row was [CallerSpec.Owner(DriveSpec.Secured()), HttpStatusCode.NotFound] —
    // one row, and the status was never asserted, so this is a plain [Test]. Its two commented-out
    // siblings, kept verbatim so a matrix is one edit away:
    //   [CallerSpec.Guest(DriveSpec.Secured(), DrivePermission.Write), HttpStatusCode.Forbidden]
    //   [CallerSpec.App(DriveSpec.Secured(), DrivePermission.Write), HttpStatusCode.NotFound]
    [Test]
    public async Task TransitSendsAppNotification()
    {
        //Create two connected hobbits

        var ownerSam = await LoginAsOwner(Identities.Sam);
        var ownerFrodo = await LoginAsOwner(Identities.Frodo);

        var targetDrive = TargetDrive.NewTargetDrive();

        var appId = Guid.NewGuid();
        var samCircleId = await PrepareDriveAndCircle(ownerSam, targetDrive);
        var frodoCircleId = await PrepareDriveAndCircle(ownerFrodo, targetDrive);

        // Sam's app + app client; Frodo only needs the same app registered.
        var samApp = await AppSession.SetupAsync(
            ownerSam,
            targetDrive,
            DrivePermission.ReadWrite,
            permissionKeys: [PermissionKeys.UseTransitWrite, PermissionKeys.SendPushNotifications],
            authorizedCircles: [samCircleId],
            circleMemberGrantRequest: CircleMemberWriteGrant(targetDrive),
            knownAppId: appId);

        await ownerFrodo.Admin.RegisterApp(
            appId,
            AppPermissions(targetDrive),
            [frodoCircleId],
            CircleMemberWriteGrant(targetDrive));

        // Both must be connected
        await ownerSam.Connections.SendConnectionRequest(ownerFrodo.Identity, new List<GuidId> { samCircleId });
        await ownerFrodo.Connections.AcceptConnectionRequest(ownerSam.Identity, new List<GuidId> { frodoCircleId });

        // Sam sends message over transit with notifications set
        var fileMetadata = SampleMetadataData.Create(101, acl: AccessControlList.Connected);
        fileMetadata.AllowDistribution = true;
        fileMetadata.AppData.Content = "some app content";

        var options = new AppNotificationOptions()
        {
            AppId = appId,
            TypeId = Guid.NewGuid(),
            Silent = true,
            UnEncryptedMessage = "some unencrypted message",
            TagId = Guid.NewGuid() //note: if this is meant to be the fileId, how can it be set by the client
        };

        var transitOptions = new TransitOptions()
        {
            Recipients = [ownerFrodo.Identity],
            UseAppNotification = true,
            AppNotificationOptions = options
        };

        var uploadFileResponse = await samApp.V1.Drive.UploadNewMetadata(targetDrive, fileMetadata, transitOptions);
        Assert.That(uploadFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(uploadFileResponse.Content.RecipientStatus.TryGetValue(ownerFrodo.Identity, out var frodoTransferStatus),
            Is.True);
        Assert.That(frodoTransferStatus, Is.EqualTo(TransferStatus.Enqueued));

        await ownerSam.Sync.DrainOutboxAsync();
        await ownerFrodo.Sync.ProcessInboxAsync(targetDrive);

        // Frodo should have the notification in his list

        var getNotificationsResponse = await ownerFrodo.V1.Notifications.GetList(1000);
        Assert.That(getNotificationsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var notification = getNotificationsResponse.Content.Results.SingleOrDefault(n => n.Options.TagId == options.TagId);
        Assert.That(notification, Is.Not.Null);
        Assert.That(notification.SenderId, Is.EqualTo((string)ownerSam.Identity));
        Assert.That(notification.Options.AppId, Is.EqualTo(appId));
        Assert.That(notification.Options.TypeId, Is.EqualTo(options.TypeId));
        Assert.That(notification.Options.Silent, Is.EqualTo(options.Silent));
        Assert.That(notification.Options.TagId, Is.EqualTo(options.TagId));
    }

    /// <summary>
    /// The drive + circle half of the original's <c>PrepareAppAccess</c>. The app-registration half
    /// differs per identity (Sam also needs an app client), so it stays in the test body.
    /// </summary>
    private static async Task<Guid> PrepareDriveAndCircle(OwnerSession ownerClient, TargetDrive targetDrive)
    {
        // Both need the same target drive

        var circleId = Guid.NewGuid();

        await ownerClient.Admin.CreateDrive(targetDrive, "Chat Drive", allowAnonymousReads: false);
        await ownerClient.Admin.CreateCircle(circleId, "Chat Participants", new PermissionSetGrantRequest()
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadWhoIFollow) //does not matter, just need to create the circle
        });

        return circleId;
    }

    // Both need the same app
    //  The app must have write access to the drive
    //  the app must have the circle
    private static PermissionSetGrantRequest AppPermissions(TargetDrive targetDrive) => new()
    {
        Drives = new List<DriveGrantRequest>()
        {
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = targetDrive,
                    Permission = DrivePermission.ReadWrite
                }
            }
        },
        PermissionSet =
            new PermissionSet(PermissionKeys.UseTransitWrite,
                PermissionKeys.SendPushNotifications) //TODO: add permissions for sending notifications?
    };

    // Register a 'chat' app that has readwrite access to the chat drive
    // and allows the chat-circle to use it via transit
    private static PermissionSetGrantRequest CircleMemberWriteGrant(TargetDrive targetDrive) => new()
    {
        Drives = new List<DriveGrantRequest>()
        {
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = targetDrive,
                    Permission = DrivePermission.Write
                }
            }
        }
    };
}
