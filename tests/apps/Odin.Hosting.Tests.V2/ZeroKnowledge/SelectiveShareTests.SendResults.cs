#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;
using Odin.Test.Helpers;
using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// How a member's send to the owner's folder went, told on the sockets of the app that sent it
/// (<c>PeerSendResultNotification</c>): their server keeps no copy of a file it sends from the transient drive, so no
/// transfer history can tell them.
/// </summary>
/// <remarks>
/// Sam's sockets are the server's own, registered as a handshake registers them, as in <see cref="ShareSockets"/>: the
/// sending app's, another app's, and his own client's (no app).  The event goes the whole way: the outbox, the mediator,
/// the pub/sub channel, the dispatcher and each socket's message.
/// </remarks>
public partial class SelectiveShareTests
{
    [Test]
    public async Task AMembersAppHearsHowEachOfItsSendsToTheOwnersFolderWent()
    {
        var s = await ArrangeAsync();
        var samsApp = await SamsSendingAppAsync(s);
        await using var sockets = await SenderSockets.OpenAsync(s, samsApp.AppId);
        var circleKey = CircleKey();
        var fileKey = KeyHeader.NewRandom16();
        var gtid = Guid.NewGuid();
        var uniqueId = Guid.NewGuid();

        // A new file: delivered, with the version tag of Frodo's copy for the next edit to name
        await SendDepositAsync(samsApp, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "Sam's file", remoteDrive: s.Drive,
            shares: [s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.Write, lockedFileKey: LockFileKey(s, circleKey, fileKey, gtid))],
            fileKey: fileKey, globalTransitId: gtid);
        var added = await s.HeaderByUniqueIdAsync(uniqueId);
        await sockets.App.WaitForAsync("the new file", r => r.Matches(s, gtid, isUpdate: false) &&
                                                           r.Status == LatestTransferStatus.Delivered && !r.WillRetry &&
                                                           r.VersionTag == added.FileMetadata.VersionTag);

        // Keeping the key of a file no owner device has opened yet: refused for its key header, for good
        await MemberEditsAsync(s, gtid, added.FileMetadata.VersionTag, fileKey, "kept", [], [], KeepFileKey, uniqueId, samsApp);
        await sockets.App.WaitForAsync("keeping the key of a sealed file", r => r.Matches(s, gtid, isUpdate: true) &&
                                                                              r.Status == LatestTransferStatus.RecipientIdentityReturnedInvalidKeyHeader &&
                                                                              !r.WillRetry && r.VersionTag == null);

        // So the app seals the same key again: delivered, with the version tag of the edit
        await MemberEditsAsync(s, gtid, added.FileMetadata.VersionTag, fileKey, "sealed again", [], [], SealAgain(s, fileKey), uniqueId,
            samsApp);
        var edited = await s.HeaderByUniqueIdAsync(uniqueId);
        Assert.That(edited.FileMetadata.VersionTag, Is.Not.EqualTo(added.FileMetadata.VersionTag), "the edit is a new version");
        await sockets.App.WaitForAsync("sealed again", r => r.Matches(s, gtid, isUpdate: true) &&
                                                          r.Status == LatestTransferStatus.Delivered && !r.WillRetry &&
                                                          r.VersionTag == edited.FileMetadata.VersionTag);

