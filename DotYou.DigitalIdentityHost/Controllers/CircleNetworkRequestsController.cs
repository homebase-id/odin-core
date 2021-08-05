using System;
using System.Net;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Circle;
using DotYou.Types;
using DotYou.Types.Circle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.TenantHost.Controllers
{

    [ApiController]
    [Route("api/circlenetwork/requests")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner)]
    public class CircleNetworkRequestsController : ControllerBase
    {
        readonly ICircleNetworkService _circleNetwork;

        public CircleNetworkRequestsController(ICircleNetworkService cn)
        {
            _circleNetwork = cn;
        }

        [HttpGet("pending")]
        public async Task<PagedResult<ConnectionRequest>> GetPendingRequests(int pageNumber, int pageSize)
        {
            var result = await _circleNetwork.GetPendingRequests(new PageOptions(pageNumber, pageSize));
            return result;
        }

        [HttpGet("pending/{id}")]
        public async Task<IActionResult> GetPendingRequest(Guid id)
        {
            var result = await _circleNetwork.GetPendingRequest(id);

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
            var result = await _circleNetwork.GetSentRequests(new PageOptions(pageNumber, pageSize));
            return result;
        }

        [HttpGet("sent/{id}")]
        public async Task<IActionResult> GetSentRequest(Guid id)
        {
            var result = await _circleNetwork.GetSentRequest(id);
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
        public async Task<IActionResult> SendConnectionRequest([FromBody] ConnectionRequestHeader requestHeader)
        {
            await _circleNetwork.SendConnectionRequest(requestHeader);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpPost("pending/accept/{id}")]
        public async Task<IActionResult> AcceptConnectionRequest(Guid id)
        {
            await _circleNetwork.AcceptConnectionRequest(id);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpDelete("pending/{id}")]
        public async Task<IActionResult> DeletePending(Guid id)
        {
            await _circleNetwork.DeletePendingRequest(id);
            return new JsonResult(new NoResultResponse(true));
        }
        
        [HttpGet("profile/{dotYouId}")]
        public async Task<IActionResult> GetDotYouProfile(string dotYouId)
        {
            var result = await _circleNetwork.GetProfile(dotYouId);
            return new JsonResult(result);
        }

    }
}
