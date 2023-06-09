using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Transit.ReceivingHost;
using Odin.Core.Services.Transit.ReceivingHost.Quarantine;
using Refit;

namespace Odin.Core.Services.Transit.SendingHost
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
            params StreamPart[] additionalStreamParts);

        [Post(RootPath + "/deletelinkedfile")]
        Task<ApiResponse<HostTransitResponse>> DeleteLinkedFile([Body] DeleteRemoteFileTransitRequest request);

        [Post(RootPath + "/querybatch")]
        Task<ApiResponse<QueryBatchResponse>> QueryBatch([Body] QueryBatchRequest request);

        [Post(RootPath + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader([Body] ExternalFileIdentifier file);

        [Post(RootPath + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnailStream([Body] GetThumbnailRequest request);

        [Post(RootPath + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayloadStream([Body] GetPayloadRequest request);

        [Post(RootPath + "/metadata/type")]
        Task<ApiResponse<IEnumerable<PerimeterDriveData>>> GetDrives([Body] GetDrivesByTypeRequest request);

        [Get(RootPath + "/security/context")]
        Task<ApiResponse<RedactedOdinContext>> GetRemoteDotYouContext();

    }
}