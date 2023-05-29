using System.Threading.Tasks;
using Youverse.Core.Services.Base;
using Youverse.Hosting.Tests.AppAPI.Utils;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests.AppAPI.ApiClient.Security;

public class AppSecurityApiClient : AppApiTestUtils
{
    private readonly AppClientToken _token;

    public AppSecurityApiClient(OwnerApiTestUtils ownerApiTestUtils, AppClientToken token) : base(ownerApiTestUtils)
    {
        _token = token;
    }
    

    public async Task<RedactedDotYouContext> GetSecurityContext()
    {

        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextAppClient>(client, _token.SharedSecret);
            var apiResponse = await svc.GetDotYouContext();
            return apiResponse.Content;
        }
    }
}