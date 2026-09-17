#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.AppNotifications;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Ported.Transit;
using Odin.Services.AppNotifications.Data;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;

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
/// <item>The original's <c>SetupScenario</c> — drive, circle, two handshakes, three app
/// registrations, three push subscriptions — ran per test case, four times over. All of it is
/// identity-DB state that the baseline snapshot carries, so it is baked in once by
/// <see cref="WarmTenantBaselineAsync"/> under fixed ids. The uploads stay per-test: the payload tree
/// is wiped on reset, so a file baked into the baseline would have a header and no payloads.</item>
/// <item>Trailing <c>CleanupScenario</c> disconnects were cleanup only and are dropped — per-test
/// reset covers them.</item>
/// </list>
/// </remarks>
[TestFixture]
public class CollaborationChatPushNotificationTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Collab, Identities.Merry, Identities.Pippin];

    // Fixed rather than minted per test: the whole scenario below — drive, circle, two handshakes,
    // three app registrations, three push subscriptions — is baseline state, so it is baked in once
    // (see WarmTenantBaselineAsync) instead of being rebuilt for each of the four executions. Only
    // the file uploads stay per-test: the payload tree is wiped on reset.
    private static readonly TargetDrive CollabChatDrive = new()
    {
        Alias = Guid.Parse("c0111ab0-c8a7-4000-8000-000000000001"),
        Type = Guid.Parse("c0111ab0-c8a7-4000-8000-000000000002")
    };

    private static readonly Guid CollabChatAppId = Guid.Parse("c0111ab0-c8a7-4000-8000-000000000003");
    private static readonly Guid PeerSubscriptionId = Guid.Parse("c0111ab0-c8a7-4000-8000-000000000004");
    private static readonly Guid ChatCircleId = Guid.Parse("c0111ab0-c8a7-4000-8000-000000000005");

    /// <summary>The ACL every post here carries: connected, and narrowed to the chat circle.</summary>
    private static AccessControlList ChatAcl => new()
    {
        RequiredSecurityGroup = SecurityGroupType.Connected,
        CircleIdList = [ChatCircleId]
    };

    protected override async Task WarmTenantBaselineAsync()
    {
        await base.WarmTenantBaselineAsync();

        var collabChat = await LoginAsOwner(Identities.Collab);
        var member1 = await LoginAsOwner(Identities.Merry);
        var member2 = await LoginAsOwner(Identities.Pippin);

        await CollabScenario.PrepareScenarioAsync(
            collabChat,
            [member1, member2],
            CollabChatDrive,
            DrivePermission.ReadWrite,
            "Test collab chat drive 001",
            allowAnonymousReads: false,
            circleId: ChatCircleId);

        foreach (var owner in new[] { member1, member2, collabChat })
        {
            await SubscribeToPushNotifications(owner, collabChat.Identity);
        }
    }

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

        var notificationOptions = new AppNotificationOptions
        {
            AppId = CollabChatAppId,
            PeerSubscriptionId = PeerSubscriptionId,
            Recipients = [member2.Identity, member1.Identity]

            // TypeId = default,
            // TagId = default,
            // Silent = false,
            // UnEncryptedMessage = null
        };

        var response = await CollabScenario.UploadNewEncryptedFileAsync(
            collabChatIdentity,
            CollabChatDrive,
            ChatAcl,
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

        await AssertNotificationFrom(collabChatIdentity, notificationOptions.Recipients,
            [member1, member2, collabChatIdentity]);
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

        var notificationOptions = new AppNotificationOptions
        {
            AppId = CollabChatAppId,
            PeerSubscriptionId = PeerSubscriptionId,
            Recipients = [collabChatIdentity.Identity, member2.Identity],
            UnEncryptedMessage = "unencrypted message from unit test"
            // TypeId = default,
            // TagId = default,
            // Silent = false,
        };

        var keyHeader = KeyHeader.NewRandom16();
        var (response, uploadedFileMetadata, _) = await CollabScenario.PostNewEncryptedFileOverPeerDirectAsync(
            member1,
            CollabChatDrive,
            collabChatIdentity,
            keyHeader,
            ChatAcl,
            notificationOptions);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var remoteTargetFile = response.Content!.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();

        // Let's test more
        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        // The collab chat identity has to take the file off its inbox before it can redistribute the
        // push notifications; V1 leaned on the inbox background service for this.
        await collabChatIdentity.Sync.ProcessInboxAsync(CollabChatDrive);
        await collabChatIdentity.Sync.DrainOutboxAsync();

        //
        // Assert collab channel has the file
        //
        var theFile = await TransitScenario.SingleByGlobalTransitIdAsync(
            collabChatIdentity, remoteTargetFile.ToGlobalTransitIdFileIdentifier());
        Assert.That(theFile.FileMetadata.AppData.FileType, Is.EqualTo(uploadedFileMetadata.AppData.FileType));

        await AssertNotificationFrom(member1, notificationOptions.Recipients,
            [member1, member2, collabChatIdentity]);
    }

    /// <summary>
    /// Asserts every one of <paramref name="recipients"/> holds a notification whose sender is
    /// <paramref name="sender"/>. <c>TestIdentities.InitializedIdentities</c> is null here, so the
    /// recipient <see cref="OdinId"/>s are mapped back to their sessions through
    /// <paramref name="sessions"/>.
    /// </summary>
    private static async Task AssertNotificationFrom(
        OwnerSession sender,
        IEnumerable<OdinId> recipients,
        IReadOnlyList<OwnerSession> sessions)
    {
        var byIdentity = sessions.ToDictionary(s => s.Identity);

        foreach (var recipient in recipients)
        {
            var getNotificationResponse = await byIdentity[recipient].V1.Notifications.GetList(1000);
            Assert.That(getNotificationResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            //TODO: determine who the sender should actually be?
            Assert.That(getNotificationResponse.Content!.Results,
                Has.Some.Matches<AppNotification>(n => n.SenderId == sender.Identity.DomainName),
                $"{recipient} has no notification from {sender.Identity}");
            //TODO: where do we check this? in the notifications or the log?
        }
    }

    /// <summary>
    /// Registers the push-capable app on <paramref name="owner"/> and subscribes it to
    /// <paramref name="collabIdentity"/>'s notifications, under the fixture's fixed app and
    /// subscription ids.
    /// </summary>
    private static async Task SubscribeToPushNotifications(OwnerSession owner, OdinId collabIdentity)
    {
        var appPermissions = new PermissionSetGrantRequest
        {
            Drives = [],
            PermissionSet = new PermissionSet(PermissionKeys.UseTransitWrite, PermissionKeys.UseTransitRead,
                PermissionKeys.SendPushNotifications)
        };

        var app = await AppSession.SetupAsync(owner, appPermissions, knownAppId: CollabChatAppId);
        var peerNotifications = new UniversalPeerAppNotificationApiClient(app.Identity, app.Factory);
        var subscribe = await peerNotifications.Subscribe(collabIdentity, PeerSubscriptionId);
        // 204, not 200 — the peer subscribe endpoint is one of the NoContent responders the README
        // lists. The original asserted nothing here at all; this is arrange validation.
        Assert.That(subscribe.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }
}
