#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Peer.Encryption;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.AppNotifications.WebSocket;

public class ClientDriveNotification
{
    public TargetDrive? TargetDrive { get; set; }
    public SharedSecretEncryptedFileHeader? Header { get; set; }
    public SharedSecretEncryptedFileHeader? PreviousServerFileHeader { get; set; }

    /// <summary>
    /// The file, for a socket that asked for <see cref="FileResultShape.Minimal"/> (instead of <see cref="Header"/>)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FileRef? FileRef { get; set; }

    /// <summary>
    /// The file as it was before a delete, for a socket that asked for <see cref="FileResultShape.Minimal"/>
    /// (instead of <see cref="PreviousServerFileHeader"/>)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FileRef? PreviousFileRef { get; set; }

    /// <summary>
    /// A drive event in <paramref name="shape"/>, its key headers encrypted for <paramref name="deviceOdinContext"/>.
    /// A context without a shared secret gets neither headers nor refs. The headers are the event's, shared by
    /// every socket, so they are never changed here.
    /// </summary>
    /// <param name="callerShares">On a Selective Share drive, a member's live shares on the file, the one they read it
    /// through first (section 6).</param>
    public static ClientDriveNotification For(FileResultShape shape, IOdinContext? deviceOdinContext, StorageDrive? drive,
        ServerFileHeader? header, ServerFileHeader? previousHeader, IReadOnlyList<DriveAclIndexRecord>? callerShares = null)
    {
        var notification = new ClientDriveNotification { TargetDrive = drive?.TargetDriveInfo };
        if (deviceOdinContext?.PermissionsContext?.SharedSecretKey == null)
        {
            return notification;
        }

        if (shape == FileResultShape.Minimal)
        {
            notification.FileRef = header == null ? null : FileRef.FromHeader(header);
            notification.PreviousFileRef = previousHeader == null ? null : FileRef.FromHeader(previousHeader);
            return notification;
        }

        notification.Header = DriveFileUtility.CreateClientFileHeader(header, drive, deviceOdinContext, callerShares: callerShares);
        notification.PreviousServerFileHeader = DriveFileUtility.CreateClientFileHeader(previousHeader, drive, deviceOdinContext,
            callerShares: callerShares);

        if (shape == FileResultShape.NoContent)
        {
            WithoutContent(notification.Header);
            WithoutContent(notification.PreviousServerFileHeader);
        }

        return notification;
    }

    // The client header's AppData is the event's own instance, so it is replaced by a copy, not emptied
    private static void WithoutContent(SharedSecretEncryptedFileHeader? clientHeader)
    {
        if (clientHeader?.FileMetadata?.AppData != null)
        {
            clientHeader.FileMetadata.AppData = clientHeader.FileMetadata.AppData.WithoutContent();
        }
    }
}

