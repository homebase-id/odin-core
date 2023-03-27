using System.Threading.Tasks;
using Refit;

namespace Youverse.Core.Services.Workers.FeedDistributionApp
{
    public interface IFeedDistributionClient
    {
        [Post("/api/owner/v1/followers/system/distribute")]
        Task<ApiResponse<bool>> DistributeQueuedItems();
    }
}

