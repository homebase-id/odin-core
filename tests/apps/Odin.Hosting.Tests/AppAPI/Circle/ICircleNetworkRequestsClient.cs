using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Membership.Connections.Requests;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.ClientToken;
using Odin.Hosting.Controllers.ClientToken.App;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.Circle
{
    public interface ICircleNetworkRequestsClient
    {
        private const string RootPath = AppApiPathConstantsV1.CirclesV1 + "/requests";
        private const string SentPathRoot = RootPath + "/sent/list";
        private const string PendingPathRoot = RootPath + "/pending/list";

        [Get(SentPathRoot)]
        Task<ApiResponse<PagedResult<ConnectionRequestResponse>>> GetSentRequestList([Query] PageOptions pageRequest);
        
        [Get(PendingPathRoot)]
        Task<ApiResponse<PagedResult<PendingConnectionRequestHeader>>> GetPendingRequestList([Query] PageOptions pageRequest);
    }
}