using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Services.Base;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.Base.Membership.Connections
{
    public abstract class CircleNetworkRequestsControllerBase(
        CircleNetworkRequestService circleNetworkRequestService,
        CircleNetworkIntroductionService introductionService)
        : OdinControllerBase
    {
        /// <summary>
        /// Gets a list of connection requests that are awaiting a response
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.Circles })]
        [HttpGet("pending/list")]
        public async Task<PagedResult<PendingConnectionRequestHeader>> GetPendingRequestList(int pageNumber, int pageSize)
        {
            var result = await circleNetworkRequestService.GetPendingRequestsAsync(new PageOptions(pageNumber, pageSize), WebOdinContext);
            return result;
            // var resp = result.Results.Select(ConnectionRequestResponse.FromConnectionRequest).ToList();
            // return new PagedResult<PendingConnectionRequestHeader>(result.Request, result.TotalPages, resp);
        }

        /// <summary>
        /// Gets a connection request by sender that is awaiting a response from the recipient
        /// </summary>
        /// <param name="sender"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.Circles })]
        [HttpPost("pending/single")]
        public async Task<ConnectionRequestResponse> GetPendingRequest([FromBody] OdinIdRequest sender)
        {
            AssertIsValidOdinId(sender.OdinId, out var id);
            var result = await circleNetworkRequestService.GetPendingRequestAsync(id, WebOdinContext);

            if (result == null)
            {
                this.HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return null;
            }

            return ConnectionRequestResponse.FromConnectionRequest(result, ConnectionRequestDirection.Incoming);
        }

        /// <summary>
        /// Accepts a pending connection request
        /// </summary>
        /// <param name="header"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.Circles })]
        [HttpPost("pending/accept")]
        public async Task<bool> AcceptConnectionRequest([FromBody] AcceptRequestHeader header)
        {
            OdinValidationUtils.AssertNotNull(header, nameof(header));
            header.Validate();
            await circleNetworkRequestService.AcceptConnectionRequestAsync(header, tryOverrideAcl: false, WebOdinContext);
            return true;
        }

        /// <summary>
        /// Deletes a pending connection request
        /// </summary>
        /// <param name="sender"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.Circles })]
        [HttpPost("pending/delete")]
        public async Task<bool> DeletePendingRequest([FromBody] OdinIdRequest sender)
        {
            AssertIsValidOdinId(sender.OdinId, out var id);
            await circleNetworkRequestService.DeletePendingRequest(id, WebOdinContext);
            return true;
        }

        /// <summary>
        /// Gets a list of sent connection requests
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.Circles })]
        [HttpGet("sent/list")]
        public async Task<PagedResult<ConnectionRequestResponse>> GetSentRequestList(int pageNumber, int pageSize)
        {
            var result = await circleNetworkRequestService.GetSentRequestsAsync(new PageOptions(pageNumber, pageSize), WebOdinContext);
            var resp = result.Results.Select(r => ConnectionRequestResponse.FromConnectionRequest(r, ConnectionRequestDirection.Outgoing))
                .ToList();
            return new PagedResult<ConnectionRequestResponse>(result.Request, result.TotalPages, resp);
        }

        /// <summary>
        /// Gets a sent connection request by recipient
        /// </summary>
        /// <param name="recipient"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.Circles })]
        [HttpPost("sent/single")]
        public async Task<ConnectionRequestResponse> GetSentRequest([FromBody] OdinIdRequest recipient)
        {
            AssertIsValidOdinId(recipient.OdinId, out var id);

            var result = await circleNetworkRequestService.GetSentRequestAsync(id, WebOdinContext);
            if (result == null)
            {
                this.HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return null;
            }

            return ConnectionRequestResponse.FromConnectionRequest(result, ConnectionRequestDirection.Outgoing);
        }

        /// <summary>
        /// Deletes a connection request sent to the specified recipient.
        /// </summary>
        /// <param name="recipient"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.Circles })]
        [HttpPost("sent/delete")]
        public async Task<bool> DeleteSentRequest([FromBody] OdinIdRequest recipient)
        {
            AssertIsValidOdinId(recipient.OdinId, out var id);

            await circleNetworkRequestService.DeleteSentRequest(id, WebOdinContext);
            return true;
        }

        /// <summary>
        /// Sends a connection request.
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.Circles })]
        [HttpPost("sendrequest")]
        public async Task<bool> SendConnectionRequest([FromBody] ConnectionRequestHeader requestHeader)
        {
            OdinValidationUtils.AssertNotNull(requestHeader, nameof(requestHeader));
            OdinValidationUtils.AssertIsTrue(requestHeader.Id != Guid.Empty, "Invalid Id");
            OdinValidationUtils.AssertIsValidOdinId(requestHeader.Recipient, out _);


            await circleNetworkRequestService.SendConnectionRequestAsync(requestHeader, HttpContext.RequestAborted, WebOdinContext);
            return true;
        }

        [HttpPost("introductions/send-introductions")]
        public async Task<IActionResult> SendIntroductions([FromBody] IntroductionGroup group)
        {
            OdinValidationUtils.AssertNotNull(group, nameof(group));
            OdinValidationUtils.AssertValidRecipientList(group.Recipients);

            
            var result = await introductionService.SendIntroductions(group, WebOdinContext);
            return new JsonResult(result);
        }

        [HttpPost("introductions/process-incoming-introductions")]
        public async Task<IActionResult> ProcessIncomingIntroductions()
        {
            await introductionService.SendOutstandingConnectionRequestsAsync(WebOdinContext, HttpContext.RequestAborted);
            return new OkResult();
        }

        [HttpPost("introductions/auto-accept-eligible-introductions")]
        public async Task<IActionResult> AutoAcceptEligibleIntroductions()
        {
            await introductionService.ForceAutoAcceptEligibleConnectionRequestsAsync(WebOdinContext, HttpContext.RequestAborted);
            return new OkResult();
        }


        [HttpGet("introductions/received")]
        public async Task<IActionResult> GetReceivedIntroductions()
        {
            
            var list = await introductionService.GetReceivedIntroductionsAsync(WebOdinContext);
            return new JsonResult(list);
        }

        [HttpDelete("introductions")]
        public async Task<IActionResult> DeleteAllIntroductions()
        {
            
            await introductionService.DeleteIntroductionsAsync(WebOdinContext);
            return Ok();
        }
    }
}