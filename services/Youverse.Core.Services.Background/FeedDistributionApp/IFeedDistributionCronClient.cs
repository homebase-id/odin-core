using System.Threading.Tasks;
using Refit;

namespace Youverse.Core.Services.Workers.FeedDistributionApp
{
    public interface IFeedDistributionCronClient
    {
        [Post("/api/owner/v1/followers/system/distribute/reactionpreview")]
        Task<ApiResponse<bool>> DistributeReactionPreviewUpdates();
        
        [Post("/api/owner/v1/followers/system/distribute/files")]
        Task<ApiResponse<bool>> DistributeFiles();
    }
}

