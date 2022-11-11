using System.Threading.Tasks;
using Refit;
using Youverse.Hosting.Controllers;

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
        Task<ApiResponse<HostTransitResponse>> DeleteLinkedFile([Body]DeleteLinkedFileTransitRequest request);

        [Post("/api/perimeter/transit/host/querybatch")]
        Task<ApiResponse<HostTransitQueryBatchResponse>> QueryBatch([Body] QueryBatchRequest request);
    }
}