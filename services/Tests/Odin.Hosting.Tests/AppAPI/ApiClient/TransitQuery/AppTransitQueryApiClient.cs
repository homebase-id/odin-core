using System.Threading.Tasks;
using Odin.Core.Services.Drives;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests.AppAPI.ApiClient.Base;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.TransitQuery;

public class AppTransitQueryApiClient : AppApiClientBase
{
    private readonly AppClientToken _token;

    public AppTransitQueryApiClient(OwnerApiTestUtils ownerApiTestUtils, AppClientToken token) : base(ownerApiTestUtils)
    {
        _token = token;
    }

    public async Task<ApiResponse<QueryBatchResponse>> GetBatch(TransitQueryBatchRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitQuery>(client, _token.SharedSecret);
            var apiResponse = await svc.GetBatch(request);
            return apiResponse;
        }
    }
}