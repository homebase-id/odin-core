using System;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken.Drive;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Tests.OwnerApi.Transit
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface ITransitQueryHttpClientForOwner
    {
        private const string RootEndpoint = OwnerApiPathConstants.TransitQueryV1;

        // [Post(RootQueryEndpoint + "/modified")]
        // Task<ApiResponse<QueryModifiedResult>> GetModified(QueryModifiedRequest request);

        [Post(RootEndpoint + "/batch")]
        Task<ApiResponse<QueryBatchResponse>> GetBatch([Body] TransitQueryBatchRequest request);

        [Post(RootEndpoint + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader([Body] TransitExternalFileIdentifier file);

        [Post(RootEndpoint + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayload([Body] TransitExternalFileIdentifier file);

        [Post(RootEndpoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnail([Body] TransitGetThumbRequest request);
        
        [Post(RootEndpoint + "/metadata/type")]
        Task<ApiResponse<PagedResult<ClientDriveData>>> GetDrives([Body] TransitGetDrivesByTypeRequest request);
    }
}