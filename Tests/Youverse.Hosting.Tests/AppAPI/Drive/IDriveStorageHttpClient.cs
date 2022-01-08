using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Tests.AppAPI.Drive
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveStorageHttpClient
    {
        private const string DriveRootEndpoint = "/api/apps/v1/drive";

        [Get(DriveRootEndpoint + "/files")]
        Task<ApiResponse<ClientFileHeader>> GetFileHeader(Guid fileId);

        [Get(DriveRootEndpoint + "/files")]
        Task<ApiResponse<HttpContent>> GetPayload(Guid fileId);
    }
}