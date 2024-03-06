using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.Connections.Requests;
using Odin.Hosting.Controllers;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner.CircleMembership
{
    public interface IRefitOwnerCircleNetworkRequests
    {
        private const string RootPath = OwnerApiPathConstants.CirclesV1 + "/requests";
        private const string SentPathRoot = RootPath + "/sent";
        private const string PendingPathRoot = RootPath + "/pending";

        [Post(RootPath + "/sendrequest")]
        Task<ApiResponse<HttpContent>> SendConnectionRequest([Body] ConnectionRequestHeader requestHeader);

        [Post(PendingPathRoot + "/accept")]
        Task<ApiResponse<HttpContent>> AcceptConnectionRequest([Body] AcceptRequestHeader header);

        [Get(SentPathRoot + "/list")]
        Task<ApiResponse<PagedResult<ConnectionRequestResponse>>> GetSentRequestList([Query] PageOptions pageRequest);

        [Post(SentPathRoot + "/single")]
        Task<ApiResponse<ConnectionRequestResponse>> GetSentRequest([Body] OdinIdRequest request);

        [Post(SentPathRoot + "/delete")]
        Task<ApiResponse<HttpContent>> DeleteSentRequest([Body] OdinIdRequest request);

        [Get(PendingPathRoot + "/list")]
        Task<ApiResponse<PagedResult<PendingConnectionRequestHeader>>> GetPendingRequestList([Query] PageOptions pageRequest);

        [Post(PendingPathRoot + "/single")]
        Task<ApiResponse<ConnectionRequestResponse>> GetPendingRequest([Body] OdinIdRequest request);

        [Post(PendingPathRoot + "/delete")]
        Task<ApiResponse<HttpContent>> DeletePendingRequest([Body] OdinIdRequest request);
    }
}