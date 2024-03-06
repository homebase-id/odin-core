using System.Threading.Tasks;
using Odin.Services.Base;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.Utils;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Security;

public class AppSecurityApiClient : AppApiTestUtils
{
    private readonly AppClientToken _token;

    public AppSecurityApiClient(OwnerApiTestUtils ownerApiTestUtils, AppClientToken token) : base(ownerApiTestUtils)
    {
        _token = token;
    }
    

    public async Task<RedactedOdinContext> GetSecurityContext()
    {

        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<ITestSecurityContextAppClient>(client, _token.SharedSecret);
            var apiResponse = await svc.GetDotYouContext();
            return apiResponse.Content;
        }
    }
}