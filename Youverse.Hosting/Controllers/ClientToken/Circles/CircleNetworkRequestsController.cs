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

        [HttpGet("pending")]
        public async Task<PagedResult<ConnectionRequestResponse>> GetPendingRequests(int pageNumber, int pageSize)
        {
            var result = await _requestService.GetPendingRequests(new PageOptions(pageNumber, pageSize));
            var resp = result.Results.Select(ConnectionRequestResponse.FromConnectionRequest).ToList();
            return new PagedResult<ConnectionRequestResponse>(result.Request, result.TotalPages, resp);
        }

        [HttpGet("sent")]
        public async Task<PagedResult<ConnectionRequestResponse>> GetSentRequests(int pageNumber, int pageSize)
        {
            var result = await _requestService.GetSentRequests(new PageOptions(pageNumber, pageSize));

            var resp = result.Results.Select(ConnectionRequestResponse.FromConnectionRequest).ToList();
            return new PagedResult<ConnectionRequestResponse>(result.Request, result.TotalPages, resp);
        }
    }
}