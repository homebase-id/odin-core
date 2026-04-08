using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Peer.Incoming;
using Odin.Services.Peer;
using Refit;

namespace Odin.Services.DataSubscription.SendingHost
{
    public interface IFeedDistributorHttpClient
    {
        private const string RootPath = PeerApiPathConstants.FeedV1;

        [Post(RootPath + "/send-feed-filemetadata")]
        Task<ApiResponse<PeerTransferResponse>> SendFeedFileMetadata([Body] UpdateFeedFileMetadataRequest request, CancellationToken cancellationToken = default);

        [Post(RootPath + "/delete")]
        Task<ApiResponse<PeerTransferResponse>> DeleteFeedMetadata([Body] DeleteFeedFileMetadataRequest request, CancellationToken cancellationToken = default);
    }
}