using System;
using MediatR;
using Odin.Core.Identity;

namespace Odin.Core.Services.AppNotifications.SystemNotifications;

public class NewFeedItemReceived : INotification
{
    public Guid NotificationTypeId { get; } = Guid.Parse("ad695388-c2df-47a0-ad5b-fc9f9e1fffc9");

    public OdinId Sender { get; set; }
}