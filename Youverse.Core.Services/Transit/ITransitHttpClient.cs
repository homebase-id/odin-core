using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Transit.Audit;
using Youverse.Core.Services.Transit.Outbox;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// The interface for transit
    /// </summary>
    public interface ITransitHttpClient
    {
        private const string ClientRootEndpoint = "/api/transit/client";
        private const string AuditRootEndpoint = "/api/transit/audit";

        private const string OutboxRootEndPoint = ClientRootEndpoint + "/outbox";
        
        private const string TransitRootEndpoint = "/api/apps/v1";

        [Multipart]
        [Post(ClientRootEndpoint + "/sendpackage")]
        Task<ApiResponse<TransferResult>> SendFile(
            [AliasAs("tekh")] StreamPart transferEncryptedKeyHeader,
            [AliasAs("recipients")] StreamPart recipientList,
            [AliasAs("metaData")] StreamPart metaData,
            [AliasAs("payload")] StreamPart payload);

        
        [Multipart]
        [Post(TransitRootEndpoint + "/transit/upload")]
        Task<ApiResponse<TransferResult>> Upload(
            [AliasAs("instructions")] StreamPart instructionSet,
            [AliasAs("metaData")] StreamPart metaData,
            [AliasAs("payload")] StreamPart payload);
        
        
        [Post(OutboxRootEndPoint + "/processor/process")]
        Task<ApiResponse<bool>> ProcessOutbox();

        [Get(OutboxRootEndPoint)]
        Task<ApiResponse<PagedResult<OutboxItem>>> GetOutboxItems(int pageNumber, int pageSize);

        [Get(OutboxRootEndPoint + "/item")]
        Task<ApiResponse<OutboxItem>> GetOutboxItem(Guid id);

        [Delete(OutboxRootEndPoint + "/item")]
        Task<ApiResponse<bool>> RemoveOutboxItem(Guid id);

        [Put(OutboxRootEndPoint + "/item/priority")]
        Task<ApiResponse<bool>> UpdateOutboxItemPriority(Guid id, int priority);

        [Get(AuditRootEndpoint + "/recent")]
        Task<ApiResponse<PagedResult<TransitAuditEntry>>> GetRecentAuditEntries(int seconds, int pageNumber, int pageSize);
    }
}