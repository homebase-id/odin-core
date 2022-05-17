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
        private const string HostRootEndpoint = "/api/perimeter/transit/host";

        [Multipart]
        [Post(HostRootEndpoint + "/stream")]
        Task<ApiResponse<HostTransferResponse>> SendHostToHost(
            [AliasAs("header")] StreamPart header,
            [AliasAs("metaData")] StreamPart metaData,
            [AliasAs("payload")] StreamPart payload);
        
    }
}