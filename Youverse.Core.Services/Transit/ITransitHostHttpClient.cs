using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Quarantine;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// The interface for querying from a host to another host
    /// </summary>
    public interface ITransitHostHttpClient
    {
        private const string RootPath = "/api/perimeter/transit/host";

        [Multipart]
        [Post(RootPath + "/stream")]
        Task<ApiResponse<HostTransitResponse>> SendHostToHost(
            StreamPart header,
            StreamPart metaData,
            StreamPart payload,
            params StreamPart[] thumbnail);

        [Post(RootPath + "/deletelinkedfile")]
        Task<ApiResponse<HostTransitResponse>> DeleteLinkedFile([Body] DeleteLinkedFileTransitRequest request);

        [Post(RootPath + "/querybatch")]
        Task<ApiResponse<QueryBatchResponse>> QueryBatch([Body] QueryBatchRequest request);

        [Post(RootPath + "/header")]
        Task<ApiResponse<ClientFileHeader>> GetFileHeader([Body] ExternalFileIdentifier file);

        [Post(RootPath + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnailStream([Body] GetThumbnailRequest request);

        [Post(RootPath + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayloadStream([Body] ExternalFileIdentifier file);

        [Post(RootPath + "/metadata/type")]
        Task<ApiResponse<IEnumerable<PerimeterDriveData>>> GetDrives([Body] GetDrivesByTypeRequest request);
    }
}