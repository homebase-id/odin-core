using MediatR;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.AppNotifications.ClientNotifications;

public class IdentityConnectionRegistrationChangedNotification : INotification
{
    public OdinId DotYouId { get; set; }
}