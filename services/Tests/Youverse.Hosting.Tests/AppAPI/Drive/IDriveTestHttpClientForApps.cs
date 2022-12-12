using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken;
using Youverse.Hosting.Controllers.ClientToken.Drive;
using Youverse.Hosting.Controllers.OwnerToken.Drive;

namespace Youverse.Hosting.Tests.AppAPI.Drive
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveTestHttpClientForApps
    {
        private const string RootEndpoint = AppApiPathConstants.DrivesV1;

        [Multipart]
        [Post(RootEndpoint + "/files/upload")]
        Task<ApiResponse<UploadResult>> Upload(StreamPart instructionSet, StreamPart metaData, StreamPart payload, params StreamPart[] thumbnail);

        [Post(RootEndpoint + "/files/header")]
        Task<ApiResponse<ClientFileHeader>> GetFileHeader(ExternalFileIdentifier file);

        [Post(RootEndpoint + "/files/payload")]
        Task<ApiResponse<HttpContent>> GetPayload(ExternalFileIdentifier file);

        [Post(RootEndpoint + "/files/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnail(GetThumbnailRequest request);

        [Post(RootEndpoint + "/query/modified")]
        Task<ApiResponse<QueryModifiedResult>> QueryModified(QueryModifiedRequest request);

        [Post(RootEndpoint + "/query/batch")]
        Task<ApiResponse<QueryBatchResponse>> QueryBatch(QueryBatchRequest request);

        [Post(RootEndpoint + "/files/delete")]
        Task<ApiResponse<DeleteLinkedFileResult>> DeleteFile([Body] DeleteFileRequest file);
    }
}