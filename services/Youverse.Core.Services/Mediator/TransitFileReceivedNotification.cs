using System;
using MediatR;
using Youverse.Core.Services.AppNotifications;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Mediator;

public class TransitFileReceivedNotification : EventArgs, INotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.TransitFileReceived;

    public ExternalFileIdentifier TempFile { get; set; }
    public FileSystemType FileSystemType { get; set; }
}
