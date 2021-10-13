using System;
using System.Threading.Tasks;
using DotYou.Types.Circle;
using DotYou.Types.Messaging;
using Refit;

namespace DotYou.Types.ApiClient
{
    public interface ICircleNetworkConnectionsClient
    {
        private const string root_path = "/api/circlenetwork/connections";
        
        [Get(root_path + "/unblock/{dotYouId}")]
        Task<ApiResponse<bool>> Unblock(string dotYouId);

        
        [Get(root_path + "/block/{dotYouId}")]
        Task<ApiResponse<bool>> Block(string dotYouId);
        
        
        [Get(root_path + "/disconnect/{dotYouId}")]
        Task<ApiResponse<bool>> Disconnect(string dotYouId);

        [Get(root_path + "/status/{dotYouId}")]
        Task<ApiResponse<ConnectionInfo>> GetStatus(string dotYouId);

        [Get(root_path + "/connected")]
        Task<ApiResponse<PagedResult<DotYouProfile>>> GetConnectedProfiles(PageOptions pageRequest, bool connectedContactsOnly);

        [Get(root_path + "/blocked")]
        Task<ApiResponse<PagedResult<DotYouProfile>>> GetBlockedProfiles(PageOptions pageRequest, bool connectedContactsOnly);
        
    }
}
