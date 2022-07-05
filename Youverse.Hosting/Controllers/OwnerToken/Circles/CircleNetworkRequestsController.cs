using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle.Requests;

namespace Youverse.Hosting.Controllers.OwnerToken.Circles
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/requests")]
    [AuthorizeValidOwnerToken]
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

        [HttpGet("pending/{senderDotYouId}")]
        public async Task<IActionResult> GetPendingRequest(string senderDotYouId)
        {
            var result = await _requestService.GetPendingRequest((DotYouIdentity)senderDotYouId);

            if (result == null)
            {
                return new JsonResult(new NoResultResponse(true))
                {
                    StatusCode = (int)HttpStatusCode.NotFound
                };
            }

            return new JsonResult(ConnectionRequestResponse.FromConnectionRequest(result));
        }

        [HttpGet("sent")]
        public async Task<PagedResult<ConnectionRequestResponse>> GetSentRequests(int pageNumber, int pageSize)
        {
            var result = await _requestService.GetSentRequests(new PageOptions(pageNumber, pageSize));
            var resp = result.Results.Select(ConnectionRequestResponse.FromConnectionRequest).ToList();
            return new PagedResult<ConnectionRequestResponse>(result.Request, result.TotalPages, resp);

        }

        [HttpGet("sent/{recipientDotYouId}")]
        public async Task<IActionResult> GetSentRequest(string recipientDotYouId)
        {
            var result = await _requestService.GetSentRequest((DotYouIdentity)recipientDotYouId);
            if (result == null)
            {
                return new JsonResult(new NoResultResponse(true))
                {
                    StatusCode = (int)HttpStatusCode.NotFound
                };
            }

            return new JsonResult(ConnectionRequestResponse.FromConnectionRequest(result));
        }

        [HttpDelete("sent/{recipientDotYouId}")]
        public async Task<IActionResult> DeleteSentRequest(string recipientDotYouId)
        {
            await _requestService.DeleteSentRequest((DotYouIdentity)recipientDotYouId);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpPost("sent")]
        public async Task<IActionResult> SendConnectionRequest([FromBody] ConnectionRequestHeader requestHeader)
        {
            await _requestService.SendConnectionRequest(requestHeader);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpPost("pending/accept")]
        public async Task<IActionResult> AcceptConnectionRequest([FromBody] AcceptRequestHeader header)
        {
            await _requestService.AcceptConnectionRequest((DotYouIdentity)header.Sender, header.Drives, header.Permissions);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpDelete("pending/{senderDotYouId}")]
        public async Task<IActionResult> DeletePendingRequest(string senderDotYouId)
        {
            await _requestService.DeletePendingRequest((DotYouIdentity)senderDotYouId);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}