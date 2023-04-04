using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Transit.ReceivingHost;

namespace Youverse.Core.Services.DataSubscription.SendingHost
{
    public interface IFeedDistributorHttpClient
    {
        private const string RootPath = "/api/perimeter/transit/host/feed";

        [Post(RootPath + "/filemetadata")]
        Task<ApiResponse<HostTransitResponse>> SendFeedFileMetadata([Body] UpdateFeedFileMetadataRequest request);
        
        [Post(RootPath + "/reactionpreview")]
        Task<ApiResponse<HostTransitResponse>> SendReactionPreview([Body] UpdateReactionSummaryRequest request);

    }
}