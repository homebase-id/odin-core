using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Notifications;

public class NotificationsApiClient
{
    private readonly OdinId _identity;
    private readonly IApiClientFactory _factory;

    public NotificationsApiClient(OdinId identity, IApiClientFactory factory)
    {
        _identity = identity;
        _factory = factory;
    }

    public async Task<ApiResponse<HttpContent>> AddNotification()
    {
        var client = _factory.CreateHttpClient(_identity, out var sharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitNotifications>(client, sharedSecret);
            var response = await svc.AddNotification()
            return response;
        }
    }
}