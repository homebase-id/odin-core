using System;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.Connections.Requests;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.ClientToken.App;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Membership.Connections
{
    [Obsolete]
    public interface IAppCircleNetworkRequestsClient
    {
        private const string RootPath = AppApiPathConstants.CirclesV1 + "/requests";
        private const string SentPathRoot = RootPath + "/sent";
        private const string PendingPathRoot = RootPath + "/pending";

        [Post(RootPath + "/sendrequest")]
        Task<ApiResponse<bool>> SendConnectionRequest([Body] ConnectionRequestHeader requestHeader);

        [Post(PendingPathRoot + "/accept")]
        Task<ApiResponse<bool>> AcceptConnectionRequest([Body] AcceptRequestHeader header);

        [Get(SentPathRoot + "/list")]
        Task<ApiResponse<PagedResult<ConnectionRequestResponse>>> GetSentRequestList([Query] PageOptions pageRequest);

        [Post(SentPathRoot + "/single")]
        Task<ApiResponse<ConnectionRequestResponse>> GetSentRequest([Body] OdinIdRequest request);

        [Post(SentPathRoot + "/delete")]
        Task<ApiResponse<bool>> DeleteSentRequest([Body] OdinIdRequest request);

        [Get(PendingPathRoot + "/list")]
        Task<ApiResponse<PagedResult<PendingConnectionRequestHeader>>> GetPendingRequestList([Query] PageOptions pageRequest);

        [Post(PendingPathRoot + "/single")]
        Task<ApiResponse<ConnectionRequestResponse>> GetPendingRequest([Body] OdinIdRequest request);

        [Post(PendingPathRoot + "/delete")]
        Task<ApiResponse<bool>> DeletePendingRequest([Body] OdinIdRequest request);
    }
}