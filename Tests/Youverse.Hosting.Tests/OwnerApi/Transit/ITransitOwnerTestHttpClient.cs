using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Outbox;
using Youverse.Hosting.Controllers.Owner;

namespace Youverse.Hosting.Tests.OwnerApi.Transit
{
    /// <summary>
    /// The interface for transit
    /// </summary>
    public interface ITransitOwnerTestHttpClient
    {
        private const string ClientRootEndpoint = "/api/transit/client";
        private const string OutboxRootEndPoint = ClientRootEndpoint + "/outbox";
        private const string TransitRootEndpoint = OwnerApiPathConstants.TransitV1;
        
        
        [Multipart]
        [Post(TransitRootEndpoint + "/upload")]
        Task<ApiResponse<UploadResult>> Upload(
            [AliasAs("instructions")] StreamPart instructionSet,
            [AliasAs("metaData")] StreamPart metaData,
            [AliasAs("payload")] StreamPart payload);
        
        
        [Post(TransitRootEndpoint + "/outbox/processor/process")]
        Task<ApiResponse<bool>> ProcessOutbox();

        [Get(OutboxRootEndPoint)]
        Task<ApiResponse<PagedResult<OutboxItem>>> GetOutboxItems(int pageNumber, int pageSize);
    }
}