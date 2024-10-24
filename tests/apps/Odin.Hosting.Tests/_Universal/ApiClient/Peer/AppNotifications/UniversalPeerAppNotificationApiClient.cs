using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Peer.AppNotification;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Peer.AppNotifications;

public class UniversalPeerAppNotificationApiClient(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<AppNotificationTokenResponse>> GetRemoteNotificationToken(GetRemoteTokenRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerAppNotification>(client, sharedSecret);
        var response = await svc.GetRemoteNotificationToken(request);
        return response;
    }

}