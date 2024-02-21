using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Transit.Query;

public interface IRefitOwnerTransitQuery
{
    private const string RootEndpoint = OwnerApiPathConstants.TransitQueryV1;


    [Post(RootEndpoint + "/modified")]
    Task<ApiResponse<QueryModifiedResponse>> GetModified(PeerQueryModifiedRequest request);

    [Post(RootEndpoint + "/batchcollection")]
    Task<ApiResponse<QueryBatchCollectionResponse>> GetBatchCollection([Body] PeerQueryBatchCollectionRequest request);
        
    [Post(RootEndpoint + "/batch")]
    Task<ApiResponse<QueryBatchResponse>> GetBatch([Body] PeerQueryBatchRequest request);

    [Post(RootEndpoint + "/header")]
    Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader([Body] TransitExternalFileIdentifier file);

    [Post(RootEndpoint + "/payload")]
    Task<ApiResponse<HttpContent>> GetPayload([Body] TransitGetPayloadRequest file);

    [Post(RootEndpoint + "/thumb")]
    Task<ApiResponse<HttpContent>> GetThumbnail([Body] TransitGetThumbRequest request);
        
    [Post(RootEndpoint + "/metadata/type")]
    Task<ApiResponse<PagedResult<ClientDriveData>>> GetDrives([Body] TransitGetDrivesByTypeRequest request);

    [Post(RootEndpoint + "/security/context")]
    Task<ApiResponse<RedactedOdinContext>> GetRemoteDotYouContext(TransitGetSecurityContextRequest request);
}