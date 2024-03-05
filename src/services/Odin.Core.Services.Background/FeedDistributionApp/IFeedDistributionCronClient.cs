using System.Threading.Tasks;
using Odin.Core.Services.Authentication.Owner;
using Refit;

namespace Odin.Core.Services.Background.FeedDistributionApp
{
    public interface IFeedDistributionCronClient
    {
        [Post($"{OwnerApiPathConstants.FollowersV1}/system/distribute/files")]
        Task<ApiResponse<bool>> DistributeFiles();
    }
}

