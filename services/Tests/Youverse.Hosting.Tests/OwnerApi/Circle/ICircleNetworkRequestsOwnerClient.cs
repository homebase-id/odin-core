using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Tests.OwnerApi.Circle
{
    public interface ICircleNetworkRequestsOwnerClient
    {
        private const string RootPath = OwnerApiPathConstants.CirclesV1 + "/requests";
        private const string SentPathRoot = RootPath + "/sent";
        private const string PendingPathRoot = RootPath + "/pending";

        [Post(RootPath + "/sendrequest")]
        Task<ApiResponse<bool>> SendConnectionRequest([Body] ConnectionRequestHeader requestHeader);

        [Post(PendingPathRoot + "/accept")]
        Task<ApiResponse<bool>> AcceptConnectionRequest([Body] AcceptRequestHeader header);

        [Get(SentPathRoot + "/list")]
        Task<ApiResponse<PagedResult<ConnectionRequestResponse>>> GetSentRequestList([Query] PageOptions pageRequest);

        [Post(SentPathRoot+ "/single")]
        Task<ApiResponse<ConnectionRequestResponse>> GetSentRequest([Body] OdinIdRequest request);

        [Post(SentPathRoot + "/delete")]
        Task<ApiResponse<bool>> DeleteSentRequest([Body] OdinIdRequest request);

        [Get(PendingPathRoot + "/list")]
        Task<ApiResponse<PagedResult<ConnectionRequestResponse>>> GetPendingRequestList([Query] PageOptions pageRequest);

        [Post(PendingPathRoot+ "/single")]
        Task<ApiResponse<ConnectionRequestResponse>> GetPendingRequest([Body] OdinIdRequest request);

        [Post(PendingPathRoot + "/delete")]
        Task<ApiResponse<bool>> DeletePendingRequest([Body] OdinIdRequest request);
    }
}