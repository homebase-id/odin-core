using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.Peer;

public class PeerNotificationTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }


    public static IEnumerable TestCases()
    {
        // yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
        // yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.NotFound };
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.NotFound };
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task TransitSendsAppNotification(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // what is the primary thing being tested here?
        // when it's all done - the notification exists in frodo's notification list

        /*
         * I need to connect two hobbits
         * Both need an app named 'chat'
         * sam sends a chat to frodo and includes an app notification (done via app)
         * the notification should be queued in sam's outbox
         * I call process notifications on sam's owner api
         * the notification will then exist in frodo's inbox
         * I call process notifications on frodo's owner api
         * here we ignore whether the push actually went out (because that's a whole other set of dependencies)
         * the notification will then exist in frodo's notification's list
         */

        //Create two connected hobbits

        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;

        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);

        var targetDrive = callerContext.TargetDrive;

        Guid appId = Guid.NewGuid();
        var samCircleId = await PrepareAppAccess(ownerSam, appId, targetDrive);
        var frodoCircleId = await PrepareAppAccess(ownerFrodo, appId, targetDrive);

        var (samAppToken, samAppSharedSecret) = await ownerSam.AppManager.RegisterAppClient(appId);
        // var (frodoAppToken, frodoAppSharedSecret) = await ownerFrodo.AppManager.RegisterAppClient(appId);

        // Both must be connected
        await ownerSam.Connections.SendConnectionRequest(frodo.OdinId, new List<GuidId>() { samCircleId });
        await ownerFrodo.Connections.AcceptConnectionRequest(sam.OdinId, new List<GuidId>() { frodoCircleId });

        // Sam sends message over transit with notifications set
        var samDriveClient = new UniversalDriveApiClient(sam.OdinId, new AppApiClientFactory(samAppToken, samAppSharedSecret));

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
            Recipients = [frodo.OdinId],
            UseAppNotification = true,
            AppNotificationOptions = options
        };

        var uploadFileResponse = await samDriveClient.UploadNewMetadata(targetDrive, fileMetadata, transitOptions);
        Assert.IsTrue(uploadFileResponse.IsSuccessStatusCode, $"Failed with status code {uploadFileResponse.StatusCode}");
        Assert.IsTrue(uploadFileResponse.Content.RecipientStatus.TryGetValue(frodo.OdinId, out var frodoTransferStatus));
        Assert.IsTrue(frodoTransferStatus == TransferStatus.Enqueued, $"transfer status: {frodoTransferStatus}");


        await ownerFrodo.DriveRedux.WaitForEmptyOutbox(targetDrive);

        // Frodo should have the notification in his list

        var getNotificationsResponse = await ownerFrodo.AppNotifications.GetList(1000);
        Assert.IsTrue(getNotificationsResponse.IsSuccessStatusCode);
        var notification = getNotificationsResponse.Content.Results.SingleOrDefault(n => n.Options.TagId == options.TagId);
        Assert.IsNotNull(notification);
        Assert.IsTrue(notification.SenderId == sam.OdinId);
        Assert.IsTrue(notification.Options.AppId == appId);
        Assert.IsTrue(notification.Options.TypeId == options.TypeId);
        Assert.IsTrue(notification.Options.Silent == options.Silent);
        Assert.IsTrue(notification.Options.TagId == options.TagId);

        await ownerFrodo.Connections.DisconnectFrom(sam.OdinId);
        await ownerSam.Connections.DisconnectFrom(frodo.OdinId);
    }

    private async Task<Guid> PrepareAppAccess(OwnerApiClientRedux ownerClient, Guid appId, TargetDrive targetDrive)
    {
        // Both need the same target drive

        var circleId = Guid.NewGuid();

        await ownerClient.DriveManager.CreateDrive(targetDrive, "Chat Drive", "", false);
        await ownerClient.Network.CreateCircle(circleId, "Chat Participants", new PermissionSetGrantRequest()
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadWhoIFollow) //does not matter, just need to create the circle
        });

        // Both need the same app
        //  The app must have write access to the drive
        //  the app must have the circle

        var appPermissions = new PermissionSetGrantRequest()
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
        var circles = new List<Guid>() { circleId };
        var circlePermissions = new PermissionSetGrantRequest()
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

        var response = await ownerClient.AppManager.RegisterApp(appId, appPermissions, circles, circlePermissions);
        Assert.IsTrue(response.IsSuccessStatusCode, $"Failed with status code {response.StatusCode}");

        return circleId;
    }
}