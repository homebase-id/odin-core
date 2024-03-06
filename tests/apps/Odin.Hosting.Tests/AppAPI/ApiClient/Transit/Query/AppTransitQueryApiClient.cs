using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Core.Storage;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Odin.Hosting.Tests.AppAPI.ApiClient.Base;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Transit.Query;

public class AppTransitQueryApiClient : AppApiClientBase
{
    private readonly AppClientToken _token;

    public AppTransitQueryApiClient(OwnerApiTestUtils ownerApiTestUtils, AppClientToken token) : base(ownerApiTestUtils)
    {
        _token = token;
    }

    public async Task<ApiResponse<RedactedOdinContext>> GetRemoteDotYouContext(TransitGetSecurityContextRequest request)
    {
        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitQuery>(client, _token.SharedSecret);
            var apiResponse = await svc.GetRemoteDotYouContext(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<QueryBatchCollectionResponse>> GetBatchCollection(PeerQueryBatchCollectionRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitQuery>(client, _token.SharedSecret);
            var apiResponse = await svc.GetBatchCollection(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<QueryModifiedResponse>> GetModified(PeerQueryModifiedRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitQuery>(client, _token.SharedSecret);
            var apiResponse = await svc.GetModified(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<QueryBatchResponse>> GetBatch(PeerQueryBatchRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitQuery>(client, _token.SharedSecret);
            var apiResponse = await svc.GetBatch(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(TransitExternalFileIdentifier file,
        FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitQuery>(client, _token.SharedSecret);
            var apiResponse = await svc.GetFileHeader(file);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> GetPayload(TransitGetPayloadRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitQuery>(client, _token.SharedSecret);
            var apiResponse = await svc.GetPayload(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> GetThumbnail(TransitGetThumbRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitQuery>(client, _token.SharedSecret);
            var apiResponse = await svc.GetThumbnail(request);
            return apiResponse;
        }
    }

    public async Task<ApiResponse<PagedResult<ClientDriveData>>> GetDrives(TransitGetDrivesByTypeRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = CreateAppApiHttpClient(_token, fst);
        {
            var svc = RefitCreator.RestServiceFor<IRefitAppTransitQuery>(client, _token.SharedSecret);
            var apiResponse = await svc.GetDrives(request);
            return apiResponse;
        }
    }
}