/// <summary>
/// One drive event, as the message each socket gets in the shape it asked for. A minimal socket's message does not
/// depend on the socket (no key header in it), so it is built once per event; full and noContent carry a key header
/// encrypted for each socket. <paramref name="previousHeader"/> is set only for a delete (the publisher sees to that).
/// A socket gets the event only if it may read the file (<see cref="VisibleToAsync"/>).  <paramref name="drive"/> is the
/// file's, null when it is gone.
/// </summary>
public sealed class DriveEventMessage(
    ClientNotificationType notificationType,
    StorageDrive? drive,
    ServerFileHeader? header,
    ServerFileHeader? previousHeader)
{
    // A minimal event leaves out what it does not carry (the headers, an absent ref) instead of sending null;
    // full and noContent keep their nulls, as clients have always had them
    private static readonly JsonSerializerOptions MinimalOptions = new(OdinSystemSerializer.JsonSerializerOptions!)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private string? _minimalJson;

    /// <summary>
    /// Whether <paramref name="socket"/> may read the file, by the read rule a query applies
    /// (<see cref="IDriveAclAuthorizationService.CallerMayReadAsync"/>), whatever the result shape. Drive read, checked at the
    /// handshake, is not enough: a file's ACL can exclude a reader of its drive. A delete is checked against the
    /// file as it was, since the deleted header no longer says who could read it (it keeps the key header).
    /// Unlike a query, this doesn't leave out an encrypted file the socket holds no storage key for.  On a drive that blocks
    /// enumeration only a caller who may list it hears of its files: an event would hand everyone else each new file's
    /// address (a WebDrop drop's unique id) without their having been given it.
    /// </summary>
    public Task<bool> VisibleToAsync(DeviceSocket socket, IDriveAclAuthorizationService aclService)
    {
        var (current, previous) = HeadersFor(socket.DeviceOdinContext);
        return MayReadAsync(socket.DeviceOdinContext, previous ?? current, aclService);
    }

    private async Task<bool> MayReadAsync(IOdinContext? deviceOdinContext, ServerFileHeader? file,
        IDriveAclAuthorizationService aclService) =>
        MayHearOf(deviceOdinContext) && await aclService.CallerMayReadAsync(drive, file, deviceOdinContext!);

    /// <summary>
    /// Whether the socket may hear of the drive's files at all: on a drive that blocks enumeration only a caller who may
    /// list it; with no drive to ask (it is gone), only the owner, the owner's apps and the server.
    /// </summary>
    private bool MayHearOf(IOdinContext? deviceOdinContext) =>
        deviceOdinContext != null &&
        (drive?.MayBeListedBy(deviceOdinContext.Caller) ?? DriveAclAuthorizationService.PassesEveryAcl(deviceOdinContext.Caller));

    /// <summary>
    /// The event's headers as <paramref name="deviceOdinContext"/> gets them: on a Server Share drive a deposit its request
    /// can open is the server's own file to it, as a read would have converted it (<see cref="Deposits.ConvertedOnServerShare"/>).
    /// Copies, never written: the event's headers are shared by every socket.
    /// </summary>
    private (ServerFileHeader? Header, ServerFileHeader? Previous) HeadersFor(IOdinContext? deviceOdinContext) =>
        (OpenedFor(header, deviceOdinContext), OpenedFor(previousHeader, deviceOdinContext));

    private ServerFileHeader? OpenedFor(ServerFileHeader? stored, IOdinContext? deviceOdinContext)
    {
        if (stored == null || deviceOdinContext == null || !Deposits.IsDeposit(stored) ||
            !ZkDriveFiles.ServerOpensDepositsFor(drive, deviceOdinContext))
        {
            return stored;
        }

        // The same for every device holding the drive key, so opened once per event; a failure is the device's to retry
        if (_opened?.TryGetValue(stored, out var opened) == true)
        {
            return opened;
        }

        if (Deposits.ConvertedOnServerShare(drive, [stored], deviceOdinContext) is not [var (_, keyHeader)])
        {
            return stored;
        }

        opened = new ServerFileHeader { EncryptedKeyHeader = keyHeader, FileMetadata = stored.FileMetadata, ServerMetadata = stored.ServerMetadata };
        (_opened ??= new Dictionary<ServerFileHeader, ServerFileHeader>(ReferenceEqualityComparer.Instance))[stored] = opened;
        return opened;
    }

    // Only for an event carrying a deposit that opened: what a device holding the drive key gets of it
    private Dictionary<ServerFileHeader, ServerFileHeader>? _opened;

    /// <summary>
    /// A dispatcher's drive event, sent to each of its <paramref name="sockets"/> on the drive that gets it (#1966): when
    /// the drive can't be read, every one of them misses it and is aborted, so each reconnects and catches up; when one
    /// socket's message can't be built, that socket alone (<see cref="TryMessageForAsync"/>).
    /// </summary>
    public static async Task PublishAsync(Guid driveId, ClientNotificationType notificationType, ServerFileHeader? header,
        ServerFileHeader? previousHeader, IReadOnlyList<DeviceSocket> sockets, IDriveManager driveManager,
        IDriveAclAuthorizationService aclService, Func<DeviceSocket, string, Task> send, ILogger logger)
    {
        DriveEventMessage driveEvent;
        try
        {
            var drive = await driveManager.GetDriveAsync(driveId);
            driveEvent = new DriveEventMessage(notificationType, drive, header, previousHeader);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Drive event for drive {driveId}: {error}", driveId, e.Message);
            foreach (var deviceSocket in sockets)
            {
                deviceSocket.Abort(logger, "drive event could not be prepared", e);
            }

            return;
        }

        foreach (var deviceSocket in sockets)
        {
            if (await driveEvent.TryMessageForAsync(deviceSocket, aclService, logger) is { } json)
            {
                await send(deviceSocket, json);
            }
        }
    }

    /// <summary>
    /// <see cref="MessageForAsync"/> for a dispatcher's loop: when the message can't be built, that socket alone misses the
    /// event and is aborted, so its client reconnects and catches up, and the others still get it (#1966).
    /// </summary>
    private async Task<string?> TryMessageForAsync(DeviceSocket socket, IDriveAclAuthorizationService aclService, ILogger logger)
    {
        try
        {
            return await MessageForAsync(socket, aclService);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Drive event for socket {key}: {error}", socket.Key, e.Message);
            socket.Abort(logger, "drive event could not be prepared", e);
            return null;
        }
    }

    /// <summary>
    /// The message for <paramref name="socket"/>, or null when it doesn't get this event.  On a Selective Share drive a
    /// member's socket is judged by their share, not the event's headers alone (docs/selective-share-pr3-plan.md,
    /// section 9): the file with their share's key while they may read it, and once a share naming them ends, the
    /// tombstone their sync gets (section 8).  Every other socket: <see cref="VisibleToAsync"/>, then <see cref="JsonFor"/>.
    /// </summary>
    public async Task<string?> MessageForAsync(DeviceSocket socket, IDriveAclAuthorizationService aclService)
    {
        var deviceOdinContext = socket.DeviceOdinContext;
        var shape = socket.ResultShape; // read once, as JsonFor does: a handshake may change it while this runs
        var headers = HeadersFor(deviceOdinContext);
        var (current, previous) = headers;
        var file = previous ?? current;
        if (!MayHearOf(deviceOdinContext))
        {
            return null;
        }

        var shares = file?.FileMetadata?.File == null
            ? null
            : await aclService.FilesSharedWithCallerAsync(drive, [file], deviceOdinContext!);
        if (shares == null)
        {
            return await MayReadAsync(deviceOdinContext, file, aclService) ? JsonFor(socket, headers) : null;
        }

        if (await aclService.CallerMayReadAsync(drive, file!, deviceOdinContext!, shares))
        {
            return Json(notificationType, ClientDriveNotification.For(shape, deviceOdinContext, drive, current,
                previous, shares.LiveRowsOf(file!.FileMetadata.File.FileId)), shape);
        }

        // From the header as it is now: a change that ended the share announces its own time, which a client goes by
        return DriveStorageServiceBase.NoLongerSharedViewOf(current ?? file!, shares) is { } noLongerShared
            ? Json(ClientNotificationType.FileDeleted, ClientDriveNotification.For(shape, deviceOdinContext, drive,
                noLongerShared, null), shape)
            : null;
    }

    private static string Json(ClientNotificationType type, ClientDriveNotification notification, FileResultShape shape) =>
        OdinSystemSerializer.Serialize(new
        {
            NotificationType = type,
            Data = shape == FileResultShape.Minimal
                ? OdinSystemSerializer.Serialize(notification, MinimalOptions)
                : OdinSystemSerializer.Serialize(notification)
        });

    private string JsonFor(DeviceSocket socket, (ServerFileHeader? Header, ServerFileHeader? Previous) headers)
    {
        // Read once: a handshake may change them while this runs, and the cached message must be what was built
        var shape = socket.ResultShape;
        var deviceOdinContext = socket.DeviceOdinContext;

        var shared = shape == FileResultShape.Minimal && deviceOdinContext?.PermissionsContext?.SharedSecretKey != null;
        if (shared && _minimalJson != null)
        {
            return _minimalJson;
        }

        var notification = ClientDriveNotification.For(shape, deviceOdinContext, drive, headers.Header, headers.Previous);
        var json = Json(notificationType, notification, shape);

        if (shared)
        {
            _minimalJson = json;
        }

        return json;
    }
}
