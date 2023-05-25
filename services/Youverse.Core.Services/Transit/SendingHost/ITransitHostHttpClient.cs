using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Core.Services.Transit.ReceivingHost;
using Youverse.Core.Services.Transit.ReceivingHost.Quarantine;

namespace Youverse.Core.Services.Transit.SendingHost
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
            [HeaderCollection] IDictionary<string, string> httpHeaders,
            StreamPart header,
            StreamPart metaData,
            params StreamPart[] additionalStreamParts);

        [Post(RootPath + "/deletelinkedfile")]
        Task<ApiResponse<HostTransitResponse>> DeleteLinkedFile(
            [HeaderCollection] IDictionary<string, string> httpHeaders,
            [Body] DeleteRemoteFileTransitRequest request);

        [Post(RootPath + "/querybatch")]
        Task<ApiResponse<QueryBatchResponse>> QueryBatch(
            [HeaderCollection] IDictionary<string, string> httpHeaders,
            [Body] QueryBatchRequest request);

        [Post(RootPath + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(
            [HeaderCollection] IDictionary<string, string> httpHeaders,
            [Body] ExternalFileIdentifier file);

        [Post(RootPath + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnailStream(
            [HeaderCollection] IDictionary<string, string> httpHeaders,
            [Body] GetThumbnailRequest request);

        [Post(RootPath + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayloadStream(
            [HeaderCollection] IDictionary<string, string> httpHeaders,
            [Body] GetPayloadRequest request);

        [Post(RootPath + "/metadata/type")]
        Task<ApiResponse<IEnumerable<PerimeterDriveData>>> GetDrives(
            [HeaderCollection] IDictionary<string, string> httpHeaders,
            [Body] GetDrivesByTypeRequest request);

        [Get(RootPath + "/security/context")]
        Task<ApiResponse<RedactedDotYouContext>> GetRemoteDotYouContext();

    }
}