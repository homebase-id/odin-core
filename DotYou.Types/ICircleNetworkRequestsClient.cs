using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DotYou.Types.Circle;
using Refit;

namespace DotYou.Types
{
    public interface ICircleNetworkRequestsClient
    {
        private const string RootPath = "/api/circlenetwork/requests";
        private const string SentPathRoot = RootPath + "/sent";
        private const string PendingPathRoot = RootPath + "/pending";

        [Post(SentPathRoot)]
        Task<ApiResponse<NoResultResponse>> SendConnectionRequest([Body] ConnectionRequest request);

        [Post(PendingPathRoot + "/accept/{id}")]
        Task<ApiResponse<NoResultResponse>> AcceptConnectionRequest(Guid id);

        [Get(SentPathRoot)]
        Task<ApiResponse<PagedResult<ConnectionRequest>>> GetSentRequestList(PageOptions pageRequest);

        [Get(SentPathRoot + "/{id}")]
        Task<ApiResponse<ConnectionRequest>> GetSentRequest(Guid id);

        [Get(PendingPathRoot)]
        Task<ApiResponse<PagedResult<ConnectionRequest>>> GetPendingRequestList([Query]PageOptions pageRequest);

        [Get(PendingPathRoot + "/{id}")]
        Task<ApiResponse<ConnectionRequest>> GetPendingRequest(Guid id);

        [Delete(PendingPathRoot + "/{id}")]
        Task<ApiResponse<NoResultResponse>> DeletePendingRequest(Guid id);
    }
}
