using System.Threading.Tasks;
using Odin.Core.Dto;
using Refit;

namespace Odin.Services.AppNotifications.Push;

public interface IDevicePushNotificationApi
{
    [Post("/message")]
    Task<string> PostMessage([Body] DevicePushNotificationRequest request);
}

