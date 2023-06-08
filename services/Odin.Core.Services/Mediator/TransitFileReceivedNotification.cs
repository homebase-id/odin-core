using System;
using MediatR;
using Odin.Core.Services.AppNotifications;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Transit;
using Odin.Core.Storage;

namespace Odin.Core.Services.Mediator;

public class TransitFileReceivedNotification : EventArgs, INotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.TransitFileReceived;

    public ExternalFileIdentifier TempFile { get; set; }
    public FileSystemType FileSystemType { get; set; }
    public TransferFileType TransferFileType { get; set; }
}
