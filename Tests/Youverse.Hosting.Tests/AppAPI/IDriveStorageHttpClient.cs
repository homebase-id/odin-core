using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;

namespace Youverse.Hosting.Tests.AppAPI
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveStorageHttpClient
    {
        private const string ClientRootEndpoint = "/api/apps/v1/drive";

        /// <summary>
        /// Stores a file using the drive associated with the App
        /// </summary>
        /// <returns></returns>
        [Multipart]
        [Post(ClientRootEndpoint + "/store")]
        Task<ApiResponse<DriveFileId>> StoreUsingAppDrive(
            [AliasAs("tekh")] StreamPart transferEncryptedKeyHeader,
            [AliasAs("metaData")] StreamPart metaData,
            [AliasAs("payload")] StreamPart payload);

    }
}