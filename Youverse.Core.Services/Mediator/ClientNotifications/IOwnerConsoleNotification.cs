using MediatR;

namespace Youverse.Core.Services.Mediator.ClientNotifications
{
    public interface IOwnerConsoleNotification : INotification
    {
        string Key { get; }
    }
}