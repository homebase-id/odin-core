using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.UnifiedV2.Connections;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class V2ConnectionNetworkClient(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<List<CircleWithMembers>>> GetCirclesWithMembersAsync(bool includeSystemCircle = true)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IConnectionNetworkHttpClientApiV2>(client, sharedSecret);
        return await svc.GetCirclesWithMembers(includeSystemCircle);
    }
}
