using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests._Universal.Concepts.CollabChat;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class CollaborationChatPushNotificationTests
{
    private WebScaffold _scaffold;

    private static readonly Dictionary<string, string> IsCollaborativeChannelAttributes = new()
        { { BuiltInDriveAttributes.IsCollaborativeChannel, bool.TrueString } };


    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
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

    public static IEnumerable OwnerAllowed()
    {
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable AppWithOnlyUseTransitWrite()
    {
        yield return new object[]
            { new AppPermissionKeysOnly(new TestPermissionKeyList(PermissionKeys.UseTransitWrite)), HttpStatusCode.OK };
    }

    public static IEnumerable GuestNotAllowed()
    {
        yield return new object[]
        {
            new ConnectedIdentityLoggedInOnGuestApi(TestIdentities.Pippin.OdinId, new TestPermissionKeyList(PermissionKeys.ReadWhoIFollow)),
            HttpStatusCode.MethodNotAllowed
        };
    }

    [Test]
    // [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppWithOnlyUseTransitWrite))]
    [TestCaseSource(nameof(GuestNotAllowed))]
    [Description("the collab channel identity uploads a chat message to itself.  A push notification should " +
                 "still be scheduled (pending options from api)")]
    public async Task CanAddPushNotificationWhenUploadingChatMessageDirectly(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var debugTimeout = _scaffold.DebugTimeout;

        var collabChatIdentity = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Collab);
        var member1 = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);
        var member2 = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);

        var collabChatDrive = TargetDrive.NewTargetDrive();
        var chatCircleId = Guid.NewGuid();
        var collabChatAppId = Guid.NewGuid();
        var peerSubscriptionId = Guid.NewGuid();

        await SetupScenario(collabChatIdentity, member1, member2, collabChatDrive, chatCircleId, collabChatAppId, peerSubscriptionId);

        var notificationOptions = new AppNotificationOptions
        {
            AppId = collabChatAppId,
            PeerSubscriptionId = peerSubscriptionId,
            Recipients = [member2.OdinId, member1.OdinId]

            // TypeId = default,
            // TagId = default,
            // Silent = false,
            // UnEncryptedMessage = null
        };

        var (response, _, _) = await AwaitNewEncryptedFileUpload(
            collabChatIdentity,
            collabChatDrive,
            chatCircleId,
            notificationOptions);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            // wait for push notifications to be distributed
            await collabChatIdentity.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, debugTimeout);
            await collabChatIdentity.DriveRedux.WaitForEmptyOutbox(collabChatDrive, debugTimeout);

            //
            // Assert: all notification recipients received a notification in their list
            //
            foreach (var recipient in notificationOptions.Recipients)
            {
                var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.InitializedIdentities[recipient]);
                var getNotificationResponse = await client.AppNotifications.GetList(1000);
                ClassicAssert.IsTrue(getNotificationResponse.IsSuccessStatusCode);

                //TODO: determine who the sender should actually be?
                var notificationsFromCollabChat = getNotificationResponse.Content.Results
                    .Where(notification => notification.SenderId == collabChatIdentity.OdinId);

                ClassicAssert.IsTrue(notificationsFromCollabChat.Any());
                //TODO: where do we check this? in the notifications or the log?
            }
        }

        await callerContext.Cleanup();
        await CleanupScenario(collabChatIdentity, member1, member2);
    }


    [Test]
    // [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppWithOnlyUseTransitWrite))]
    [TestCaseSource(nameof(GuestNotAllowed))]
    public async Task CanAddPushNotificationWhenSendingChatMessageOverPeer(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var debugTimeout = _scaffold.DebugTimeout;

        var collabChatIdentity = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Collab);
        var member1 = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);
        var member2 = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);

        var collabChatDrive = TargetDrive.NewTargetDrive();
        var chatCircleId = Guid.NewGuid();
        var collabChatAppId = Guid.NewGuid();
        var peerSubscriptionId = Guid.NewGuid();

        await SetupScenario(collabChatIdentity, member1, member2, collabChatDrive, chatCircleId, collabChatAppId, peerSubscriptionId);

        var notificationOptions = new AppNotificationOptions
        {
            AppId = collabChatAppId,
            PeerSubscriptionId = peerSubscriptionId,
            Recipients = [collabChatIdentity.OdinId, member2.OdinId],
            UnEncryptedMessage = "unencrypted message from unit test"
            // TypeId = default,
            // TagId = default,
            // Silent = false,
            // UnEncryptedMessage = null
        };

        var keyHeader = KeyHeader.NewRandom16();
        var (response, uploadedFileMetadata, _) = await AwaitPostNewEncryptedFileOverPeerDirect(
            member1,
            collabChatDrive,
            collabChatIdentity,
            chatCircleId,
            notificationOptions,
            keyHeader);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        var remoteTargetFile = response.Content.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            // wait for push notifications to be distributed
            await collabChatIdentity.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, debugTimeout);
            await collabChatIdentity.DriveRedux.WaitForEmptyOutbox(collabChatDrive, debugTimeout);

            //
            // Assert collab channel has the file
            //
            var byGlobalTransitIdResponse =
                await collabChatIdentity.DriveRedux.QueryByGlobalTransitId(remoteTargetFile.ToGlobalTransitIdFileIdentifier());
            ClassicAssert.IsTrue(byGlobalTransitIdResponse.IsSuccessStatusCode);
            var theFile = byGlobalTransitIdResponse.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(theFile);
            ClassicAssert.IsTrue(theFile.FileMetadata.AppData.FileType == uploadedFileMetadata.AppData.FileType);

            //
            // Assert: all notification recipients received a notification in their list
            //
            foreach (var recipient in notificationOptions.Recipients)
            {
                var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.InitializedIdentities[recipient]);
                var getNotificationResponse = await client.AppNotifications.GetList(1000);
                ClassicAssert.IsTrue(getNotificationResponse.IsSuccessStatusCode);

                //TODO: determine who the sender should actually be?
                var notificationsFromCollabChat = getNotificationResponse.Content.Results
                    .Where(notification => notification.SenderId == member1.OdinId);

                ClassicAssert.IsTrue(notificationsFromCollabChat.Any());
                //TODO: where do we check this? in the notifications or the log?
            }
        }

        await callerContext.Cleanup();
        await CleanupScenario(collabChatIdentity, member1, member2);
    }

    private static async Task<(ApiResponse<TransitResult> response, UploadFileMetadata uploadedMetadata, TestPayloadDefinition payload1)>
        AwaitPostNewEncryptedFileOverPeerDirect(OwnerApiClientRedux sender,
            TargetDrive collabChannelDrive,
            OwnerApiClientRedux collabChannel,
            Guid chatCircleId,
            AppNotificationOptions notificationOptions, KeyHeader keyHeader)
    {
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "some content here";
        uploadedFileMetadata.AllowDistribution = true;
        uploadedFileMetadata.AppData.DataType = 7779;
        uploadedFileMetadata.AccessControlList = new AccessControlList
        {
            RequiredSecurityGroup = SecurityGroupType.Connected,
            CircleIdList = [chatCircleId]
        };
        var payload1 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        payload1.Iv = ByteArrayUtil.GetRndByteArray(16);
        var payload2 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2();
        payload2.Iv = ByteArrayUtil.GetRndByteArray(16);

        var testPayloads = new List<TestPayloadDefinition>()
        {
            payload1,
            payload2
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };


        ApiResponse<TransitResult> response = null;

        //Pippin sends a file to the recipient
        (response, _) = await sender.PeerDirect.TransferNewEncryptedFile(collabChannelDrive,
            uploadedFileMetadata, [collabChannel.OdinId], null, uploadManifest,
            testPayloads, notificationOptions, keyHeader: keyHeader);

        await sender.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

        return (response, uploadedFileMetadata, payload1);
    }

    private static async Task<(ApiResponse<UploadResult> response, UploadFileMetadata uploadedMetadata, TestPayloadDefinition payload1)>
        AwaitNewEncryptedFileUpload(
            OwnerApiClientRedux sender,
            TargetDrive collabChannelDrive,
            Guid chatCircleId,
            AppNotificationOptions notificationOptions)
    {
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "some content here";
        uploadedFileMetadata.AllowDistribution = true;
        uploadedFileMetadata.AppData.DataType = 7779;
        uploadedFileMetadata.AccessControlList = new AccessControlList
        {
            RequiredSecurityGroup = SecurityGroupType.Connected,
            CircleIdList = [chatCircleId]
        };
        var payload1 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        payload1.Iv = ByteArrayUtil.GetRndByteArray(16);
        var payload2 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2();
        payload2.Iv = ByteArrayUtil.GetRndByteArray(16);

        var testPayloads = new List<TestPayloadDefinition>()
        {
            payload1,
            payload2
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        ApiResponse<UploadResult> response = null;

        var originalKeyHeader = KeyHeader.NewRandom16();
        (response, _, _, _) = await sender.DriveRedux.UploadNewEncryptedFile(
            collabChannelDrive,
            originalKeyHeader,
            uploadedFileMetadata,
            uploadManifest,
            testPayloads,
            notificationOptions);

        await sender.DriveRedux.WaitForEmptyOutbox(collabChannelDrive);

        return (response, uploadedFileMetadata, payload1);
    }

    private async Task SetupScenario(OwnerApiClientRedux collabChat, OwnerApiClientRedux member1, OwnerApiClientRedux member2,
        TargetDrive collabChannelDrive, Guid collabChatCircleId, Guid collabChatAppId, Guid peerSubscriptionId)
    {
        await collabChat.Configuration.DisableAutoAcceptIntroductions(true);
        await member1.Configuration.DisableAutoAcceptIntroductions(true);
        await member2.Configuration.DisableAutoAcceptIntroductions(true);

        var createDriveResponse = await collabChat.DriveManager.CreateDrive(collabChannelDrive, "Test collab chat drive 001", "",
            allowAnonymousReads: false,
            allowSubscriptions: true, //required for distributing push notifications
            attributes: IsCollaborativeChannelAttributes);

        Assert.That(createDriveResponse.IsSuccessStatusCode, Is.True);

        var permissions = TestUtils.CreatePermissionGrantRequest(collabChannelDrive, DrivePermission.ReadWrite);
        await collabChat.Network.CreateCircle(collabChatCircleId, "circle with some access", permissions);

        await member1.Connections.SendConnectionRequest(collabChat.OdinId);
        await collabChat.Connections.AcceptConnectionRequest(member1.OdinId, [collabChatCircleId]);

        await member2.Connections.SendConnectionRequest(collabChat.OdinId);
        await collabChat.Connections.AcceptConnectionRequest(member2.OdinId, [collabChatCircleId]);

        var collabIdentity = collabChat.OdinId;
        await SubscribeToPushNotifications(member1, collabChatAppId, collabIdentity, peerSubscriptionId);
        await SubscribeToPushNotifications(member2, collabChatAppId, collabIdentity, peerSubscriptionId);
        await SubscribeToPushNotifications(collabChat, collabChatAppId, collabIdentity, peerSubscriptionId);
    }

    private async Task SubscribeToPushNotifications(OwnerApiClientRedux client, Guid appId, OdinId collabIdentity, Guid peerSubscriptionId)
    {
        var appPermissions = new PermissionSetGrantRequest
        {
            Drives = [],
            PermissionSet = new PermissionSet(PermissionKeys.UseTransitWrite, PermissionKeys.UseTransitRead,
                PermissionKeys.SendPushNotifications)
        };

        var member1AppToken = await client.AppManager.RegisterAppAndClient(appId, appPermissions);
        var member1AppClient = _scaffold.CreateAppApiClientRedux(client.OdinId, member1AppToken);
        await member1AppClient.PeerAppNotification.Subscribe(collabIdentity, peerSubscriptionId);
    }

    private async Task CleanupScenario(OwnerApiClientRedux collabChat, OwnerApiClientRedux member1, OwnerApiClientRedux member2)
    {
        await collabChat.Connections.DisconnectFrom(member1.OdinId);
        await collabChat.Connections.DisconnectFrom(member2.OdinId);
        await member1.Connections.DisconnectFrom(collabChat.OdinId);
        await member2.Connections.DisconnectFrom(collabChat.OdinId);
    }
}