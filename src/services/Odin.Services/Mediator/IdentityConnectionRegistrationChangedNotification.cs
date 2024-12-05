using MediatR;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite;

namespace Odin.Services.Mediator;

public class IdentityConnectionRegistrationChangedNotification : MediatorNotificationBase
{
    public OdinId OdinId { get; init; }
}