using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Membership.Connections.Requests;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class V2ConnectionRequestsClient(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<AutoConnectResult>> AutoConnectAsync(ConnectionRequestHeader header)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IConnectionRequestsHttpClientApiV2>(client, sharedSecret);
        return await svc.AutoConnect(header);
    }
}
