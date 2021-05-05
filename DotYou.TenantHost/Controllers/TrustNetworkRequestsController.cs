using System;
using System.Net;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.TrustNetwork;
using DotYou.Types;
using DotYou.Types.TrustNetwork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.TenantHost.Controllers
{

    [ApiController]
    [Route("api/trustnetwork/requests")]
    [Authorize(Policy = DotYouPolicyNames.MustOwnThisIdentity)]
    public class TrustNetworkRequestsController : ControllerBase
    {
        ITrustNetworkService _trustNetwork;

        public TrustNetworkRequestsController(ITrustNetworkService trustNetwork)
        {
            _trustNetwork = trustNetwork;
        }

        [HttpGet("pending")]
        public async Task<PagedResult<ConnectionRequest>> GetPendingRequests(int pageNumber, int pageSize)
        {
            var result = await _trustNetwork.GetPendingRequests(new PageOptions(pageNumber, pageSize));
            return result;
        }

        [HttpGet("pending/{id}")]
        public async Task<IActionResult> GetPendingRequest(Guid id)
        {
            var result = await _trustNetwork.GetPendingRequest(id);

            if (result == null)
            {
                return new JsonResult(new NoResultResponse(true))
                {
                    StatusCode = (int)HttpStatusCode.NotFound
                };
            }

            return new JsonResult(result);
        }

        [HttpGet("sent")]
        public async Task<PagedResult<ConnectionRequest>> GetSentRequests(int pageNumber, int pageSize)
        {
            var result = await _trustNetwork.GetSentRequests(new PageOptions(pageNumber, pageSize));
            return result;
        }

        [HttpGet("sent/{id}")]
        public async Task<IActionResult> GetSentRequest(Guid id)
        {
            var result = await _trustNetwork.GetSentRequest(id);
            if (result == null)
            {
                return new JsonResult(new NoResultResponse(true))
                {
                    StatusCode = (int)HttpStatusCode.NotFound
                };
            }

            return new JsonResult(result);
        }

        [HttpPost("sent")]
        public async Task<IActionResult> Send([FromBody] ConnectionRequest request)
        {
            await _trustNetwork.SendConnectionRequest(request);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpPost("pending/accept/{id}")]
        public async Task<IActionResult> AcceptPending(Guid id)
        {
            await _trustNetwork.AcceptConnectionRequest(id);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpDelete("pending/{id}")]
        public async Task<IActionResult> DeletePending(Guid id)
        {
            await _trustNetwork.DeletePendingRequest(id);
            return new JsonResult(new NoResultResponse(true));
        }

    }
}
