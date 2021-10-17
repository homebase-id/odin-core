using System;
using System.Net;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Circle;
using DotYou.Types;
using DotYou.Types.Circle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.DigitalIdentityHost.Controllers
{
    [ApiController]
    [Route("api/circlenetwork/requests")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner)]
    public class CircleNetworkRequestsController : ControllerBase
    {
        readonly ICircleNetworkRequestService _requestService;

        public CircleNetworkRequestsController(ICircleNetworkRequestService cn)
        {
            _requestService = cn;
        }

        [HttpGet("pending")]
        public async Task<PagedResult<ConnectionRequest>> GetPendingRequests(int pageNumber, int pageSize)
        {
            var result = await _requestService.GetPendingRequests(new PageOptions(pageNumber, pageSize));
            return result;
        }

        [HttpGet("pending/{senderDotYouId}")]
        public async Task<IActionResult> GetPendingRequest(string senderDotYouId)
        {
            var result = await _requestService.GetPendingRequest((DotYouIdentity) senderDotYouId);

            if (result == null)
            {
                return new JsonResult(new NoResultResponse(true))
                {
                    StatusCode = (int) HttpStatusCode.NotFound
                };
            }

            return new JsonResult(result);
        }

        [HttpGet("sent")]
        public async Task<PagedResult<ConnectionRequest>> GetSentRequests(int pageNumber, int pageSize)
        {
            var result = await _requestService.GetSentRequests(new PageOptions(pageNumber, pageSize));
            return result;
        }

        [HttpGet("sent/{recipientDotYouId}")]
        public async Task<IActionResult> GetSentRequest(string recipientDotYouId)
        {
            var result = await _requestService.GetSentRequest((DotYouIdentity) recipientDotYouId);
            if (result == null)
            {
                return new JsonResult(new NoResultResponse(true))
                {
                    StatusCode = (int) HttpStatusCode.NotFound
                };
            }

            return new JsonResult(result);
        }

        [HttpDelete("sent/{recipientDotYouId}")]
        public async Task<IActionResult> DeleteSentRequest(string recipientDotYouId)
        { 
            await _requestService.DeleteSentRequest((DotYouIdentity) recipientDotYouId);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpPost("sent")]
        public async Task<IActionResult> SendConnectionRequest([FromBody] ConnectionRequestHeader requestHeader)
        {
            await _requestService.SendConnectionRequest(requestHeader);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpPost("pending/accept/{dotYouId}")]
        public async Task<IActionResult> AcceptConnectionRequest(string dotYouId)
        {
            await _requestService.AcceptConnectionRequest((DotYouIdentity) dotYouId);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpDelete("pending/{senderDotYouId}")]
        public async Task<IActionResult> DeletePendingRequest(string senderDotYouId)
        {
            await _requestService.DeletePendingRequest((DotYouIdentity) senderDotYouId);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}