        // Frodo's own file, shared with Sam's circle: an edit keeping its key is delivered, and one naming the version his
        // copy has moved on from is refused, for good (Sam may change a file he sent without naming its version while no
        // one else has, so his own file can't show it)
        var frodosKey = KeyHeader.NewRandom16();
        var frodosGtid = Guid.NewGuid();
        var (frodosId, _, _) = await s.UploadClientLockedAsync(fileKey: frodosKey, globalTransitId: frodosGtid,
            shares: [s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.Write, lockedFileKey: LockFileKey(s, circleKey, frodosKey, frodosGtid))]);
        var frodosVersion = (await s.HeaderAsync(frodosId)).FileMetadata.VersionTag;
        await MemberEditsAsync(s, frodosGtid, frodosVersion, frodosKey, "edited by Sam", [], [], KeepFileKey, editor: samsApp);
        var frodosEdited = await s.HeaderAsync(frodosId);
        await sockets.App.WaitForAsync("an edit of Frodo's file", r => r.Matches(s, frodosGtid, isUpdate: true) &&
                                                                     r.Status == LatestTransferStatus.Delivered &&
                                                                     r.VersionTag == frodosEdited.FileMetadata.VersionTag);

        await MemberEditsAsync(s, frodosGtid, frodosVersion, frodosKey, "edited again", [], [], KeepFileKey, editor: samsApp);
        await sockets.App.WaitForAsync("an edit of an old version", r => r.Matches(s, frodosGtid, isUpdate: true) &&
                                                                       r.Status == LatestTransferStatus.RecipientIdentityReturnedVersionTagMismatch &&
                                                                       !r.WillRetry && r.VersionTag == null);

        var frodosEdits = sockets.App.Results.Where(r => r.GlobalTransitId == frodosGtid).Select(r => r.SentVersionTag).ToList();
        Assert.That(frodosEdits, Has.None.EqualTo(Guid.Empty).And.Unique, "each result names the version tag its edit gave back");
        await sockets.AssertOnlyHeardOnAsync(sockets.App);
    }

    [Test]
    public async Task AMembersAppHearsASendToTheOwnersFolderIsTriedAgain()
    {
        var s = await ArrangeAsync();
        var samsApp = await SamsSendingAppAsync(s);
        await using var sockets = await SenderSockets.OpenAsync(s, samsApp.AppId);
        var gtid = Guid.NewGuid();
        var uniqueId = Guid.NewGuid();

        s.Frodo.Host.PeerFaults.FailNext(from: Identities.Sam, to: Identities.Frodo, "/upload");
        await SendDepositAsync(samsApp, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "Sam's file", remoteDrive: s.Drive,
            globalTransitId: gtid);

        // Frodo's server answered 500 once, and the outbox tried again
        await sockets.App.WaitForAsync("tried again", r => r.Matches(s, gtid, isUpdate: false) &&
                                                         r.Status == LatestTransferStatus.RecipientIdentityReturnedServerError &&
                                                         r.WillRetry && r.VersionTag == null);
        var added = await s.HeaderByUniqueIdAsync(uniqueId);
        await sockets.App.WaitForAsync("then delivered", r => r.Matches(s, gtid, isUpdate: false) &&
                                                            r.Status == LatestTransferStatus.Delivered &&
                                                            r.VersionTag == added.FileMetadata.VersionTag);
        Assert.That(sockets.App.Results.Select(r => r.Status), Is.EqualTo(new[]
        {
            LatestTransferStatus.RecipientIdentityReturnedServerError, LatestTransferStatus.Delivered
        }), "in that order");
        await sockets.AssertOnlyHeardOnAsync(sockets.App);
    }

    [Test]
    public async Task ASendFromTheMembersOwnClientIsHeardOnlyOnItsSockets()
    {
        var s = await ArrangeAsync();
        var samsApp = await SamsSendingAppAsync(s);
        await using var sockets = await SenderSockets.OpenAsync(s, samsApp.AppId);
        var gtid = Guid.NewGuid();
        var uniqueId = Guid.NewGuid();

        await SendDepositAsync(s.Sam, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "Sam's file", remoteDrive: s.Drive,
            globalTransitId: gtid);

        var added = await s.HeaderByUniqueIdAsync(uniqueId);
        await sockets.Owner.WaitForAsync("his own client", r => r.Matches(s, gtid, isUpdate: false) &&
                                                              r.Status == LatestTransferStatus.Delivered &&
                                                              r.VersionTag == added.FileMetadata.VersionTag);
        await sockets.AssertOnlyHeardOnAsync(sockets.Owner);
    }

    /// <summary>An app of Sam's that sends files to peers, which grants it his transient drive</summary>
    private static Task<AppSession> SamsSendingAppAsync(Arranged s) =>
        AppSession.SetupAsync(s.Sam, SystemDriveConstants.TransientTempDrive, DrivePermission.ReadWrite,
            [PermissionKeys.UseTransitRead, PermissionKeys.UseTransitWrite]);

    /// <summary>
    /// Sam's sockets: the sending app's, another app's, his own client's, and a guest's of no app (a connected identity's,
    /// as the owner's own client is of no app too)
    /// </summary>
    private sealed class SenderSockets : IAsyncDisposable
    {
        private readonly SharedDeviceSocketCollection<AppNotificationHandler> _sockets;
        private readonly AppNotificationDispatcher _dispatcher;

        private SenderSockets(ILifetimeScope scope, Arranged s, Guid appId)
        {
            _sockets = scope.Resolve<SharedDeviceSocketCollection<AppNotificationHandler>>();
            _dispatcher = scope.Resolve<AppNotificationDispatcher>();
            var owner = Context(s.Drive, Identities.Sam, SecurityGroupType.Owner, null, DrivePermission.All);
            App = new ResultSocket(owner, appId);
            OtherApp = new ResultSocket(owner, Guid.NewGuid());
            Owner = new ResultSocket(owner, null);
            Guest = new ResultSocket(Context(s.Drive, Identities.Frodo, SecurityGroupType.Connected, null, DrivePermission.Read), null);
        }

        public ResultSocket App { get; }
        public ResultSocket OtherApp { get; }
        public ResultSocket Owner { get; }
        public ResultSocket Guest { get; }

        private IEnumerable<ResultSocket> All => [App, OtherApp, Owner, Guest];

        public static async Task<SenderSockets> OpenAsync(Arranged s, Guid appId)
        {
            var sockets = new SenderSockets(s.Sam.Host.GetTenantScope(s.Sam.Identity.DomainName), s, appId);
            await sockets._dispatcher.SubscribeAsync();
            foreach (var socket in sockets.All)
            {
                sockets._sockets.AddSocket(socket.Device);
            }

            return sockets;
        }

        /// <summary>
        /// Every result was heard on <paramref name="heardOn"/> alone, once the dispatcher, which sends to one socket at a
        /// time, has had time to reach the others too
        /// </summary>
        public async Task AssertOnlyHeardOnAsync(ResultSocket heardOn)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            Assert.That(heardOn.Results, Is.Not.Empty, "the socket that should hear them");
            foreach (var other in All.Where(socket => socket != heardOn))
            {
                Assert.That(other.Results, Is.Empty, $"{other.Name} hears none of them");
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                foreach (var socket in All)
                {
                    await _sockets.RemoveSocket(socket.Device.Key);
                }
            }
            finally
            {
                await _dispatcher.UnsubscribeAsync();
            }
        }
    }

    /// <summary>A socket of Sam's, an app's or (no app) his own client's, that keeps the send results it is sent</summary>
    private sealed class ResultSocket
    {
        private readonly KeepingWebSocket _socket = new();
        private readonly OdinContext _context;

        public ResultSocket(OdinContext context, Guid? appId)
        {
            _context = context;
            Name = appId is { } id ? $"app {id}'s socket" : $"{context.Caller.SecurityLevel} {context.Caller.OdinId}'s socket of no app";
            Device = new DeviceSocket { Key = Guid.NewGuid(), Socket = _socket, DeviceOdinContext = _context, AppId = appId };
        }

        public DeviceSocket Device { get; }

        public string Name { get; }

        public List<SendResult> Results => _socket.Sent.Select(Read).OfType<SendResult>().ToList();

        public async Task WaitForAsync(string what, Func<SendResult, bool> match)
        {
            await Poll.UntilAsync(() => Task.FromResult(Results.Any(match)), TimeSpan.FromSeconds(10),
                () => $"{what}: no send result matched; seen: " +
                      string.Join(", ", Results.Select(r => $"{r.Status} update:{r.IsUpdate} retry:{r.WillRetry} to:{r.Recipient} drive:{r.DriveId} " +
                                                         $"gtid:{r.GlobalTransitId} version:{r.VersionTag}")));
        }

        private SendResult? Read(string sent)
        {
            var envelope = ReadEnvelope(sent, _context);
            return envelope.NotificationType == ClientNotificationType.PeerSendResult
                ? OdinSystemSerializer.Deserialize<SendResult>(envelope.Data)
                : null;
        }
    }

    /// <summary>A send result as a client reads it</summary>
    private sealed class SendResult
    {
        public string Recipient { get; set; } = "";
        public Guid DriveId { get; set; }
        public Guid GlobalTransitId { get; set; }
        public Guid SentVersionTag { get; set; }
        public bool IsUpdate { get; set; }
        public LatestTransferStatus Status { get; set; }
        public bool WillRetry { get; set; }
        public Guid? VersionTag { get; set; }

        /// <summary>Of Sam's send to Frodo's folder, of the file with <paramref name="gtid"/></summary>
        public bool Matches(Arranged s, Guid gtid, bool isUpdate) =>
            Recipient == s.Frodo.Identity.DomainName && DriveId == s.Drive.Alias.Value && GlobalTransitId == gtid && IsUpdate == isUpdate;
    }
}
