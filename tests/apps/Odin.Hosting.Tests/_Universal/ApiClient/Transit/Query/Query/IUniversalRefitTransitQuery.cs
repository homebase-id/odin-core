using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Transit.Query.Query
{
    public interface IUniversalRefitTransitQuery
    {
        private const string RootEndpoint = "/transit/query";
            
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
}