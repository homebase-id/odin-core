using MediatR;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Services.Mediator;

public class IdentityConnectionRegistrationChangedNotification : MediatorNotificationBase
{
    public OdinId OdinId { get; init; }
    public IdentityDatabase db { get; init; }
}