#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Transit;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Shares;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Reactions;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Test.Helpers;
using Refit;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// What a share, a member's write, reaction or comment makes everyone else see (docs/selective-share-pr3-plan.md,
/// sections 8 to 10): the file's reaction summary and <c>modified</c>, which a member's sync goes by, and the events on
/// Frodo's device sockets and on Sam's peer socket.
/// </summary>
/// <remarks>
/// The sockets are the server's own, registered as a handshake registers them, with a socket that keeps what it is
/// sent: an owner device holding the master key, and Sam's peer socket, connected and reviewed, in the app's circle,
/// with the circle's grant on the drive (I: the context the peer handshake builds from his connection).  The events
/// go the whole way: the request, the mediator, the pub/sub channel, the dispatchers and each socket's message.
/// </remarks>
public partial class SelectiveShareTests
{
    private const string Thumbsup = ":thumbsup:";

    [Test]
    public async Task AMembersReactionUpdatesTheFilesSummaryForEveryone()
    {
        var s = await ArrangeAsync();
        var fileId = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.React)]);
        var file = await s.GtidOfAsync(fileId);
        var before = await s.HeaderAsync(fileId);
        await using var sockets = await ShareSockets.OpenAsync(s);

        await ReactAsync(s, file, add: true);

        var after = await s.HeaderAsync(fileId);
        Assert.That(ReactionsOf(after), Is.EqualTo(new[] { (Thumbsup, 1) }), "Frodo's file holds the summary");
        Assert.That(after.FileMetadata.Updated.milliseconds, Is.GreaterThan(before.FileMetadata.Updated.milliseconds), "modified moved, so syncs see it");
        Assert.That(after.FileMetadata.VersionTag, Is.EqualTo(before.FileMetadata.VersionTag), "the file itself didn't change");
        Assert.That(ReactionsOf(await SamsHeaderAsync(s, fileId)), Is.EqualTo(new[] { (Thumbsup, 1) }), "Sam reads it by id");
        Assert.That(ReactionsOf(await SamsQueriedAsync(s, fileId)), Is.EqualTo(new[] { (Thumbsup, 1) }), "and in his sync");

        await sockets.Owner.WaitForAsync("Frodo's device: the summary", ClientNotificationType.FileModified,
            h => h.FileId == fileId && ReactionsOf(h).SequenceEqual([(Thumbsup, 1)]));
        await sockets.Member.WaitForAsync("Sam's peer socket: the summary", ClientNotificationType.FileModified,
            h => h.FileId == fileId && ReactionsOf(h).SequenceEqual([(Thumbsup, 1)]));

        await ReactAsync(s, file, add: false);

        Assert.That(ReactionsOf(await s.HeaderAsync(fileId)), Is.Empty, "his reaction removed");
        await sockets.Owner.WaitForAsync("Frodo's device: the removal", ClientNotificationType.FileModified,
            h => h.FileId == fileId && !ReactionsOf(h).Any());
        await sockets.Member.WaitForAsync("Sam's peer socket: the removal", ClientNotificationType.FileModified,
            h => h.FileId == fileId && !ReactionsOf(h).Any());
    }

    [Test]
    public async Task AMembersCommentReachesEveryoneAndCountsOnItsFile()
    {
        var s = await ArrangeAsync();
        var (fileId, _, fileKey) = await s.UploadClientLockedAsync();
        var lockedFileKey = LockedFileKey();
        var share = await s.Shares.WriteShares(s.Drive.Alias, new WriteSharesRequest
        {
            Shares = [s.Share(fileId, DrivePermission.Read | DrivePermission.Comment, lockedFileKey: lockedFileKey)]
        });
        Assert.That(share.StatusCode, Is.EqualTo(HttpStatusCode.OK), share.Error?.Content);
        var file = await s.GtidOfAsync(fileId);
        var before = await s.HeaderAsync(fileId);
        await using var sockets = await ShareSockets.OpenAsync(s);

        var commentIv = await SendCommentAsync(s, s.Sam, file, fileKey, "Sam's comment");
        var commentId = (await CommentsAsync(s.Frodo.Drives.Reader, s, file)).Single().FileId;

        // The file counts it, with Sam as its author and no text: the server can't read an encrypted comment
        var after = await s.HeaderAsync(fileId);
        var summary = after.FileMetadata.ReactionPreview;
        Assert.That((summary?.TotalCommentCount, summary?.Comments.Single().OdinId.ToString(), summary?.Comments.Single().IsEncrypted,
                summary?.Comments.Single().Content),
            Is.EqualTo(((int?)1, Identities.Sam.ToString(), (bool?)true, "")), "the file's comment summary");
        Assert.That(after.FileMetadata.Updated.milliseconds, Is.GreaterThan(before.FileMetadata.Updated.milliseconds), "modified moved");
        Assert.That((await SamsQueriedAsync(s, fileId)).FileMetadata.ReactionPreview?.TotalCommentCount, Is.EqualTo(1),
            "Sam's sync sees the count");

        // Frodo's device gets the comment as stored; Sam's peer socket gets it with his share's key, then both the count
        await sockets.Owner.WaitForAsync("Frodo's device: the comment", ClientNotificationType.FileAdded,
            h => h.FileId == commentId && h.FileSystemType == FileSystemType.Comment &&
                 h.SharedSecretEncryptedKeyHeader.Type == EncryptionType.FileKey);
        await sockets.Member.WaitForAsync("Sam's peer socket: the comment, with his share's key", ClientNotificationType.FileAdded,
            h => h.FileId == commentId && h.SharedSecretEncryptedKeyHeader.Type == EncryptionType.Share &&
                 h.SharedSecretEncryptedKeyHeader.EncryptedAesKey.SequenceEqual(lockedFileKey) &&
                 h.SharedSecretEncryptedKeyHeader.Iv.SequenceEqual(commentIv!));
        await sockets.Owner.WaitForAsync("Frodo's device: the count", ClientNotificationType.FileModified,
            h => h.FileId == fileId && h.FileMetadata.ReactionPreview?.TotalCommentCount == 1);
        await sockets.Member.WaitForAsync("Sam's peer socket: the count", ClientNotificationType.FileModified,
            h => h.FileId == fileId && h.FileMetadata.ReactionPreview?.TotalCommentCount == 1);

        // His delete takes it off the count, and both hear of it
        var commentGtid = (await CommentsAsync(s.Frodo.Drives.Reader, s, file)).Single().FileMetadata.GlobalTransitId!.Value;
        var delete = await CommentWriter(s.Sam).SendDeleteRequestOverPeer(s.Frodo.Identity, s.Drive, commentGtid);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK), delete.Error?.Content);
        await s.Sam.Sync.DrainOutboxAsync();

        Assert.That((await s.HeaderAsync(fileId)).FileMetadata.ReactionPreview?.TotalCommentCount, Is.EqualTo(0), "after his delete");
        await sockets.Owner.WaitForAsync("Frodo's device: the delete", ClientNotificationType.FileDeleted, h => h.FileId == commentId);
        await sockets.Member.WaitForAsync("Sam's peer socket: the delete", ClientNotificationType.FileDeleted, h => h.FileId == commentId);
        await sockets.Member.WaitForAsync("Sam's peer socket: the count", ClientNotificationType.FileModified,
            h => h.FileId == fileId && h.FileMetadata.ReactionPreview?.TotalCommentCount == 0);
    }

    [Test]
    public async Task ASharesWritesReachTheOwnersDevicesAndItsMember()
    {
        var s = await ArrangeAsync();
        var shared = await s.UploadAsync(SecurityGroupType.Owner);
        var notShared = await s.UploadAsync(SecurityGroupType.Connected);
        await using var sockets = await ShareSockets.OpenAsync(s);

        // Frodo changes a file no share names Sam on: his devices hear it, Sam doesn't
        var react = await s.Frodo.Drives.Reactions.AddReactionAsync(s.Drive.Alias, notShared, Thumbsup);
        Assert.That(react.StatusCode, Is.EqualTo(HttpStatusCode.OK), react.Error?.Content);
        await sockets.Owner.WaitForAsync("Frodo's device: the unshared file", ClientNotificationType.FileModified, h => h.FileId == notShared);

        await s.ShareAsync(shared, DrivePermission.Read);
        await sockets.Owner.WaitForAsync("Frodo's device: the share", ClientNotificationType.FileModified, h => h.FileId == shared);
        await sockets.Member.WaitForAsync("Sam: the file, with his share", ClientNotificationType.FileModified,
            h => h.FileId == shared && h.FileState == FileState.Active && h.Shares?.Single().CircleId == s.CircleId);

        var withdraw = await s.Shares.WithdrawShare(s.Drive.Alias, shared, s.CircleId);
        Assert.That(withdraw.StatusCode, Is.EqualTo(HttpStatusCode.OK), withdraw.Error?.Content);
        await sockets.Owner.WaitForAsync("Frodo's device: the withdrawal", ClientNotificationType.FileModified,
            h => h.FileId == shared && h.FileState == FileState.Active);
        await sockets.Member.WaitForAsync("Sam: no longer shared, ids, types and dates only", ClientNotificationType.FileDeleted,
            h => h.FileId == shared && h.FileState == FileState.Deleted && h.FileMetadata.AppData.Content == "" && h.Shares == null &&
                 h.FileMetadata.AppData.Tags == null && h.FileMetadata.AppData.GroupId == null && h.FileMetadata.SenderOdinId == null &&
                 h.SharedSecretEncryptedKeyHeader.EncryptedAesKey.All(b => b == 0));

        // In the order sent: by now the unshared file's event would have reached him
        Assert.That(sockets.Member.Seen.Where(e => e.Header?.FileId == notShared), Is.Empty, "Sam heard nothing of the unshared file");
    }

    [Test]
    public async Task AMembersEditAndDeleteReachEveryone()
    {
        var s = await ArrangeAsync();
        var (fileId, _, fileKey) = await s.UploadClientLockedAsync();
        var share = await s.Shares.WriteShares(s.Drive.Alias, new WriteSharesRequest
        {
            Shares = [s.Share(fileId, DrivePermission.Read | DrivePermission.Write, lockedFileKey: LockedFileKey())]
        });
        Assert.That(share.StatusCode, Is.EqualTo(HttpStatusCode.OK), share.Error?.Content);
        await using var sockets = await ShareSockets.OpenAsync(s);

        var newIv = await MemberEditAsync(s, await s.HeaderAsync(fileId), fileKey, "edited by Sam");
        await sockets.Owner.WaitForAsync("Frodo's device: Sam's edit", ClientNotificationType.FileModified,
            h => h.FileId == fileId && h.SharedSecretEncryptedKeyHeader.Iv.SequenceEqual(newIv));
        await sockets.Member.WaitForAsync("Sam: his edit, with his share's key and the new IV", ClientNotificationType.FileModified,
            h => h.FileId == fileId && h.SharedSecretEncryptedKeyHeader.Type == EncryptionType.Share &&
                 h.SharedSecretEncryptedKeyHeader.Iv.SequenceEqual(newIv));

        var gtid = (await s.GtidOfAsync(fileId)).GlobalTransitId;
        var delete = await s.Sam.Drives.PeerWriter.SendDeleteRequestOverPeer(s.Frodo.Identity, s.Drive, gtid);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK), delete.Error?.Content);
        await s.Sam.Sync.DrainOutboxAsync();
        await sockets.Owner.WaitForAsync("Frodo's device: Sam's delete", ClientNotificationType.FileDeleted, h => h.FileId == fileId);
        await sockets.Member.WaitForAsync("Sam: his delete", ClientNotificationType.FileDeleted, h => h.FileId == fileId);
    }

    [Test]
    public async Task AMembersNewFileReachesEveryone()
    {
        var s = await ArrangeAsync();
        await using var sockets = await ShareSockets.OpenAsync(s);

        var uniqueId = Guid.NewGuid();
        await ZkTestFiles.SendDepositAsync(s.Sam, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "Sam's file", remoteDrive: s.Drive,
            shares: [s.Share(Guid.Empty, DrivePermission.Read, lockedFileKey: LockedFileKey())]);

        var added = (await s.HeaderByUniqueIdAsync(uniqueId)).FileId;
        await sockets.Owner.WaitForAsync("Frodo's device: Sam's new file", ClientNotificationType.FileAdded, h => h.FileId == added);
        await sockets.Member.WaitForAsync("Sam: his new file, shared with his circle", ClientNotificationType.FileAdded,
            h => h.FileId == added && h.Shares?.Single().CircleId == s.CircleId);
    }

    private static IEnumerable<(string, int)> ReactionsOf(SharedSecretEncryptedFileHeader header) =>
        header.FileMetadata.ReactionPreview?.Reactions.Values.Select(r => (r.ReactionContent, r.Count)) ?? [];

    /// <summary>Sam adds or removes his reaction on Frodo's file, through his own server.</summary>
    private static async Task ReactAsync(Arranged s, GlobalTransitIdFileIdentifier file, bool add)
    {
        var response = add
            ? await SamAddsReactionAsync(s, file, Thumbsup)
            : await s.Sam.RefitFor<IRefitOwnerTransitReaction>().DeleteReactionContent(new PeerDeleteReactionRequest
            {
                OdinId = s.Frodo.Identity,
                Request = new DeleteReactionRequestByGlobalTransitId { File = file, Reaction = Thumbsup }
            });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"the reaction answered {response.StatusCode}: {response.Error?.Content}");
    }

    private static async Task<SharedSecretEncryptedFileHeader> SamsHeaderAsync(Arranged s, Guid fileId)
    {
        var read = await s.Sam.Drives.Peer.GetFileHeaderAsync(s.Frodo.Identity, s.Drive.Alias, fileId);
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK), read.Error?.Content);
        return read.Content!;
    }

    private static async Task<SharedSecretEncryptedFileHeader> SamsQueriedAsync(Arranged s, Guid fileId)
    {
        var query = await s.Sam.Drives.Peer.QueryBatchAsync(s.Frodo.Identity, s.Drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = s.Drive },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 100, IncludeMetadataHeader = true }
        });
        Assert.That(query.StatusCode, Is.EqualTo(HttpStatusCode.OK), query.Error?.Content);
        return query.Content!.SearchResults.Single(h => h.FileId == fileId);
    }

    /// <summary>
    /// Frodo's owner device socket and Sam's peer socket on Frodo's server, subscribed to the drive; unregistered when
    /// disposed.
    /// </summary>
    private sealed class ShareSockets : IAsyncDisposable
    {
        private readonly SharedDeviceSocketCollection<AppNotificationHandler> _ownerSockets;
        private readonly SharedDeviceSocketCollection<PeerAppNotificationHandler> _peerSockets;
        private readonly AppNotificationDispatcher _ownerDispatcher;
        private readonly PeerAppNotificationDispatcher _peerDispatcher;

        private ShareSockets(ILifetimeScope scope, Arranged s, bool memberReviewed)
        {
            _ownerSockets = scope.Resolve<SharedDeviceSocketCollection<AppNotificationHandler>>();
            _peerSockets = scope.Resolve<SharedDeviceSocketCollection<PeerAppNotificationHandler>>();
            _ownerDispatcher = scope.Resolve<AppNotificationDispatcher>();
            _peerDispatcher = scope.Resolve<PeerAppNotificationDispatcher>();
            Owner = new KeptSocket(s.Drive, Context(s.Drive, Identities.Frodo, SecurityGroupType.Owner, null, DrivePermission.All,
                masterKey: true));
            Member = new KeptSocket(s.Drive, Context(s.Drive, Identities.Sam, SecurityGroupType.Connected, s.CircleId,
                DrivePermission.Read | DrivePermission.Write, reviewed: memberReviewed));
        }

        public KeptSocket Owner { get; }
        public KeptSocket Member { get; }

        /// <param name="memberReviewed">False for Sam's socket as an unreviewed connection's, which counts as Authenticated.</param>
        public static async Task<ShareSockets> OpenAsync(Arranged s, bool memberReviewed = true)
        {
            var sockets = new ShareSockets(s.Frodo.Host.GetTenantScope(s.Frodo.Identity.DomainName), s, memberReviewed);
            sockets._ownerSockets.AddSocket(sockets.Owner.Device);
            sockets._peerSockets.AddSocket(sockets.Member.Device);
            await sockets._ownerDispatcher.SubscribeAsync();
            await sockets._peerDispatcher.SubscribeAsync();
            return sockets;
        }

        public async ValueTask DisposeAsync()
        {
            await _ownerDispatcher.UnsubscribeAsync();
            await _peerDispatcher.UnsubscribeAsync();
            await _ownerSockets.RemoveSocket(Owner.Device.Key);
            await _peerSockets.RemoveSocket(Member.Device.Key);
        }

        private static OdinContext Context(TargetDrive drive, string identity, SecurityGroupType level, Guid? circleId,
            DrivePermission permission, bool masterKey = false, bool reviewed = true)
        {
            var context = new OdinContext
            {
                Caller = new CallerContext((OdinId)identity, masterKey ? Guid.NewGuid().ToByteArray().ToSensitiveByteArray() : null!, level,
                    circleIds: circleId is { } c ? [new GuidId(c)] : null) { IsReviewed = reviewed }
            };
            var grant = new DriveGrant
            {
                DriveId = drive.Alias,
                PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = permission }
            };
            context.SetPermissionContext(new PermissionContext(
                new Dictionary<string, PermissionGroup> { ["drive"] = new(new PermissionSet(), [grant], null, null) },
                ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray()));
            return context;
        }
    }

    /// <summary>A device socket that keeps the drive events it is sent, decrypted.</summary>
    private sealed class KeptSocket
    {
        private readonly KeepingWebSocket _socket = new();

        public KeptSocket(TargetDrive drive, OdinContext context)
        {
            Drive = drive;
            Context = context;
            Device = new DeviceSocket { Key = Guid.NewGuid(), Socket = _socket, DeviceOdinContext = context, Drives = [drive.Alias] };
        }

        public TargetDrive Drive { get; }
        public OdinContext Context { get; }
        public DeviceSocket Device { get; }

        public IEnumerable<(ClientNotificationType Type, SharedSecretEncryptedFileHeader? Header)> Seen =>
            _socket.Sent.Select(Read).Where(e => e.Type != null).Select(e => (e.Type!.Value, e.Header));

        public async Task WaitForAsync(string what, ClientNotificationType type, Func<SharedSecretEncryptedFileHeader, bool> match)
        {
            await Poll.UntilAsync(() => Task.FromResult(Seen.Any(e => e.Type == type && e.Header != null && match(e.Header))),
                TimeSpan.FromSeconds(10),
                () => $"{what}: no {type} matched; seen: " +
                      string.Join(", ", Seen.Select(e => $"{e.Type} {e.Header?.FileId} {e.Header?.FileSystemType}")));
        }

        private (ClientNotificationType? Type, SharedSecretEncryptedFileHeader? Header) Read(string sent)
        {
            var payload = OdinSystemSerializer.Deserialize<ClientNotificationPayload>(sent)!;
            var json = payload.IsEncrypted
                ? SharedSecretEncryptedPayload.Decrypt(payload.Payload, Context.PermissionsContext.SharedSecretKey).ToStringFromUtf8Bytes()
                : payload.Payload;
            var envelope = OdinSystemSerializer.Deserialize<Envelope>(json)!;
            if (envelope.NotificationType is not (ClientNotificationType.FileAdded or ClientNotificationType.FileModified
                or ClientNotificationType.FileDeleted))
            {
                return (null, null);
            }

            return (envelope.NotificationType, OdinSystemSerializer.Deserialize<ClientDriveNotification>(envelope.Data)?.Header);
        }

        private sealed class Envelope
        {
            public ClientNotificationType NotificationType { get; set; }
            public string Data { get; set; } = "";
        }
    }

    /// <summary>An open web socket that keeps what is sent on it, as text.</summary>
    private sealed class KeepingWebSocket : WebSocket
    {
        private readonly ConcurrentQueue<string> _sent = new();

        public IEnumerable<string> Sent => _sent;

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;

        public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, WebSocketMessageFlags messageFlags,
            CancellationToken cancellationToken)
        {
            _sent.Enqueue(buffer.ToArray().ToStringFromUtf8Bytes());
            return ValueTask.CompletedTask;
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage,
            CancellationToken cancellationToken) =>
            SendAsync(buffer.AsMemory(), messageType, WebSocketMessageFlags.EndOfMessage, cancellationToken).AsTask();

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            Task.FromCanceled<WebSocketReceiveResult>(new CancellationToken(true));

        public override void Abort()
        {
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override void Dispose()
        {
        }
    }
}
