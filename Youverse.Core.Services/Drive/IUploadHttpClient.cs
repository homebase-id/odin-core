using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Transit;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IUploadHttpClient
    {
        private const string ClientRootEndpoint = "/api/owner/v1/storage";

        [Multipart]
        [Post(ClientRootEndpoint + "/store")]
        Task<ApiResponse<TransferResult>> Store(
            [AliasAs("tekh")] StreamPart transferEncryptedKeyHeader,
            [AliasAs("metaData")] StreamPart metaData,
            [AliasAs("payload")] StreamPart payload);

    }
}