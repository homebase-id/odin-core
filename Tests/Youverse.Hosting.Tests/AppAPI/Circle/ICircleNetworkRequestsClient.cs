using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Tests.AppAPI.Circle
{
    public interface ICircleNetworkRequestsClient
    {
        private const string RootPath = AppApiPathConstants.CirclesV1 + "/requests";
        private const string SentPathRoot = RootPath + "/sent";
        private const string PendingPathRoot = RootPath + "/pending";

        [Get(SentPathRoot)]
        Task<ApiResponse<PagedResult<ConnectionRequestResponse>>> GetSentRequestList([Query] PageOptions pageRequest);
        
        [Get(PendingPathRoot)]
        Task<ApiResponse<PagedResult<ConnectionRequestResponse>>> GetPendingRequestList([Query] PageOptions pageRequest);
    }
}