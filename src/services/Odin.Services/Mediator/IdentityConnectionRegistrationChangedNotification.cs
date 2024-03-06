using MediatR;
using Odin.Core.Identity;

namespace Odin.Services.Mediator;

public class IdentityConnectionRegistrationChangedNotification : INotification
{
    public OdinId OdinId { get; set; }
}