using System.Threading.Tasks;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Incoming;
using Refit;

namespace Odin.Core.Services.DataSubscription.SendingHost
{
    public interface IFeedDistributorHttpClient
    {
        private const string RootPath = PeerApiPathConstants.FeedV1;

        [Post(RootPath + "/filemetadata")]
        Task<ApiResponse<PeerResponse>> SendFeedFileMetadata([Body] UpdateFeedFileMetadataRequest request);
        
        [Post(RootPath + "/delete")]
        Task<ApiResponse<PeerResponse>> DeleteFeedMetadata([Body] DeleteFeedFileMetadataRequest request);
    }
}