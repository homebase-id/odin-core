using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.OwnerToken.Notifications;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.AppNotifications.Push;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class PushNotificationV2Client(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<PushNotificationVerificationResult>> Verify()
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IPushNotificationHttpClientApiV2>(client, sharedSecret);
        return await svc.Verify();
    }

    public async Task<ApiResponse<HttpContent>> SubscribeFirebase(PushNotificationSubscribeFirebaseRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IPushNotificationHttpClientApiV2>(client, sharedSecret);
        return await svc.SubscribeFirebase(request);
    }

    public async Task<ApiResponse<HttpContent>> Unsubscribe()
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IPushNotificationHttpClientApiV2>(client, sharedSecret);
        return await svc.Unsubscribe();
    }

    public async Task<ApiResponse<RedactedPushNotificationSubscription>> GetSubscription()
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IPushNotificationHttpClientApiV2>(client, sharedSecret);
        return await svc.GetSubscription();
    }
}
