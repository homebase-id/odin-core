using System;
using System.Threading.Tasks;
using Refit;
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
        Task<ApiResponse<object>> GetFile(Guid fileId);
        
    }
}