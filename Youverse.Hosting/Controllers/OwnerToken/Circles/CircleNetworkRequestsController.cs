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
        [HttpGet("pending/list")]
        public async Task<PagedResult<ConnectionRequestResponse>> GetPendingRequestList(int pageNumber, int pageSize)
        {
            var result = await _requestService.GetPendingRequests(new PageOptions(pageNumber, pageSize));
            var resp = result.Results.Select(ConnectionRequestResponse.FromConnectionRequest).ToList();
            return new PagedResult<ConnectionRequestResponse>(result.Request, result.TotalPages, resp);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("pending/single")]
        public async Task<ConnectionRequestResponse> GetPendingRequest([FromBody] DotYouIdRequest request)
        {
            var result = await _requestService.GetPendingRequest((DotYouIdentity)request.DotYouId);

            if (result == null)
            {
                this.HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return null;
            }

            return ConnectionRequestResponse.FromConnectionRequest(result);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("pending/accept")]
        public async Task<bool> AcceptConnectionRequest([FromBody] AcceptRequestHeader header)
        {
            await _requestService.AcceptConnectionRequest((DotYouIdentity)header.Sender, header.Drives, header.Permissions);
            return true;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("pending/delete")]
        public async Task<bool> DeletePendingRequest([FromBody] DotYouIdRequest request)
        {
            await _requestService.DeletePendingRequest((DotYouIdentity)request.DotYouId);
            return true;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpGet("sent/list")]
        public async Task<PagedResult<ConnectionRequestResponse>> GetSentRequestList(int pageNumber, int pageSize)
        {
            var result = await _requestService.GetSentRequests(new PageOptions(pageNumber, pageSize));
            var resp = result.Results.Select(ConnectionRequestResponse.FromConnectionRequest).ToList();
            return new PagedResult<ConnectionRequestResponse>(result.Request, result.TotalPages, resp);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("sent/single")]
        public async Task<ConnectionRequestResponse> GetSentRequest([FromBody] DotYouIdRequest request)
        {
            var result = await _requestService.GetSentRequest((DotYouIdentity)request.DotYouId);
            if (result == null)
            {
                this.HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return null;
            }

            return ConnectionRequestResponse.FromConnectionRequest(result);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("sent/delete")]
        public async Task<bool> DeleteSentRequest([FromBody] DotYouIdRequest request)
        {
            await _requestService.DeleteSentRequest((DotYouIdentity)request.DotYouId);
            return true;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCircles })]
        [HttpPost("sendrequest")]
        public async Task<bool> SendConnectionRequest([FromBody] ConnectionRequestHeader requestHeader)
        {
            await _requestService.SendConnectionRequest(requestHeader);
            return true;
        }
    }
}