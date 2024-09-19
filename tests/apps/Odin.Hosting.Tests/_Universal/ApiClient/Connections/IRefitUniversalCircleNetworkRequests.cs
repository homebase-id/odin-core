using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Hosting.Controllers;
using Odin.Services.Membership.Connections.Requests;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Connections
{
    public interface IRefitUniversalCircleNetworkRequests
    {
        private const string RootPath = "/circles/requests";
        private const string SentPathRoot = RootPath + "/sent";
        private const string PendingPathRoot = RootPath + "/pending";
        private const string IntroductionsRoot = RootPath + "/introductions";

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

        [Post(IntroductionsRoot + "/send-introductions")]
        Task<ApiResponse<IntroductionResult>> SendIntroductions([Body] IntroductionGroup group);
        
        [Post(IntroductionsRoot + "/process-incoming-introductions")]
        Task<ApiResponse<HttpContent>> ProcessIncomingIntroductions();
        
        [Post(IntroductionsRoot + "/auto-accept-eligible-introductions")]
        Task<ApiResponse<HttpContent>> AutoAcceptEligibleIntroductions();
            
        [Get(IntroductionsRoot + "/received")]
        Task<ApiResponse<List<IdentityIntroduction>>> GetReceivedIntroductions();
    }
}