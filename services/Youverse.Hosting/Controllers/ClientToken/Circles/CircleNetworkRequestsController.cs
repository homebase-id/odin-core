using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Controllers.ClientToken.Circles
{
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/requests")]
    [AuthorizeValidAppExchangeGrant]
    public class CircleNetworkRequestsController : ControllerBase
    {
        readonly ICircleNetworkRequestService _requestService;

        public CircleNetworkRequestsController(ICircleNetworkRequestService cn)
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
        public async Task<PagedResult<ConnectionRequestResponse>> GetPendingRequestList(int pageNumber, int pageSize)
        {
            var result = await _requestService.GetPendingRequests(new PageOptions(pageNumber, pageSize));
            var resp = result.Results.Select(ConnectionRequestResponse.FromConnectionRequest).ToList();
            return new PagedResult<ConnectionRequestResponse>(result.Request, result.TotalPages, resp);
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