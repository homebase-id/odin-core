using MediatR;
using Odin.Core.Identity;

namespace Odin.Core.Services.Mediator;

public class IdentityConnectionRegistrationChangedNotification : INotification
{
    public OdinId OdinId { get; set; }
}