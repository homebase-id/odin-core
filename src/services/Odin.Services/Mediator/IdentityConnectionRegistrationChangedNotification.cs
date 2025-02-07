using Odin.Core.Identity;

namespace Odin.Services.Mediator;

public class IdentityConnectionRegistrationChangedNotification : MediatorNotificationBase
{
    public OdinId OdinId { get; init; }
}