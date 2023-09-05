using System.Threading.Tasks;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.ReceivingHost;
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