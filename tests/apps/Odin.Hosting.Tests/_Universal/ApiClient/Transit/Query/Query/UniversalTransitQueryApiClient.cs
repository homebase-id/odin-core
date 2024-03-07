using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Storage;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Transit.Query.Query;

public class UniversalPeerQueryApiClient(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<RedactedOdinContext>> GetRemoteDotYouContext(TransitGetSecurityContextRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitTransitQuery>(client, sharedSecret);
        var response = await svc.GetRemoteDotYouContext(request);
        return response;
    }

    public async Task<ApiResponse<QueryBatchCollectionResponse>> GetBatchCollection(PeerQueryBatchCollectionRequest request,
        FileSystemType fst = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitTransitQuery>(client, sharedSecret);
        var apiResponse = await svc.GetBatchCollection(request);
        return apiResponse;
    }

    public async Task<ApiResponse<QueryModifiedResponse>> GetModified(PeerQueryModifiedRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitTransitQuery>(client, sharedSecret);
        var apiResponse = await svc.GetModified(request);
        return apiResponse;
    }

    public async Task<ApiResponse<QueryBatchResponse>> GetBatch(PeerQueryBatchRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitTransitQuery>(client, sharedSecret);
        var apiResponse = await svc.GetBatch(request);
        return apiResponse;
    }

    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(TransitExternalFileIdentifier file,
        FileSystemType fst = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitTransitQuery>(client, sharedSecret);
        var apiResponse = await svc.GetFileHeader(file);
        return apiResponse;
    }

    public async Task<ApiResponse<HttpContent>> GetPayload(TransitGetPayloadRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitTransitQuery>(client, sharedSecret);
        var apiResponse = await svc.GetPayload(request);
        return apiResponse;
    }

    public async Task<ApiResponse<HttpContent>> GetThumbnail(TransitGetThumbRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitTransitQuery>(client, sharedSecret);
        var apiResponse = await svc.GetThumbnail(request);
        return apiResponse;
    }

    public async Task<ApiResponse<PagedResult<ClientDriveData>>> GetDrives(TransitGetDrivesByTypeRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitTransitQuery>(client, sharedSecret);
        var apiResponse = await svc.GetDrives(request);
        return apiResponse;
    }
}