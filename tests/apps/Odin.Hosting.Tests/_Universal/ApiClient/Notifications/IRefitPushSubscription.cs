using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.OwnerToken.Notifications;
using Odin.Services.AppNotifications.Push;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Notifications;

/// <summary>The device push subscription endpoints under /notify/push (PushNotificationControllerBase).</summary>
public interface IRefitPushSubscription
{
    private const string RootPath = "/notify/push";

    [Post(RootPath + "/subscribe-firebase")]
    Task<ApiResponse<HttpContent>> SubscribeFirebase([Body] PushNotificationSubscribeFirebaseRequest request);

    [Get(RootPath + "/subscription")]
    Task<ApiResponse<RedactedPushNotificationSubscription>> GetSubscription();

    [Post(RootPath + "/unsubscribeAll")]
    Task<ApiResponse<HttpContent>> UnsubscribeAll();
}
