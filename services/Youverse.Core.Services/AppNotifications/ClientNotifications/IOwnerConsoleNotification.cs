using MediatR;

namespace Youverse.Core.Services.AppNotifications.ClientNotifications
{
    public interface IOwnerConsoleNotification : INotification
    {
        string Key { get; }
    }
}