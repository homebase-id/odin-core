using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Peer.Query;

public class UniversalPeerQueryApiClient(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<RedactedOdinContext>> GetRemoteDotYouContext(TransitGetSecurityContextRequest request)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerQuery>(client, sharedSecret);
        var response = await svc.GetRemoteDotYouContext(request);
        return response;
    }

    public async Task<ApiResponse<QueryBatchCollectionResponse>> GetBatchCollection(PeerQueryBatchCollectionRequest request,
        FileSystemType fst = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fst);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerQuery>(client, sharedSecret);
        var apiResponse = await svc.GetBatchCollection(request);
        return apiResponse;
    }

    public async Task<ApiResponse<QueryModifiedResponse>> GetModified(PeerQueryModifiedRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fst);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerQuery>(client, sharedSecret);
        var apiResponse = await svc.GetModified(request);
        return apiResponse;
    }

    public async Task<ApiResponse<QueryBatchResponse>> GetBatch(PeerQueryBatchRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fst);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerQuery>(client, sharedSecret);
        var apiResponse = await svc.GetBatch(request);
        return apiResponse;
    }

    public async Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(TransitExternalFileIdentifier file,
        FileSystemType fst = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fst);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerQuery>(client, sharedSecret);
        var apiResponse = await svc.GetFileHeader(file);
        return apiResponse;
    }

    //

    public async Task<ApiResponse<QueryBatchResponse>> GetFileHeaderByGlobalTransitId(OdinId odinId, GlobalTransitIdFileIdentifier file,
        FileSystemType fst = FileSystemType.Standard)
    {
        var request = new PeerQueryBatchRequest()
        {
            OdinId = odinId,
            QueryParams = new()
            {
                TargetDrive = file.TargetDrive,
                GlobalTransitId = [file.GlobalTransitId],
            },
            ResultOptionsRequest = new()
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        };
        
        var response = await this.GetBatch(request, fst);
        return response;
        
        // var client = factory.CreateHttpClient(identity, out var sharedSecret, fst);
        // var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerQuery>(client, sharedSecret);
        // var apiResponse = await svc.GetFileHeaderByGlobalTransitId(odinId, file.GlobalTransitId, file.TargetDrive.Alias, file.TargetDrive.Type);
        // return apiResponse;
    }

    public async Task<ApiResponse<HttpContent>> GetPayload(PeerGetPayloadRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fst);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerQuery>(client, sharedSecret);
        var apiResponse = await svc.GetPayload(request);
        return apiResponse;
    }

    public async Task<ApiResponse<HttpContent>> GetThumbnail(TransitGetThumbRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fst);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerQuery>(client, sharedSecret);
        var apiResponse = await svc.GetThumbnail(request);
        return apiResponse;
    }

    public async Task<ApiResponse<PagedResult<ClientDriveData>>> GetDrives(TransitGetDrivesByTypeRequest request, FileSystemType fst = FileSystemType.Standard)
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret, fst);
        var svc = RefitCreator.RestServiceFor<IUniversalRefitPeerQuery>(client, sharedSecret);
        var apiResponse = await svc.GetDrives(request);
        return apiResponse;
    }
}