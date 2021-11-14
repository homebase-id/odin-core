using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Audit;

namespace Youverse.Hosting.Tests.Transit
{
    public interface ITransitTestHttpClient
    {
        private const string ClientRootEndpoint = "/api/transit/client";
        private const string AuditRootEndpoint = "/api/transit/audit";

        [Multipart]
        [Post(ClientRootEndpoint + "/sendpackage")]
        Task<ApiResponse<TransferResult>> SendClientToHost(
            [AliasAs("recipients")] RecipientList recipientList,
            [AliasAs("tekh")] string transferEncryptedKeyHeader64,
            [AliasAs("metaData")] StreamPart metaData,
            [AliasAs("payload")] StreamPart payload);


        [Get(AuditRootEndpoint + "/recent")]
        Task<ApiResponse<PagedResult<TransitAuditEntry>>> GetRecentAuditEntries(int seconds, int pageNumber, int pageSize);
    }
}