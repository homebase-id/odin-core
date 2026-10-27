#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;

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
    public static ClientDriveNotification For(FileResultShape shape, IOdinContext? deviceOdinContext, TargetDrive? targetDrive,
        ServerFileHeader? header, ServerFileHeader? previousHeader)
    {
        var notification = new ClientDriveNotification { TargetDrive = targetDrive };
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

        notification.Header = DriveFileUtility.CreateClientFileHeader(header, deviceOdinContext);
        notification.PreviousServerFileHeader = DriveFileUtility.CreateClientFileHeader(previousHeader, deviceOdinContext);

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
/// A socket gets the event only if it may read the file (<see cref="VisibleToAsync"/>).
/// </summary>
public sealed class DriveEventMessage(
    ClientNotificationType notificationType,
    TargetDrive? targetDrive,
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
    /// Unlike a query, this doesn't leave out an encrypted file the socket holds no storage key for.
    /// </summary>
    public async Task<bool> VisibleToAsync(DeviceSocket socket, IDriveAclAuthorizationService aclService)
    {
        var deviceOdinContext = socket.DeviceOdinContext;
        return deviceOdinContext != null && await aclService.CallerMayReadAsync(previousHeader ?? header, deviceOdinContext);
    }

    public string JsonFor(DeviceSocket socket)
    {
        // Read once: a handshake may change them while this runs, and the cached message must be what was built
        var shape = socket.ResultShape;
        var deviceOdinContext = socket.DeviceOdinContext;

        var shared = shape == FileResultShape.Minimal && deviceOdinContext?.PermissionsContext?.SharedSecretKey != null;
        if (shared && _minimalJson != null)
        {
            return _minimalJson;
        }

        var notification = ClientDriveNotification.For(shape, deviceOdinContext, targetDrive, header, previousHeader);
        var json = OdinSystemSerializer.Serialize(new
        {
            NotificationType = notificationType,
            Data = shape == FileResultShape.Minimal
                ? OdinSystemSerializer.Serialize(notification, MinimalOptions)
                : OdinSystemSerializer.Serialize(notification)
        });

        if (shared)
        {
            _minimalJson = json;
        }

        return json;
    }
}
