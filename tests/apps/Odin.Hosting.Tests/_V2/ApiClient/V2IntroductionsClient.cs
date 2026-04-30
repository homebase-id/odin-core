using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Membership.Connections.Requests;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class V2IntroductionsClient(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<IntroductionPreflightResult>> PreflightIntroductionsAsync(IntroductionGroup group)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IIntroductionsHttpClientApiV2>(client, sharedSecret);
        return await svc.PreflightIntroductions(group);
    }
}
