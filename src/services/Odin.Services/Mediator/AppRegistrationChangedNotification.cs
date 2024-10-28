using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Authorization.Apps;

namespace Odin.Services.Mediator;

public class AppRegistrationChangedNotification : MediatorNotificationBase
{
    public AppRegistration NewAppRegistration { get; init; }
    public AppRegistration OldAppRegistration { get; init; }
    public IdentityDatabase db { get; init; }
}