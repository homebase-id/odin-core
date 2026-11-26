using System;
using Odin.Core.Identity;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.SystemNotifications;

public class NewFeedItemReceived : MediatorNotificationBase
{
    public OdinId Sender { get; init; }
    public Guid GlobalTransitId { get; init; }
}
