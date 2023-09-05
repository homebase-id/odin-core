using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Peer.ReceivingHost;
using Odin.Core.Services.Peer.ReceivingHost.Quarantine;
using Refit;

namespace Odin.Core.Services.Peer.SendingHost
{
    /// <summary>
    /// The interface for querying from a host to another host
    /// </summary>
    public interface IPeerHostHttpClient
    {
        private const string DriveRoot = PeerApiPathConstants.DriveV1;

        [Multipart]
        [Post(DriveRoot + "/upload")]
        Task<ApiResponse<HostTransitResponse>> SendHostToHost(
            StreamPart header,
            StreamPart metaData,
            params StreamPart[] additionalStreamParts);

        [Post(DriveRoot + "/deletelinkedfile")]
        Task<ApiResponse<HostTransitResponse>> DeleteLinkedFile([Body] DeleteRemoteFileTransitRequest request);

        [Post(DriveRoot + "/querybatch")]
        Task<ApiResponse<QueryBatchResponse>> QueryBatch([Body] QueryBatchRequest request);

        [Post(DriveRoot + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader([Body] ExternalFileIdentifier file);

        [Post(DriveRoot + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnailStream([Body] GetThumbnailRequest request);

        [Post(DriveRoot + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayloadStream([Body] GetPayloadRequest request);

        [Post(DriveRoot + "/metadata/type")]
        Task<ApiResponse<IEnumerable<PerimeterDriveData>>> GetDrives([Body] GetDrivesByTypeRequest request);

        [Get(PeerApiPathConstants.SecurityV1 + "/context")]
        Task<ApiResponse<RedactedOdinContext>> GetRemoteDotYouContext();

    }
}