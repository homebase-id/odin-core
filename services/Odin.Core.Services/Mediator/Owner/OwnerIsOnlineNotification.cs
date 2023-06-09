using MediatR;

namespace Odin.Core.Services.Mediator.Owner;

/// <summary>
/// Raised when the owner makes a request to the system, thus indicating we have access to
/// the master key to perform various system operations (rotate keys, etc.)
/// </summary>
public class OwnerIsOnlineNotification : INotification
{
}