using DotYou.Kernel.Services.TrustNetwork;
using DotYou.Types;
using DotYou.Types.TrustNetwork;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DotYou.TenantHost.Controllers
{

    [Route("api/trustnetwork/requests")]
    [ApiController]
    public class TrustNetworkRequestsController : ControllerBase
    {
        ITrustNetworkService _trustNetwork;

        public TrustNetworkRequestsController(ITrustNetworkService trustNetwork)
        {
            _trustNetwork = trustNetwork;
        }

        [HttpGet("pending")]
        //[Authorize(Policy = PolicyNames.MustOwnThisIdentity)]
        public async Task<PagedResult<ConnectionRequest>> GetPendingRequests(int pageNumber, int pageSize)
        {
            var result = await _trustNetwork.GetPendingRequests(new PageOptions(pageNumber, pageSize));
            return result;
        }

        [HttpGet("pending/{id}")]
        //[Authorize(Policy = PolicyNames.MustOwnThisIdentity)]
        public async Task<ConnectionRequest> GetPendingRequest(Guid id)
        {
            var result = await _trustNetwork.GetPendingRequest(id);
            return result;
        }

        [HttpGet("sent")]
        //[Authorize(Policy = PolicyNames.MustOwnThisIdentity)]
        public async Task<PagedResult<ConnectionRequest>> GetSentRequests(int pageNumber, int pageSize)
        {
            var result = await _trustNetwork.GetSentRequests(new PageOptions(pageNumber, pageSize));
            return result;
        }

        [HttpGet("sent/{id}")]
        //[Authorize(Policy = PolicyNames.MustOwnThisIdentity)]
        public async Task<ConnectionRequest> GetSentRequest(Guid id)
        {
            return await _trustNetwork.GetSentRequest(id);
        }

        [HttpPost("sent")]
        //[Authorize(Policy = PolicyNames.MustOwnThisIdentity)]
        public async Task<IActionResult> Send([FromBody] ConnectionRequest request)
        {
            await _trustNetwork.SendConnectionRequest(request);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpPost("pending/accept")]
        //[Authorize(Policy = PolicyNames.MustOwnThisIdentity)]
        public async Task<IActionResult> AcceptPending(Guid id)
        {
            await _trustNetwork.AcceptConnectionRequest(id);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpDelete("pending")]
        //[Authorize(Policy = PolicyNames.MustOwnThisIdentity)]
        public async Task<IActionResult> DeletePending(Guid id)
        {
            await _trustNetwork.DeletePendingRequest(id);
            return new JsonResult(new NoResultResponse(true));
        }

    }
}
