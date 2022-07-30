using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.ClientToken.Drive;

namespace Youverse.Hosting.Tests.DriveApi.YouAuth
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveTestHttpClientForYouAuth
    {
        private const string RootEndpoint = YouAuthApiPathConstants.DrivesV1;
        
        [Post(RootEndpoint + "/files/header")]
        Task<ApiResponse<ClientFileHeader>> GetFileHeader(ExternalFileIdentifier file);

        [Post(RootEndpoint + "/files/payload")]
        Task<ApiResponse<HttpContent>> GetPayload(ExternalFileIdentifier file);
        
        [Post(RootEndpoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnail(GetThumbnailRequest request);

        [Post(RootEndpoint + "/query/recent")]
        Task<ApiResponse<QueryModifiedResult>> GetModified(QueryModifiedRequest request);

        [Post(RootEndpoint + "/query/batch")]
        Task<ApiResponse<QueryBatchResponse>> GetBatch(QueryBatchRequest request);
    }
}