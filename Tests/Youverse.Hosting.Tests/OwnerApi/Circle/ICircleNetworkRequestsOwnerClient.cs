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

        [Post(SentPathRoot)]
        Task<ApiResponse<NoResultResponse>> SendConnectionRequest([Body] ConnectionRequestHeader requestHeader);

        [Post(PendingPathRoot + "/accept")]
        Task<ApiResponse<NoResultResponse>> AcceptConnectionRequest([Body]AcceptRequestHeader header);

        [Get(SentPathRoot)]
        Task<ApiResponse<PagedResult<ConnectionRequestResponse>>> GetSentRequestList([Query] PageOptions pageRequest);

        [Get(SentPathRoot + "/{recipientDotYouId}")]
        Task<ApiResponse<ConnectionRequestResponse>> GetSentRequest(string recipientDotYouId);
        
        [Delete(SentPathRoot + "/{recipientDotYouId}")]
        Task<ApiResponse<NoResultResponse>> DeleteSentRequest(string recipientDotYouId);

        [Get(PendingPathRoot)]
        Task<ApiResponse<PagedResult<ConnectionRequestResponse>>> GetPendingRequestList([Query] PageOptions pageRequest);

        [Get(PendingPathRoot + "/{senderDotYouId}")]
        Task<ApiResponse<ConnectionRequestResponse>> GetPendingRequest(string senderDotYouId);

        [Delete(PendingPathRoot + "/{senderDotYouId}")]
        Task<ApiResponse<NoResultResponse>> DeletePendingRequest(string senderDotYouId);
    }
}