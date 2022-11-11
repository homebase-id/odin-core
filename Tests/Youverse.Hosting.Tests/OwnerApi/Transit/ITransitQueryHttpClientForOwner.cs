using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Tests.OwnerApi.Transit
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface ITransitQueryHttpClientForOwner
    {
        private const string RootEndpoint = OwnerApiPathConstants.TransitQueryV1;

        // [Post(RootQueryEndpoint + "/modified")]
        // Task<ApiResponse<QueryModifiedResult>> GetModified(QueryModifiedRequest request);

        [Post(RootEndpoint + "/batch")]
        Task<ApiResponse<QueryBatchResponse>> GetBatch(TransitQueryBatchRequest request);
        
    }
}