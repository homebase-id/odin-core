using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// The interface for querying from a host to another host
    /// </summary>
    public interface ITransitHostHttpClient
    {
        [Multipart]
        [Post("/api/perimeter/transit/host/stream")]
        Task<ApiResponse<HostTransitResponse>> SendHostToHost(
            StreamPart header,
            StreamPart metaData,
            StreamPart payload,
            params StreamPart[] thumbnail);

        [Post("/api/perimeter/transit/host/deletelinkedfile")]
        Task<ApiResponse<HostTransitResponse>> DeleteLinkedFile([Body] DeleteLinkedFileTransitRequest request);

        [Post("/api/perimeter/transit/host/querybatch")]
        Task<ApiResponse<QueryBatchResponse>> QueryBatch([Body] QueryBatchRequest request);

        [Post("/api/perimeter/transit/host/header")]
        Task<ApiResponse<ClientFileHeader>> GetFileHeader([Body] ExternalFileIdentifier file);

        [Post("/api/perimeter/transit/host/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnailStream([Body] GetThumbnailRequest request);

        [Post("/api/perimeter/transit/host/payload")]
        Task<ApiResponse<HttpContent>> GetPayloadStream([Body] ExternalFileIdentifier file);
    }
}