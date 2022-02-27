using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.Apps;

namespace Youverse.Hosting.Tests.AppAPI.Drive
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveStorageHttpClient
    {
        private const string RootEndpoint = AppApiPathConstants.DrivesV1;
        
        [Get(RootEndpoint + "/files/header")]
        Task<ApiResponse<ClientFileHeader>> GetFileHeader(Guid driveIdentifier, Guid fileId);

        [Get(RootEndpoint + "/files/payload")]
        Task<ApiResponse<HttpContent>> GetPayload(Guid driveIdentifier, Guid fileId);
        
        [Get(RootEndpoint + "/temp/files/header")]
        Task<ApiResponse<ClientFileHeader>> GetTempFileHeader(Guid driveIdentifier, Guid fileId);

        [Get(RootEndpoint + "/temp/files/payload")]
        Task<ApiResponse<HttpContent>> GetTempPayload(Guid driveIdentifier, Guid fileId);
    }
}