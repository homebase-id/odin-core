using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Transit.Audit;

namespace Youverse.Core.Services.Transit
{
    public interface ITransitClientToHostHttpClient
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

        [Post("/api/transit/background/outbox/process")]
        Task<ApiResponse<TransferResult>> ProcessOutbox();

        [Get(AuditRootEndpoint + "/recent")]
        Task<ApiResponse<PagedResult<TransitAuditEntry>>> GetRecentAuditEntries(int seconds, int pageNumber, int pageSize);
    }
}