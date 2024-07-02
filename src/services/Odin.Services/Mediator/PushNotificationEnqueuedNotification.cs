using Odin.Core.Storage.SQLite;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Services.Mediator;

public class PushNotificationEnqueuedNotification : MediatorNotificationBase
{
    public DatabaseConnection DatabaseConnection { get; init; }
    public OutboxItemType Type { get; set; }
}