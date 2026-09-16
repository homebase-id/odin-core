#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.AppNotifications;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.AppNotifications.Data;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Concepts;

/// <summary>
/// Port of <c>_Universal/Concepts/CollabChat/CollaborationChatPushNotificationTests</c>.
///
/// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints).
/// Does not test security but rather drive features.
/// </summary>
/// <remarks>
/// Checked port. Two latent defects are carried, not fixed (see #1767):
/// <list type="bullet">
/// <item><b>The owner row is dead.</b> <c>[TestCaseSource(nameof(OwnerAllowed))]</c> is commented out
/// on <i>both</i> tests in the original, so the owner caller has never been exercised here. The row
/// is carried commented out in <see cref="PushNotificationCases"/> so it stays findable.</item>
/// <item><b>The caller matrix is inert.</b> Neither test ever calls
/// <c>callerContext.Initialize(...)</c>, so no app and no YouAuth domain is ever registered and every
/// call is made as the owner — the upload in the first test by the collaboration-chat identity, the
/// peer send in the second by member1. <c>callerContext.Cleanup()</c> is a no-op for both live rows
/// (<c>AppPermissionsKeysOnly</c>'s is literally empty; <c>ConnectedIdentityLoggedInOnGuestApi</c>'s
/// early-returns on a null <c>_api</c> because Initialize never ran). The only thing the matrix does
/// is supply <c>expected</c>, whose non-OK value skips the second half of each test. The
/// <see cref="CollabCallerSpec"/> parameter is therefore deliberately left unused here — building it
/// would grant access the original never granted. Contrast
/// <see cref="CollaborationChannelTests"/>, where the same contexts <i>are</i> initialized and do
/// drive the call.</item>
/// </list>
/// Porting notes:
/// <list type="bullet">
/// <item>Push notifications travel on the peer outbox (<c>SendPushNotificationOutboxWorker</c>), so
/// the two <c>WaitForEmptyOutbox</c> polls (transient-temp drive, then the chat drive) collapse into
/// one tenant-wide <c>Sync.DrainOutboxAsync()</c>. Nothing here is background-timer-bound.</item>
/// <item>The peer send in the second test additionally needs
/// <c>Sync.ProcessInboxAsync(collabChatDrive)</c> on the collaboration-chat identity: V1 leaned on the
/// inbox background service, which the fast host registers but never starts.</item>
/// <item><c>TestIdentities.InitializedIdentities</c> is null here, so recipients are mapped to their
/// owner sessions through a local dictionary instead.</item>
/// <item>Trailing <c>CleanupScenario</c> disconnects were cleanup only and are dropped — per-test
/// reset covers them.</item>
/// </list>
/// </remarks>
[TestFixture]
public class CollaborationChatPushNotificationTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Collab, Identities.Merry, Identities.Pippin];

    public static IEnumerable<object[]> PushNotificationCases()
    {
        // Carried verbatim from the original, where it is commented out on both tests — the owner
        // caller is untested here (see <remarks>):
        //   yield return [CollabCallerSpec.Owner(), HttpStatusCode.OK];
        yield return [CollabCallerSpec.AppWithOnlyUseTransitWrite(), HttpStatusCode.OK];
        yield return [CollabCallerSpec.ConnectedIdentityLoggedInOnGuestApi(), HttpStatusCode.MethodNotAllowed];
    }

    [Test, TestCaseSource(nameof(PushNotificationCases))]
    [Description("the collab channel identity uploads a chat message to itself.  A push notification should " +
                 "still be scheduled (pending options from api)")]
    public async Task CanAddPushNotificationWhenUploadingChatMessageDirectly(
        CollabCallerSpec callerContext, HttpStatusCode expected)
    {
        // callerContext is intentionally unused: the original never initialized it. See <remarks>.
        _ = callerContext;

        var collabChatIdentity = await LoginAsOwner(Identities.Collab);
        var member1 = await LoginAsOwner(Identities.Merry);
        var member2 = await LoginAsOwner(Identities.Pippin);

        var collabChatDrive = TargetDrive.NewTargetDrive();
        var collabChatAppId = Guid.NewGuid();
        var peerSubscriptionId = Guid.NewGuid();

        var chatCircleId = await SetupScenario(collabChatIdentity, member1, member2, collabChatDrive,
            collabChatAppId, peerSubscriptionId);

        var notificationOptions = new AppNotificationOptions
        {
            AppId = collabChatAppId,
            PeerSubscriptionId = peerSubscriptionId,
            Recipients = [member2.Identity, member1.Identity]

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
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Let's test more
        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        // push notifications ride the outbox, so one drain covers both the transient-temp drive and
        // the chat drive the V1 original polled separately
        await collabChatIdentity.Sync.DrainOutboxAsync();

        //
        // Assert: all notification recipients received a notification in their list
        //
        var sessions = new Dictionary<OdinId, OwnerSession>
        {
            [member1.Identity] = member1,
            [member2.Identity] = member2,
            [collabChatIdentity.Identity] = collabChatIdentity
        };

        foreach (var recipient in notificationOptions.Recipients)
        {
            var getNotificationResponse = await sessions[recipient].V1.Notifications.GetList(1000);
            Assert.That(getNotificationResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            //TODO: determine who the sender should actually be?
            Assert.That(getNotificationResponse.Content!.Results,
                Has.Some.Matches<AppNotification>(n => n.SenderId == collabChatIdentity.Identity.DomainName),
                $"{recipient} has no notification from the collab chat identity");
            //TODO: where do we check this? in the notifications or the log?
        }
    }

    [Test, TestCaseSource(nameof(PushNotificationCases))]
    public async Task CanAddPushNotificationWhenSendingChatMessageOverPeer(
        CollabCallerSpec callerContext, HttpStatusCode expected)
    {
        // callerContext is intentionally unused: the original never initialized it. See <remarks>.
        _ = callerContext;

        var collabChatIdentity = await LoginAsOwner(Identities.Collab);
        var member1 = await LoginAsOwner(Identities.Merry);
        var member2 = await LoginAsOwner(Identities.Pippin);

        var collabChatDrive = TargetDrive.NewTargetDrive();
        var collabChatAppId = Guid.NewGuid();
        var peerSubscriptionId = Guid.NewGuid();

        var chatCircleId = await SetupScenario(collabChatIdentity, member1, member2, collabChatDrive,
            collabChatAppId, peerSubscriptionId);

        var notificationOptions = new AppNotificationOptions
        {
            AppId = collabChatAppId,
            PeerSubscriptionId = peerSubscriptionId,
            Recipients = [collabChatIdentity.Identity, member2.Identity],
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
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var remoteTargetFile = response.Content!.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();

        // Let's test more
        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        // The collab chat identity has to take the file off its inbox before it can redistribute the
        // push notifications; V1 leaned on the inbox background service for this.
        await collabChatIdentity.Sync.ProcessInboxAsync(collabChatDrive);
        await collabChatIdentity.Sync.DrainOutboxAsync();

        //
        // Assert collab channel has the file
        //
        var byGlobalTransitIdResponse =
            await collabChatIdentity.V1.Drive.QueryByGlobalTransitId(remoteTargetFile.ToGlobalTransitIdFileIdentifier());
        Assert.That(byGlobalTransitIdResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var theFile = byGlobalTransitIdResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(theFile, Is.Not.Null);
        Assert.That(theFile!.FileMetadata.AppData.FileType, Is.EqualTo(uploadedFileMetadata.AppData.FileType));

        //
        // Assert: all notification recipients received a notification in their list
        //
        var sessions = new Dictionary<OdinId, OwnerSession>
        {
            [member1.Identity] = member1,
            [member2.Identity] = member2,
            [collabChatIdentity.Identity] = collabChatIdentity
        };

        foreach (var recipient in notificationOptions.Recipients)
        {
            var getNotificationResponse = await sessions[recipient].V1.Notifications.GetList(1000);
            Assert.That(getNotificationResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            //TODO: determine who the sender should actually be?
            Assert.That(getNotificationResponse.Content!.Results,
                Has.Some.Matches<AppNotification>(n => n.SenderId == member1.Identity.DomainName),
                $"{recipient} has no notification from member1");
            //TODO: where do we check this? in the notifications or the log?
        }
    }

    private static async Task<(ApiResponse<TransitResult> response, UploadFileMetadata uploadedMetadata, TestPayloadDefinition payload1)>
        AwaitPostNewEncryptedFileOverPeerDirect(
            OwnerSession sender,
            TargetDrive collabChannelDrive,
            OwnerSession collabChannel,
            Guid chatCircleId,
            AppNotificationOptions notificationOptions,
            KeyHeader keyHeader)
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

        //member1 sends a file to the recipient
        var (response, _) = await sender.V1.PeerDirect.TransferNewEncryptedFile(collabChannelDrive,
            uploadedFileMetadata, [collabChannel.Identity], null, uploadManifest,
            testPayloads, notificationOptions, keyHeader: keyHeader);

        await sender.Sync.DrainOutboxAsync();

        return (response, uploadedFileMetadata, payload1);
    }

    private static async Task<(ApiResponse<UploadResult> response, UploadFileMetadata uploadedMetadata, TestPayloadDefinition payload1)>
        AwaitNewEncryptedFileUpload(
            OwnerSession sender,
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

        var originalKeyHeader = KeyHeader.NewRandom16();
        var (response, _, _, _) = await sender.V1.Drive.UploadNewEncryptedFile(
            collabChannelDrive,
            originalKeyHeader,
            uploadedFileMetadata,
            uploadManifest,
            testPayloads,
            notificationOptions);

        await sender.Sync.DrainOutboxAsync();

        return (response, uploadedFileMetadata, payload1);
    }

    private static async Task<Guid> SetupScenario(
        OwnerSession collabChat,
        OwnerSession member1,
        OwnerSession member2,
        TargetDrive collabChannelDrive,
        Guid collabChatAppId,
        Guid peerSubscriptionId)
    {
        var collabChatCircleId = await CollabScenario.PrepareScenarioAsync(
            collabChat,
            [member1, member2],
            collabChannelDrive,
            DrivePermission.ReadWrite,
            "Test collab chat drive 001",
            allowAnonymousReads: false);

        var collabIdentity = collabChat.Identity;
        await SubscribeToPushNotifications(member1, collabChatAppId, collabIdentity, peerSubscriptionId);
        await SubscribeToPushNotifications(member2, collabChatAppId, collabIdentity, peerSubscriptionId);
        await SubscribeToPushNotifications(collabChat, collabChatAppId, collabIdentity, peerSubscriptionId);

        return collabChatCircleId;
    }

    private static async Task SubscribeToPushNotifications(
        OwnerSession owner, Guid appId, OdinId collabIdentity, Guid peerSubscriptionId)
    {
        var appPermissions = new PermissionSetGrantRequest
        {
            Drives = [],
            PermissionSet = new PermissionSet(PermissionKeys.UseTransitWrite, PermissionKeys.UseTransitRead,
                PermissionKeys.SendPushNotifications)
        };

        var app = await AppSession.SetupAsync(owner, appPermissions, knownAppId: appId);
        var peerNotifications = new UniversalPeerAppNotificationApiClient(app.Identity, app.Factory);
        var subscribe = await peerNotifications.Subscribe(collabIdentity, peerSubscriptionId);
        // 204, not 200 — the peer subscribe endpoint is one of the NoContent responders the README
        // lists. The original asserted nothing here at all; this is arrange validation.
        Assert.That(subscribe.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }
}
