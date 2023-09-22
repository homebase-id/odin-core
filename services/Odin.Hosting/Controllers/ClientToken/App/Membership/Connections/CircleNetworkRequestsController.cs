using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Controllers.ClientToken.App.Membership.Connections
{
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/requests")]
    [AuthorizeValidAppToken]
    public class CircleNetworkRequestsController : ControllerBase
    {
        readonly CircleNetworkRequestService _requestService;

        public CircleNetworkRequestsController(CircleNetworkRequestService cn)
        {
            _requestService = cn;
        }

        /// <summary>
        /// Gets a list of connection requests that are awaiting a response
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        [HttpGet("pending/list")]
        public async Task<PagedResult<PendingConnectionRequestHeader>> GetPendingRequestList(int pageNumber, int pageSize)
        {
            var result = await _requestService.GetPendingRequests(new PageOptions(pageNumber, pageSize));
            return result;
            // var resp = result.Results.Select(ConnectionRequestResponse.FromConnectionRequest).ToList();
            // return new PagedResult<ConnectionRequestResponse>(result.Request, result.TotalPages, resp);
        }

        /// <summary>
        /// Gets a list of sent connection requests
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        [HttpGet("sent/list")]
        public async Task<PagedResult<ConnectionRequestResponse>> GetSentRequestList(int pageNumber, int pageSize)
        {
            var result = await _requestService.GetSentRequests(new PageOptions(pageNumber, pageSize));
            var resp = result.Results.Select(ConnectionRequestResponse.FromConnectionRequest).ToList();
            return new PagedResult<ConnectionRequestResponse>(result.Request, result.TotalPages, resp);
        }
    }
}