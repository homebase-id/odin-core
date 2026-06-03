using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Serialization;
using Odin.Hosting.Tests._Universal.ApiClient.App;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.Peer.PeerAppNotificationsWebSocket;
using Odin.Hosting.UnifiedV2;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.AppNotification;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._V2.Tests.Peer;

/// <summary>
/// Cap 4b of the chat-kmp V2 peer transport: a member receives live <c>fileAdded</c> notifications on
/// a drive hosted by another identity over the V2 peer websocket
/// (<c>/api/v2/peer/notify/ws-token</c>).
///
/// FLAGGED: this lives in the OLD WebScaffold framework (not the fast <c>Odin.Hosting.Tests.V2</c>),
/// because WebSocket flows are an explicit non-goal of the fast framework — they need a real Kestrel
/// host. The handshake/in-band-auth and assertions are ported from the battle-tested V1
/// <c>_Universal/Peer/PeerAppNotificationsWebSocket/PeerAppNotificationTests</c>; only the websocket
/// URL differs (V2 route instead of <c>/api/apps/v1/notify/peer/ws</c>).
/// </summary>
public class V2PeerAppNotificationWebSocketTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity> { TestIdentities.Frodo, TestIdentities.Samwise });
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

    [Test]
    public async Task MemberReceivesFileAddedOverV2PeerWebSocket()
    {
        var targetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        var host = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var member = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var memberAppApi = await PrepareScenario(host, member, targetDrive);

        // Member's app requests a token to listen to the host's peer notifications.
        var tokenResponse = await memberAppApi.PeerAppNotification.GetRemoteNotificationToken(
            new GetRemoteTokenRequest { Identity = host.Identity.OdinId });
        ClassicAssert.IsTrue(tokenResponse.IsSuccessStatusCode, "failed to get remote notification token");
        var token = tokenResponse.Content!.ToCat();

        var listener = new PeerTestAppWebSocketListener();
        var receivedGtid = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.NotificationReceived += n =>
        {
            if (n.NotificationType == ClientNotificationType.FileAdded)
            {
                var driveNotification = OdinSystemSerializer.Deserialize<ClientDriveNotification>(n.Data);
                receivedGtid.TrySetResult(driveNotification.Header.FileMetadata.GlobalTransitId.GetValueOrDefault());
            }

            return Task.CompletedTask;
        };

        // Connect to the V2 peer websocket and subscribe to the community drive.
        await listener.ConnectAsync(host.Identity.OdinId, token,
            new EstablishConnectionOptions { Drives = [targetDrive] },
            UnifiedApiRouteConstants.PeerNotifySocket);

        // Member posts a message to the host's drive over peer; the host's dispatcher should push
        // a fileAdded event back over the websocket.
        var sent = await SendChatMessage("hi over v2 peer ws", member, host, targetDrive);
        var sentGtid = sent.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId;

        var winner = await Task.WhenAny(receivedGtid.Task, Task.Delay(TimeSpan.FromSeconds(30)));

        await listener.DisconnectAsync();
        await Shutdown(host, member);

        ClassicAssert.IsTrue(winner == receivedGtid.Task, "did not receive a fileAdded notification over the V2 peer websocket");
        ClassicAssert.AreEqual(sentGtid, receivedGtid.Task.Result, "received notification was for a different file");
    }

    private async Task<AppApiClientRedux> PrepareScenario(OwnerApiClientRedux host, OwnerApiClientRedux member,
        TargetDrive groupChannelDrive)
    {
        Dictionary<string, string> isCollaborativeChannel =
            new() { { BuiltInDriveAttributes.IsCollaborativeChannel, bool.TrueString } };

        await host.DriveManager.CreateDrive(groupChannelDrive, "A Group Channel Drive", "",
            allowAnonymousReads: false, ownerOnly: false, allowSubscriptions: false, attributes: isCollaborativeChannel);

        var memberCircleId = Guid.NewGuid();
        await host.Network.CreateCircle(memberCircleId, "group members", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new() { Drive = groupChannelDrive, Permission = DrivePermission.ReadWrite }
                }
            },
            PermissionSet = default
        });

        await host.Connections.SendConnectionRequest(member.Identity.OdinId, [memberCircleId]);
        await member.Connections.AcceptConnectionRequest(host.Identity.OdinId);

        var communityAppId = Guid.NewGuid();
        var permissions = new PermissionSetGrantRequest
        {
            Drives = [],
            PermissionSet = new PermissionSet(PermissionKeys.UseTransitWrite, PermissionKeys.UseTransitRead)
        };

        var memberAppClientAccessToken = await member.AppManager.RegisterAppAndClient(communityAppId, permissions);
        return _scaffold.CreateAppApiClientRedux(member.Identity.OdinId, memberAppClientAccessToken);
    }

    private static async Task<TransitResult> SendChatMessage(string message, OwnerApiClientRedux sender,
        OwnerApiClientRedux recipient, TargetDrive targetDrive)
    {
        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = true,
            AppData = new() { Content = message },
            AccessControlList = AccessControlList.Connected
        };

        var response = await sender.PeerDirect.TransferMetadata(targetDrive, fileMetadata,
            [recipient.Identity.OdinId], null);
        return response.Content;
    }

    private async Task Shutdown(OwnerApiClientRedux host, OwnerApiClientRedux member)
    {
        await _scaffold.OldOwnerApi.DisconnectIdentities(host.Identity.OdinId, member.Identity.OdinId);
    }
}
