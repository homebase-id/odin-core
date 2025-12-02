using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Transit.Query
{
    public interface IRefitAppTransitQuery
    {
        private const string RootEndpoint = AppApiPathConstantsV1.PeerQueryV1;

            
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