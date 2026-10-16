#nullable enable
using System.Text.Json.Serialization;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.Apps;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;

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
    /// A drive event as <paramref name="socket"/> asked to see it at its handshake (<see cref="DeviceSocket.ResultShape"/>).
    /// The headers are the event's, shared by every socket, so they are never changed here.
    /// </summary>
    public static ClientDriveNotification For(DeviceSocket socket, TargetDrive? targetDrive, ServerFileHeader? header,
        ServerFileHeader? previousHeader)
    {
        var notification = new ClientDriveNotification { TargetDrive = targetDrive };

        // A socket without a shared secret gets neither headers nor refs
        var deviceOdinContext = socket.DeviceOdinContext;
        if (!HasSharedSecret(socket))
        {
            return notification;
        }

        if (socket.ResultShape == FileResultShape.Minimal)
        {
            notification.FileRef = header == null ? null : FileRef.FromHeader(header);
            notification.PreviousFileRef = previousHeader == null ? null : FileRef.FromHeader(previousHeader);
            return notification;
        }

        notification.Header = DriveFileUtility.CreateClientFileHeader(header, deviceOdinContext);
        notification.PreviousServerFileHeader = DriveFileUtility.CreateClientFileHeader(previousHeader, deviceOdinContext);

        if (socket.ResultShape == FileResultShape.NoContent)
        {
            WithoutContent(notification.Header);
            WithoutContent(notification.PreviousServerFileHeader);
        }

        return notification;
    }

    public static bool HasSharedSecret(DeviceSocket socket) => socket.DeviceOdinContext?.PermissionsContext?.SharedSecretKey != null;

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
/// encrypted for each socket.
/// </summary>
public sealed class DriveEventMessage(
    ClientNotificationType notificationType,
    TargetDrive? targetDrive,
    ServerFileHeader? header,
    ServerFileHeader? previousHeader,
    bool isDelete)
{
    // Only a delete sends the header the file had before
    private readonly ServerFileHeader? _previousHeader = isDelete ? previousHeader : null;
    private string? _minimalJson;

    public string JsonFor(DeviceSocket socket)
    {
        var shared = socket.ResultShape == FileResultShape.Minimal && ClientDriveNotification.HasSharedSecret(socket);
        if (shared && _minimalJson != null)
        {
            return _minimalJson;
        }

        var notification = ClientDriveNotification.For(socket, targetDrive, header, _previousHeader);
        var json = OdinSystemSerializer.Serialize(new
        {
            NotificationType = notificationType,
            Data = OdinSystemSerializer.Serialize(notification)
        });

        if (shared)
        {
            _minimalJson = json;
        }

        return json;
    }
}
