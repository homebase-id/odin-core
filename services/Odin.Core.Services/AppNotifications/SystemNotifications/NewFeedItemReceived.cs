using System;
using MediatR;
using Odin.Core.Identity;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Storage;

namespace Odin.Core.Services.AppNotifications.SystemNotifications;

public class NewFeedItemReceived : INotification
{
    public OdinId Sender { get; set; }
    public FileSystemType FileSystemType { get; set; }
}