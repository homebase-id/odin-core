using MediatR;
using Odin.Core.Identity;

namespace Odin.Core.Services.AppNotifications.ClientNotifications;

public class IdentityConnectionRegistrationChangedNotification : INotification
{
    public OdinId OdinId { get; set; }
}