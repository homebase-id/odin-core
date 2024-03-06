using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Refit;

namespace Odin.Services.Background.DefaultCron
{
    public interface ICronHttpClient
    {
        private const string TransitRootEndpoint = $"{OwnerApiPathConstants.TransitV1}/outbox/processor";

        [Post(TransitRootEndpoint + "/process")]
        Task<ApiResponse<HttpContent>> ProcessOutbox();

        [Post($"{OwnerApiPathConstants.PushNotificationsV1}/process")]
        Task<ApiResponse<HttpContent>> ProcessPushNotifications();
    }
}

