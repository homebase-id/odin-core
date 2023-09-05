using System.Threading.Tasks;
using Odin.Core.Services.Transit;
using Odin.Core.Services.Transit.ReceivingHost;
using Refit;

namespace Odin.Core.Services.DataSubscription.SendingHost
{
    public interface IFeedDistributorHttpClient
    {
        private const string RootPath = PeerApiPathConstants.FeedV1;

        [Post(RootPath + "/filemetadata")]
        Task<ApiResponse<HostTransitResponse>> SendFeedFileMetadata([Body] UpdateFeedFileMetadataRequest request);
        
    }
}