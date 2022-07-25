using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
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
        
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpGet("pending")]
        public async Task<PagedResult<ConnectionRequestResponse>> GetPendingRequests(int pageNumber, int pageSize)
        {
            var result = await _requestService.GetPendingRequests(new PageOptions(pageNumber, pageSize));
            var resp = result.Results.Select(ConnectionRequestResponse.FromConnectionRequest).ToList();
            return new PagedResult<ConnectionRequestResponse>(result.Request, result.TotalPages, resp);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("pending")]
        public async Task<IActionResult> GetPendingRequest([FromBody]DotYouIdRequest request)
        {
            var result = await _requestService.GetPendingRequest((DotYouIdentity)request.DotYouId);

            if (result == null)
            {
                return new JsonResult(new NoResultResponse(true))
                {
                    StatusCode = (int)HttpStatusCode.NotFound
                };
            }

            return new JsonResult(ConnectionRequestResponse.FromConnectionRequest(result));
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("pending/accept")]
        public async Task<IActionResult> AcceptConnectionRequest([FromBody] AcceptRequestHeader header)
        {
            await _requestService.AcceptConnectionRequest((DotYouIdentity)header.Sender, header.Drives, header.Permissions);
            return new JsonResult(new NoResultResponse(true));
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("pending/delete")]
        public async Task<IActionResult> DeletePendingRequest([FromBody]DotYouIdRequest request)
        {
            await _requestService.DeletePendingRequest((DotYouIdentity)request.DotYouId);
            return new JsonResult(new NoResultResponse(true));
        }
        
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpGet("sent")]
        public async Task<PagedResult<ConnectionRequestResponse>> GetSentRequests(int pageNumber, int pageSize)
        {
            var result = await _requestService.GetSentRequests(new PageOptions(pageNumber, pageSize));
            var resp = result.Results.Select(ConnectionRequestResponse.FromConnectionRequest).ToList();
            return new PagedResult<ConnectionRequestResponse>(result.Request, result.TotalPages, resp);

        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("sent")]
        public async Task<IActionResult> GetSentRequest([FromBody]DotYouIdRequest request)
        {
            var result = await _requestService.GetSentRequest((DotYouIdentity)request.DotYouId);
            if (result == null)
            {
                return new JsonResult(new NoResultResponse(true))
                {
                    StatusCode = (int)HttpStatusCode.NotFound
                };
            }

            return new JsonResult(ConnectionRequestResponse.FromConnectionRequest(result));
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("sent/delete")]
        public async Task<IActionResult> DeleteSentRequest([FromBody]DotYouIdRequest request)
        {
            await _requestService.DeleteSentRequest((DotYouIdentity)request.DotYouId);
            return new JsonResult(new NoResultResponse(true));
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("sendrequest")]
        public async Task<IActionResult> SendConnectionRequest([FromBody] ConnectionRequestHeader requestHeader)
        {
            await _requestService.SendConnectionRequest(requestHeader);
            return new JsonResult(new NoResultResponse(true));
        }

    }
}