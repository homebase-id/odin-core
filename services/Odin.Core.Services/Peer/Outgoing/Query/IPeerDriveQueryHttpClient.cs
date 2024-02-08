using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Base;
using Odin.Core.Services.Base.SharedTypes;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Peer.Incoming.Drive.Query;
using Refit;

namespace Odin.Core.Services.Peer.Outgoing.Query
{
    /// <summary>
    /// The interface for querying from a host to another host
    /// </summary>
    public interface IPeerDriveQueryHttpClient
    {
        private const string DriveRoot = PeerApiPathConstants.DriveV1;

        [Post(DriveRoot + "/querybatch")]
        Task<ApiResponse<QueryBatchResponse>> QueryBatch([Body] QueryBatchRequest request);
        
        [Post(DriveRoot + "/batchcollection")]
        Task<ApiResponse<QueryBatchCollectionResponse>> QueryBatchCollection([Body] QueryBatchCollectionRequest request);
        
        [Post(DriveRoot + "/querymodified")]
        Task<ApiResponse<QueryModifiedResponse>> QueryModified([Body] QueryModifiedRequest request);

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

        [Post(DriveRoot + "/header_byglobaltransitid")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderByGlobalTransitId(GlobalTransitIdFileIdentifier file);
        
        [Post(DriveRoot + "/thumb_byglobaltransitid")]
        Task<ApiResponse<HttpContent>> GetThumbnailStreamByGlobalTransitId([Body] GetThumbnailByGlobalTransitIdRequest request);

        [Post(DriveRoot + "/payload_byglobaltransitid")]
        Task<ApiResponse<HttpContent>> GetPayloadStreamByGlobalTransitId([Body] GetPayloadByGlobalTransitIdRequest request);
        
        [Post(DriveRoot + "/header_byuniqueid")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderByUniqueId(GetPayloadByUniqueIdRequest request);
        
        [Post(DriveRoot + "/thumb_byuniqueid")]
        Task<ApiResponse<HttpContent>> GetThumbnailStreamByUniqueId([Body] GetThumbnailRequest request);

        [Post(DriveRoot + "/payload_byuniqueid")]
        Task<ApiResponse<HttpContent>> GetPayloadStreamByUniqueId([Body] GetPayloadRequest request);
        
    }
}