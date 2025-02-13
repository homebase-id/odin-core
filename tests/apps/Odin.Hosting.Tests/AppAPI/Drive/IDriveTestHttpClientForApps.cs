using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Apps;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Services.Peer;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.ClientToken;
using Odin.Hosting.Controllers.ClientToken.App;
using Refit;
using QueryModifiedRequest = Odin.Services.Drives.QueryModifiedRequest;

namespace Odin.Hosting.Tests.AppAPI.Drive
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveTestHttpClientForApps
    {
        private const string RootEndpoint = AppApiPathConstants.DriveV1;

        [Multipart]
        [Post(RootEndpoint + "/files/upload")]
        Task<ApiResponse<UploadResult>> Upload(StreamPart instructionSet, StreamPart metaData, StreamPart payload, params StreamPart[] thumbnail);

        [Multipart]
        [Post(RootEndpoint + "/files/upload")]
        Task<ApiResponse<UploadResult>> Upload(StreamPart[] streamdata);
        
        
        [Post(RootEndpoint + "/files/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeaderAsPost(ExternalFileIdentifier file);

        [Post(RootEndpoint + "/files/payload")]
        Task<ApiResponse<HttpContent>> GetPayloadAsPost(GetPayloadRequest request);

        [Post(RootEndpoint + "/files/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnailAsPost(GetThumbnailRequest request);

        [Get(RootEndpoint + "/files/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnail(Guid fileId, Guid alias, Guid type, int width, int height);

        [Get(RootEndpoint + "/files/payload")]
        Task<ApiResponse<HttpContent>> GetPayload(Guid fileId, Guid alias, Guid type, long? offsetPosition);

        [Get(RootEndpoint + "/files/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(Guid fileId, Guid alias, Guid type);

        [Post(RootEndpoint + "/query/modified")]
        Task<ApiResponse<QueryModifiedResult>> GetModified(QueryModifiedRequest request);

        [Post(RootEndpoint + "/query/batch")]
        Task<ApiResponse<QueryBatchResponse>> GetBatch(QueryBatchRequest request);
        
        [Post(RootEndpoint + "/query/batchcollection")]
        Task<ApiResponse<QueryBatchCollectionResponse>> QueryBatchCollection(QueryBatchCollectionRequest request);

        [Post(RootEndpoint + "/files/delete")]
        Task<ApiResponse<DeleteFileResult>> DeleteFile([Body] DeleteFileRequest request);
        
    }
}