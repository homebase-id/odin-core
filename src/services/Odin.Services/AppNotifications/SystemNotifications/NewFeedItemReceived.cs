using System;
using MediatR;
using Odin.Core.Identity;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Core.Storage;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.SystemNotifications;

public class NewFeedItemReceived : MediatorNotificationBase
{
    public OdinId Sender { get; init; }
    public FileSystemType FileSystemType { get; init; }
}