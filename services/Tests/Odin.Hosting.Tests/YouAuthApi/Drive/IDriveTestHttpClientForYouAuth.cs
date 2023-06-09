using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Transit;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Anonymous;
using Refit;

namespace Odin.Hosting.Tests.YouAuthApi.Drive
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveTestHttpClientForYouAuth
    {
        private const string RootEndpoint = YouAuthApiPathConstants.DrivesV1;
        
        [Post(RootEndpoint + "/files/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(ExternalFileIdentifier file);

        [Post(RootEndpoint + "/files/payload")]
        Task<ApiResponse<HttpContent>> GetPayload(GetPayloadRequest request);
        
        [Post(RootEndpoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnail(GetThumbnailRequest request);

        [Post(RootEndpoint + "/query/modified")]
        Task<ApiResponse<QueryModifiedResult>> GetModified(QueryModifiedRequest request);

        [Post(RootEndpoint + "/query/batch")]
        Task<ApiResponse<QueryBatchResponse>> GetBatch(QueryBatchRequest request);
    }
}