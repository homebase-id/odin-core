using System;
using System.Linq;
using System.Net;
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
        TenantSystemStorage tenantSystemStorage,
        CircleNetworkIntroductionService introductionService)
        : OdinControllerBase
    {
<<<<<<< HEAD
=======
        private readonly CircleNetworkRequestService _requestService;
        private readonly TenantSystemStorage _tenantSystemStorage;


        public CircleNetworkRequestsControllerBase(CircleNetworkRequestService db, TenantSystemStorage tenantSystemStorage)
        {
            _requestService = db;
            _tenantSystemStorage = tenantSystemStorage;
        }

>>>>>>> main
        /// <summary>
        /// Gets a list of connection requests that are awaiting a response
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        [SwaggerOperation(Tags = new[] { ControllerConstants.Circles })]
        [HttpGet("pending/list")]
        public async Task<PagedResult<PendingConnectionRequestHeader>> GetPendingRequestList(int pageNumber, int pageSize)
        {
<<<<<<< HEAD
            using var cn = tenantSystemStorage.CreateConnection();
            var result = await circleNetworkRequestService.GetPendingRequests(new PageOptions(pageNumber, pageSize), WebOdinContext, cn);
=======
            var db = _tenantSystemStorage.IdentityDatabase;
            var result = await _requestService.GetPendingRequests(new PageOptions(pageNumber, pageSize), WebOdinContext, db);
>>>>>>> main
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
<<<<<<< HEAD
            using var cn = tenantSystemStorage.CreateConnection();
            var result = await circleNetworkRequestService.GetPendingRequest(id, WebOdinContext, cn);
=======
            var db = _tenantSystemStorage.IdentityDatabase;
            var result = await _requestService.GetPendingRequest(id, WebOdinContext, db);
>>>>>>> main

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
<<<<<<< HEAD
            using var cn = tenantSystemStorage.CreateConnection();
            await circleNetworkRequestService.AcceptConnectionRequest(header, tryOverrideAcl: false, WebOdinContext, cn);
=======
            var db = _tenantSystemStorage.IdentityDatabase;
            await _requestService.AcceptConnectionRequest(header, WebOdinContext, db);
>>>>>>> main
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
<<<<<<< HEAD
            using var cn = tenantSystemStorage.CreateConnection();
            await circleNetworkRequestService.DeletePendingRequest(id, WebOdinContext, cn);
=======
            var db = _tenantSystemStorage.IdentityDatabase;
            await _requestService.DeletePendingRequest(id, WebOdinContext, db);
>>>>>>> main
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
<<<<<<< HEAD
            using var cn = tenantSystemStorage.CreateConnection();
            var result = await circleNetworkRequestService.GetSentRequests(new PageOptions(pageNumber, pageSize), WebOdinContext, cn);
=======
            var db = _tenantSystemStorage.IdentityDatabase;
            var result = await _requestService.GetSentRequests(new PageOptions(pageNumber, pageSize), WebOdinContext, db);
>>>>>>> main
            var resp = result.Results.Select(r => ConnectionRequestResponse.FromConnectionRequest(r, ConnectionRequestDirection.Outgoing)).ToList();
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
<<<<<<< HEAD
            using var cn = tenantSystemStorage.CreateConnection();
            var result = await circleNetworkRequestService.GetSentRequest(id, WebOdinContext, cn);
=======
            var db = _tenantSystemStorage.IdentityDatabase;
            var result = await _requestService.GetSentRequest(id, WebOdinContext, db);
>>>>>>> main
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
<<<<<<< HEAD
            using var cn = tenantSystemStorage.CreateConnection();
            await circleNetworkRequestService.DeleteSentRequest(id, WebOdinContext, cn);
=======
            var db = _tenantSystemStorage.IdentityDatabase;
            await _requestService.DeleteSentRequest(id, WebOdinContext, db);
>>>>>>> main
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

<<<<<<< HEAD
            using var cn = tenantSystemStorage.CreateConnection();
            await circleNetworkRequestService.SendConnectionRequest(requestHeader, WebOdinContext, cn);
=======
            var db = _tenantSystemStorage.IdentityDatabase;
            await _requestService.SendConnectionRequest(requestHeader, WebOdinContext, db);
>>>>>>> main
            return true;
        }

        [HttpPost("introductions/send-introductions")]
        public async Task<IActionResult> SendIntroductions([FromBody] IntroductionGroup group)
        {
            OdinValidationUtils.AssertNotNull(group, nameof(group));
            OdinValidationUtils.AssertValidRecipientList(group.Recipients);

            using var cn = tenantSystemStorage.CreateConnection();
            var result = await introductionService.SendIntroductions(group, WebOdinContext, cn);
            return new JsonResult(result);
        }

        [HttpPost("introductions/process-incoming-introductions")]
        public async Task<IActionResult> ProcessIncomingIntroductions()
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await introductionService.SendOutstandingConnectionRequests(WebOdinContext, cn);
            return new OkResult();
        }

        [HttpPost("introductions/auto-accept-eligible-introductions")]
        public async Task<IActionResult> AutoAcceptEligibleIntroductions()
        {
            using var cn = tenantSystemStorage.CreateConnection();
            await introductionService.AutoAcceptEligibleConnectionRequests(WebOdinContext, cn);
            return new OkResult();
        }


        [HttpGet("introductions/received")]
        public async Task<IActionResult> GetReceivedIntroductions()
        {
            using var cn = tenantSystemStorage.CreateConnection();
            var list = await introductionService.GetReceivedIntroductions(WebOdinContext, cn);
            return new JsonResult(list);
        }

        [HttpDelete("introductions")]
        public async Task<IActionResult> DeleteAllIntroductions()
        {
            using var cn = tenantSystemStorage.CreateConnection();
             await introductionService.DeleteIntroductions(WebOdinContext, cn);
            return Ok();
        }
    }
}