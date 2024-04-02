using System.Threading.Tasks;
using Odin.Core.Dto;
using Refit;

namespace Odin.Services.AppNotifications.Push;

public interface IDevicePushNotificationApi
{
    [Post("/message/v1")]
    Task<string> PostMessage([Body] DevicePushNotificationRequestV1 request);
}

