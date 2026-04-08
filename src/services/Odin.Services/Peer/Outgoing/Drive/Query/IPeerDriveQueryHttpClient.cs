using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Peer.Incoming.Drive.Query;
using Refit;

namespace Odin.Services.Peer.Outgoing.Drive.Query
{
    /// <summary>
    /// The interface for querying from a host to another host
    /// </summary>
    public interface IPeerDriveQueryHttpClient
    {
        private const string DriveRoot = PeerApiPathConstants.DriveV1;

        [Post(DriveRoot + "/querybatch")]
        Task<ApiResponse<QueryBatchResponse>> QueryBatch([Body] QueryBatchRequest request, CancellationToken cancellationToken = default);

        [Post(DriveRoot + "/batchcollection")]
        Task<ApiResponse<QueryBatchCollectionResponse>> QueryBatchCollection([Body] QueryBatchCollectionRequest request, CancellationToken cancellationToken = default);

        [Post(DriveRoot + "/querymodified")]
        Task<ApiResponse<QueryModifiedResponse>> QueryModified([Body] QueryModifiedRequest request, CancellationToken cancellationToken = default);

        [Post(DriveRoot + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader([Body] ExternalFileIdentifier file, CancellationToken cancellationToken = default);

        [Post(DriveRoot + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnailStream([Body] GetThumbnailRequest request, CancellationToken cancellationToken = default);

        [Post(DriveRoot + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayloadStream([Body] GetPayloadRequest request, CancellationToken cancellationToken = default);

        [Post(DriveRoot + "/metadata/type")]
        Task<ApiResponse<IEnumerable<PerimeterDriveData>>> GetDrives([Body] GetDrivesByTypeRequest request, CancellationToken cancellationToken = default);

        [Get(PeerApiPathConstants.SecurityV1 + "/context")]
        Task<ApiResponse<RedactedOdinContext>> GetRemoteDotYouContext(CancellationToken cancellationToken = default);

        [Post(DriveRoot + "/header_byglobaltransitid")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderByGlobalTransitId(GlobalTransitIdFileIdentifier file, CancellationToken cancellationToken = default);

        [Post(DriveRoot + "/thumb_byglobaltransitid")]
        Task<ApiResponse<HttpContent>> GetThumbnailStreamByGlobalTransitId([Body] GetThumbnailByGlobalTransitIdRequest request, CancellationToken cancellationToken = default);

        [Post(DriveRoot + "/payload_byglobaltransitid")]
        Task<ApiResponse<HttpContent>> GetPayloadStreamByGlobalTransitId([Body] GetPayloadByGlobalTransitIdRequest request, CancellationToken cancellationToken = default);

        [Post(DriveRoot + "/header_byuniqueid")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderByUniqueId(GetPayloadByUniqueIdRequest request, CancellationToken cancellationToken = default);

        [Post(DriveRoot + "/thumb_byuniqueid")]
        Task<ApiResponse<HttpContent>> GetThumbnailStreamByUniqueId([Body] GetThumbnailRequest request, CancellationToken cancellationToken = default);

        [Post(DriveRoot + "/payload_byuniqueid")]
        Task<ApiResponse<HttpContent>> GetPayloadStreamByUniqueId([Body] GetPayloadRequest request, CancellationToken cancellationToken = default);

    }
}