using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Services.Mediator;

public class PushNotificationEnqueuedNotification : MediatorNotificationBase
{
    public IdentityDatabase db { get; init; }
}