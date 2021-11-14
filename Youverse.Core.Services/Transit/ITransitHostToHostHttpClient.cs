using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Transit.Quarantine;

namespace Youverse.Core.Services.Transit
{
    public interface ITransitHostToHostHttpClient
    {
        private const string HostRootEndpoint = "/api/perimeter/transit/host";

        [Multipart]
        [Post(HostRootEndpoint + "/stream")]
        Task<ApiResponse<CollectiveFilterResult>> SendHostToHost(
            [AliasAs("header")] EncryptedRecipientTransferKeyHeader header,
            [AliasAs("metaData")] StreamPart metaData,
            [AliasAs("payload")] StreamPart payload);


        [Get(HostRootEndpoint + "/tpk")]
        Task<ApiResponse<TransitPublicKey>> GetTransitPublicKey();
    }
}