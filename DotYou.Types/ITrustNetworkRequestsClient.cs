using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DotYou.Types.TrustNetwork;
using Refit;

namespace DotYou.Types
{
    public interface ITrustNetworkRequestsClient
    {
        private const string root_path = "/api/trustnetwork/requests";
        private const string sent_path_root = root_path + "/sent";
        private const string pending_path_root = root_path + "/pending";

        [Post(sent_path_root)]
        Task<ApiResponse<bool>> SendConnectionRequest([Body] ConnectionRequest request);

        [Get(pending_path_root + "/accept/{id}")]
        Task AcceptConnectionRequest(Guid id);

        [Get(sent_path_root)]
        Task<PagedResult<ConnectionRequest>> GetSentRequests(PageOptions pageRequest);

        [Get(sent_path_root + "/{id}")]
        Task<ConnectionRequest> GetSentRequest(Guid id);

        [Get(pending_path_root)]
        Task<PagedResult<ConnectionRequest>> GetPendingRequests(PageOptions pageRequest);

        [Get(pending_path_root + "/{id}")]
        Task<ApiResponse<ConnectionRequest>> GetPendingRequest(Guid id);

        [Delete(pending_path_root + "/{id}")]
        Task DeletePendingRequest(Guid id);
    }
}
