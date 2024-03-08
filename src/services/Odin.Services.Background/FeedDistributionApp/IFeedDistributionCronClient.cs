using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Refit;

namespace Odin.Services.Background.FeedDistributionApp
{
    public interface IFeedDistributionCronClient
    {
        [Post($"{OwnerApiPathConstants.FollowersV1}/system/distribute/files")]
        Task<ApiResponse<bool>> DistributeFiles();
    }
}

