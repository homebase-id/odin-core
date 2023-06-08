using System.Threading.Tasks;
using Odin.Core.Services.Transit.ReceivingHost;
using Refit;

namespace Odin.Core.Services.DataSubscription.SendingHost
{
    public interface IFeedDistributorHttpClient
    {
        private const string RootPath = "/api/perimeter/transit/host/feed";

        [Post(RootPath + "/filemetadata")]
        Task<ApiResponse<HostTransitResponse>> SendFeedFileMetadata([Body] UpdateFeedFileMetadataRequest request);
        
    }
}