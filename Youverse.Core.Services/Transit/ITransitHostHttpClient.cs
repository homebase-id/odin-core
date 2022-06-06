using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Quarantine;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// The interface for querying from a host to another host
    /// </summary>
    public interface ITransitHostHttpClient
    {
        [Multipart]
        [Post("/api/perimeter/transit/host/stream")]
        Task<ApiResponse<HostTransferResponse>> SendHostToHost(
            [AliasAs("header")] StreamPart header,
            [AliasAs("metaData")] StreamPart metaData,
            [AliasAs("payload")] StreamPart payload);
    }
}