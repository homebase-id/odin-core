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

        /// <summary>
        /// Stores a file using the drive associated with the App
        /// </summary>
        /// <returns></returns>
        [Multipart]
        [Post(DriveRootEndpoint + "/store")]
        Task<ApiResponse<DriveFileId>> StoreUsingAppDrive(
            [AliasAs("tekh")] StreamPart transferEncryptedKeyHeader,
            [AliasAs("metaData")] StreamPart metaData,
            [AliasAs("payload")] StreamPart payload);

        [Get(DriveRootEndpoint + "/files")]
        Task<ApiResponse<object>> GetFile(Guid fileId);
        
    }
}