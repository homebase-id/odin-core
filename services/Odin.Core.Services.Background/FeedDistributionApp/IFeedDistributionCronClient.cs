using System.Threading.Tasks;
using Refit;

namespace Odin.Core.Services.Background.FeedDistributionApp
{
    public interface IFeedDistributionCronClient
    {
        [Post("/api/owner/v1/followers/system/distribute/files")]
        Task<ApiResponse<bool>> DistributeFiles();
    }
}

