using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Email;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class V2MailClient(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<MailAppStatusResult>> GetStatusAsync()
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IMailHttpClientApiV2>(client, sharedSecret);
        return await svc.GetStatus();
    }

    public async Task<ApiResponse<MailRoundTripChallenge>> CreateChallengeAsync()
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IMailHttpClientApiV2>(client, sharedSecret);
        return await svc.CreateChallenge();
    }
}
