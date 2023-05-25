using System.Collections.Generic;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Transit.ReceivingHost;

namespace Youverse.Core.Services.DataSubscription.SendingHost
{
    public interface IFeedDistributorHttpClient
    {
        private const string RootPath = "/api/perimeter/transit/host/feed";

        [Post(RootPath + "/filemetadata")]
        Task<ApiResponse<HostTransitResponse>> SendFeedFileMetadata(
            [HeaderCollection] IDictionary<string, string> httpHeaders,
            [Body] UpdateFeedFileMetadataRequest request);
        
    }
}