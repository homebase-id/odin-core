using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Apps;
using Refit;

namespace Odin.Hosting.Tests._Universal.V2.ApiClient
{
    public interface IDriveHttpClientApiV2
    {
        private const string RootStorageEndpoint = UnifiedApiRouteConstants.Files;

        [Get(RootStorageEndpoint + "/header")]
        Task<ApiResponse<SharedSecretEncryptedFileHeader>> GetFileHeader(Guid fileId, Guid driveId);

        [Get(RootStorageEndpoint + "/payload")]
        Task<ApiResponse<HttpContent>> GetPayload(Guid fileId, Guid driveId, string key, int start, int length);

        [Get(RootStorageEndpoint + "/thumb")]
        Task<ApiResponse<HttpContent>> GetThumbnail(Guid fileId, Guid driveId, string payloadKey, int width, int height);
    }
}