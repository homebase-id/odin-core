using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.OwnerToken.Notifications;
using Odin.Hosting.UnifiedV2;
using Odin.Services.AppNotifications.Push;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IPushNotificationHttpClientApiV2
{
    private const string Endpoint = UnifiedApiRouteConstants.Notify;

    [Get(Endpoint + "/verify")]
    Task<ApiResponse<PushNotificationVerificationResult>> Verify();

    [Post(Endpoint + "/subscribe-firebase")]
    Task<ApiResponse<HttpContent>> SubscribeFirebase([Body] PushNotificationSubscribeFirebaseRequest request);

    [Post(Endpoint + "/unsubscribe")]
    Task<ApiResponse<HttpContent>> Unsubscribe();

    [Get(Endpoint + "/subscription")]
    Task<ApiResponse<RedactedPushNotificationSubscription>> GetSubscription();
}
